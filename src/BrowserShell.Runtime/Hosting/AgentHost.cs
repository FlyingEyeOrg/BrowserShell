using System.IO;
using System.Security.Claims;
using System.Text.Json;
using System.Windows.Threading;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Serilog;
using Serilog.Formatting.Compact;
using BrowserShell.Service.SDK;

namespace BrowserShell.Runtime;

/// <summary>组装 BrowserShell 本机服务、实时通道和窗口基础设施。</summary>
internal sealed class AgentHost : IAsyncDisposable
{
    internal const string AuthenticationType = "BrowserShell.Token";
    private const string OwnerClaim = "softwarehub.desktop-agent.owner";
    private const string ServiceClaim = "softwarehub.desktop-agent.service";
    private const string ClientVersionClaim = "softwarehub.desktop-agent.client-version";
    private readonly WebApplication _application;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly WindowManager _windows;
    private readonly BrowserWindowManager _browserWindows;

    private AgentHost(WebApplication application, WindowManager windows, BrowserWindowManager browserWindows)
    {
        _application = application;
        _windows = windows;
        _browserWindows = browserWindows;
    }

    public static async Task<AgentHost> StartAsync(
        Dispatcher dispatcher,
        string? configurationFilePath,
        CancellationToken token)
    {
        var contentRootPath = Path.GetFullPath(AppContext.BaseDirectory);
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            Args = [],
            ContentRootPath = contentRootPath,
        });
        builder.Configuration.Sources.Clear();
        builder.Configuration
            .SetBasePath(contentRootPath)
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
            .AddJsonFile(
                $"appsettings.{builder.Environment.EnvironmentName}.json",
                optional: true,
                reloadOnChange: false);
        if (!string.IsNullOrWhiteSpace(configurationFilePath))
        {
            if (!string.Equals(Path.GetExtension(configurationFilePath), ".json", StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException("BrowserShell Runtime 配置文件必须是 JSON 文件。", nameof(configurationFilePath));
            }
            builder.Configuration.AddJsonFile(
                Path.GetFullPath(configurationFilePath),
                optional: false,
                reloadOnChange: false);
        }
        var settings = builder.Configuration.GetSection("BrowserShell").Get<RuntimeSettings>()
            ?? throw new InvalidOperationException("JSON 配置缺少 BrowserShell 配置节。");
        if (settings.AuthenticationMode != RuntimeAuthenticationMode.EncryptedNetworkOAuth)
        {
            throw new InvalidOperationException(
                "BrowserShell 仅支持 EncryptedNetworkOAuth；PlainHttpHmacControlPlane 不支持 BrowserShell 链路。");
        }
        ValidateLoopbackEndpoint(settings.Endpoint);
        builder.WebHost.UseUrls(settings.Endpoint);
        builder.Host.UseSerilog((_, _, logging) => logging.Enrich.FromLogContext()
            .Enrich.WithProperty("Component", "BrowserShell.Runtime")
            .Enrich.WithProperty("AgentId", settings.AgentId)
            .WriteTo.Console(new CompactJsonFormatter())
            .WriteTo.File(new CompactJsonFormatter(), Path.Combine(Path.GetFullPath(settings.LogDirectory), "desktop-agent-.log"),
                rollingInterval: RollingInterval.Day, retainedFileCountLimit: 15));
        builder.Services.AddSingleton(settings);
        builder.Services.AddSingleton(dispatcher);
        builder.Services.AddSingleton<AgentAuthorizationStore>();
        builder.Services.AddSingleton<AgentClientSessionRegistry>();
        builder.Services.AddSingleton<SnapshotResourceCoordinator>();
        builder.Services.AddSingleton<WindowSnapshotAssembler>();
        builder.Services.AddSingleton<AgentInteractionClient>();
        builder.Services.AddSingleton<AgentSystemWarningWindowManager>();
        builder.Services.AddSingleton<WindowRegistry>();
        builder.Services.AddSingleton<WebView2InitializationCoordinator>();
        builder.Services.AddSingleton<WebView2EnvironmentProvider>();
        builder.Services.AddSingleton<WindowManager>();
        builder.Services.AddSingleton<BrowserWindowManager>();
        builder.Services.AddHostedService<SessionLeaseMonitor>();
        builder.Services.AddSignalR(options =>
        {
            options.MaximumReceiveMessageSize = 384 * 1024;
            options.MaximumParallelInvocationsPerClient = 2;
        });
        builder.Services.AddProblemDetails();
        var app = builder.Build();
        app.Use(async (context, next) =>
        {
            var header = context.Request.Headers.Authorization.ToString();
            if (header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            {
                var store = context.RequestServices.GetRequiredService<AgentAuthorizationStore>();
                var auth = store.AuthenticateAccessToken(header["Bearer ".Length..].Trim());
                if (auth is not null) context.User = CreatePrincipal(auth);
            }
            await next(context);
        });
        app.UseExceptionHandler();
        var infrastructureReady = 0;
        MapApi(app, settings, () => Volatile.Read(ref infrastructureReady) == 1);
        app.MapHub<BrowserShellHub>("/api/v1/hubs/clients");
        await app.StartAsync(token);
        var webViewEnvironment = app.Services.GetRequiredService<WebView2EnvironmentProvider>();
        await webViewEnvironment.InitializeAsync(token);
        var windows = app.Services.GetRequiredService<WindowManager>();
        await windows.InitializeAsync(token);
        Interlocked.Exchange(ref infrastructureReady, 1);
        var browserWindows = app.Services.GetRequiredService<BrowserWindowManager>();
        var host = new AgentHost(app, windows, browserWindows);
        if (settings.ParentProcessId > 0) host.WatchParent(settings.ParentProcessId);
        var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()?.Addresses.FirstOrDefault();
        Log.Information("AgentInfrastructureReady at {Endpoint}", address);
        return host;
    }

    private static void MapApi(WebApplication app, RuntimeSettings settings, Func<bool> isInfrastructureReady)
    {
        var api = app.MapGroup("/api/v1");
        api.MapGet("/agent", () => Results.Ok(new AgentDescriptor(settings.AgentId, "2.0", true, DateTimeOffset.UtcNow)));
        api.MapGet("/agent/readiness", () => isInfrastructureReady()
            ? Results.Ok(new AgentReadiness("ready", true, DateTimeOffset.UtcNow))
            : Results.Json(new AgentReadiness("starting", false, DateTimeOffset.UtcNow), statusCode: 503));
        api.MapPost("/auth/token", async (HttpContext context, AgentAuthorizationStore store) =>
        {
            if (!context.Request.HasFormContentType) return Problem(400, "客户端凭证必须使用表单编码。");
            var form = await context.Request.ReadFormAsync(context.RequestAborted);
            if (!string.Equals(form["grant_type"], "client_credentials", StringComparison.Ordinal))
                return Problem(400, "只支持 client_credentials。");
            var issued = store.IssueAccessToken(form["client_id"], form["client_secret"]);
            return issued is null ? Problem(401, "客户端凭证无效。") : Results.Ok(issued);
        });
        api.MapPost("/clients", (AgentClientWrite body, HttpContext context, AgentAuthorizationStore store) =>
            PutClient(Guid.NewGuid().ToString("N"), body, context, store));
        api.MapPut("/clients/{id}", (string id, AgentClientWrite body, HttpContext context, AgentAuthorizationStore store) =>
            PutClient(id, body, context, store));
        api.MapGet("/clients/{id}", (string id, HttpContext context, AgentAuthorizationStore store) =>
        {
            if (RequireOwner(context, store) is { } failure) return failure;
            return store.TryGetClient(id, out var client) ? Results.Ok(ToContract(client)) : Problem(404, "客户端不存在。");
        });
        api.MapDelete("/clients/{id}", (string id, HttpContext context, AgentAuthorizationStore store) =>
        {
            if (RequireOwner(context, store) is { } failure) return failure;
            return store.RevokeClient(id) ? Results.NoContent() : Problem(404, "客户端不存在。");
        });
    }

    private static IResult PutClient(string id, AgentClientWrite body, HttpContext context, AgentAuthorizationStore store)
    {
        if (RequireOwner(context, store) is { } failure) return failure;
        if (string.IsNullOrWhiteSpace(body.ClientSecret)) return Problem(422, "ClientSecret 不能为空。");
        var result = store.PutClient(id, body);
        return Results.Json(ToContract(result.Entry), statusCode: result.Created ? 201 : 200);
    }

    private static AgentClient ToContract(ClientEntry item) =>
        new(item.ClientId, item.ServiceInstanceIds, item.ExpiresAt, item.Revoked);

    private static IResult? RequireOwner(HttpContext context, AgentAuthorizationStore store)
    {
        var auth = ReadAuthContext(context.User) ?? store.Authenticate(
            context.Request.Headers["X-SoftwareHub-Client-Id"],
            context.Request.Headers["X-SoftwareHub-Client-Secret"]);
        if (auth is null) return Problem(401, "需要身份认证。");
        return auth.Owner ? null : Problem(403, "需要 Agent 所有者凭证。");
    }

    private static ClaimsPrincipal CreatePrincipal(AuthContext auth)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, auth.ClientId),
            new(OwnerClaim, auth.Owner ? "true" : "false"),
            new(ClientVersionClaim, auth.ClientVersion.ToString(System.Globalization.CultureInfo.InvariantCulture)),
        };
        claims.AddRange(auth.ServiceInstanceIds.Select(id => new Claim(ServiceClaim, id)));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, AuthenticationType));
    }

    internal static AuthContext? ReadAuthContext(ClaimsPrincipal principal)
    {
        var clientId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(clientId)
            || !long.TryParse(principal.FindFirstValue(ClientVersionClaim), out var version)) return null;
        return new AuthContext(clientId,
            string.Equals(principal.FindFirstValue(OwnerClaim), "true", StringComparison.Ordinal),
            principal.FindAll(ServiceClaim).Select(item => item.Value).ToArray(), version);
    }

    private static void ValidateLoopbackEndpoint(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var endpoint) || !endpoint.IsLoopback)
            throw new InvalidOperationException("BrowserShell 只能监听本机回环地址。");
    }

    private static IResult Problem(int status, string detail) => Results.Problem(statusCode: status, detail: detail);

    private void WatchParent(int processId) => _ = Task.Run(async () =>
    {
        try { using var process = System.Diagnostics.Process.GetProcessById(processId); await process.WaitForExitAsync(_lifetime.Token); }
        catch (ArgumentException) { }
        catch (OperationCanceledException) { return; }
        System.Windows.Application.Current.Dispatcher.Invoke(() => System.Windows.Application.Current.Shutdown());
    });

    public async ValueTask DisposeAsync()
    {
        _lifetime.Cancel();
        await _application.StopAsync();
        await _browserWindows.DisposeAsync();
        await _windows.DisposeAsync();
        await _application.DisposeAsync();
        _lifetime.Dispose();
    }
}

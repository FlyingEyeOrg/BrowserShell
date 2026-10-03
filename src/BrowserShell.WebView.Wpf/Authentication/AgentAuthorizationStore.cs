using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using BrowserShell.Service.SDK;

namespace BrowserShell.WebView.Wpf;

/// <summary>管理 Agent 当前进程中的客户端凭证、短期令牌和授权范围。</summary>
internal sealed class AgentAuthorizationStore
{
    private static readonly TimeSpan AccessTokenLifetime = TimeSpan.FromMinutes(15);
    private readonly ConcurrentDictionary<string, ClientEntry> _clients = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, AccessTokenEntry> _tokens = new(StringComparer.Ordinal);
    private long _clientVersion;

    public AgentAuthorizationStore(RuntimeSettings settings)
    {
        if (!string.IsNullOrWhiteSpace(settings.AdminClientSecret))
        {
            _clients[settings.AdminClientId] = CreateEntry(
                settings.AdminClientId,
                settings.AdminClientSecret,
                [],
                owner: true,
                expiresAt: null);
        }

        foreach (var client in settings.Clients)
        {
            if (string.IsNullOrWhiteSpace(client.ClientId)
                || string.IsNullOrWhiteSpace(client.ClientSecret))
            {
                continue;
            }

            _clients[client.ClientId] = CreateEntry(
                client.ClientId,
                client.ClientSecret,
                client.ServiceInstanceIds,
                owner: false,
                expiresAt: null);
        }
    }

    public AuthContext? Authenticate(string? clientId, string? clientSecret)
    {
        if (string.IsNullOrWhiteSpace(clientId)
            || string.IsNullOrWhiteSpace(clientSecret)
            || !_clients.TryGetValue(clientId, out var client)
            || client.Revoked
            || client.ExpiresAt <= DateTimeOffset.UtcNow)
        {
            return null;
        }

        var expected = Convert.FromHexString(client.SecretHash);
        var actual = SHA256.HashData(Encoding.UTF8.GetBytes(clientSecret));
        return CryptographicOperations.FixedTimeEquals(expected, actual)
            ? ToContext(client)
            : null;
    }

    public AgentAccessToken? IssueAccessToken(string? clientId, string? clientSecret)
    {
        var context = Authenticate(clientId, clientSecret);
        if (context is null)
        {
            return null;
        }

        RemoveExpiredTokens();
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        _tokens[token] = new AccessTokenEntry(context, DateTimeOffset.UtcNow.Add(AccessTokenLifetime));
        return new AgentAccessToken(token, "Bearer", (int)AccessTokenLifetime.TotalSeconds);
    }

    public AuthContext? AuthenticateAccessToken(string? token)
    {
        if (string.IsNullOrWhiteSpace(token) || !_tokens.TryGetValue(token, out var entry))
        {
            return null;
        }

        if (entry.ExpiresAt <= DateTimeOffset.UtcNow
            || !IsAuthorizedContextCurrent(entry.Context))
        {
            _tokens.TryRemove(token, out _);
            return null;
        }

        return entry.Context;
    }

    public bool IsAuthorized(AuthContext context, string serviceInstanceId)
    {
        if (!_clients.TryGetValue(context.ClientId, out var client)
            || client.Revoked
            || client.ExpiresAt <= DateTimeOffset.UtcNow
            || client.Version != context.ClientVersion)
        {
            return false;
        }

        return client.Owner
            || client.ServiceInstanceIds.Contains(serviceInstanceId, StringComparer.Ordinal);
    }

    public (ClientEntry Entry, bool Created) PutClient(string clientId, AgentClientWrite value)
    {
        var created = !_clients.ContainsKey(clientId);
        var entry = CreateEntry(
            clientId,
            value.ClientSecret,
            value.ServiceInstanceIds,
            owner: false,
            value.ExpiresAt);
        _clients[clientId] = entry;
        RevokeTokens(clientId);
        return (entry, created);
    }

    public bool TryGetClient(string clientId, out ClientEntry entry) =>
        _clients.TryGetValue(clientId, out entry!);

    public bool RevokeClient(string clientId)
    {
        var removed = _clients.TryRemove(clientId, out _);
        RevokeTokens(clientId);
        return removed;
    }

    private ClientEntry CreateEntry(
        string clientId,
        string clientSecret,
        IReadOnlyList<string> serviceInstanceIds,
        bool owner,
        DateTimeOffset? expiresAt) =>
        new(
            clientId,
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(clientSecret))),
            [.. serviceInstanceIds],
            expiresAt,
            Revoked: false,
            Owner: owner,
            Version: Interlocked.Increment(ref _clientVersion));

    private bool IsAuthorizedContextCurrent(AuthContext context) =>
        _clients.TryGetValue(context.ClientId, out var client)
        && !client.Revoked
        && (client.ExpiresAt is null || client.ExpiresAt > DateTimeOffset.UtcNow)
        && client.Version == context.ClientVersion;

    private static AuthContext ToContext(ClientEntry client) =>
        new(client.ClientId, client.Owner, client.ServiceInstanceIds, client.Version);

    private void RevokeTokens(string clientId)
    {
        foreach (var token in _tokens
                     .Where(item => string.Equals(
                         item.Value.Context.ClientId,
                         clientId,
                         StringComparison.Ordinal))
                     .Select(item => item.Key)
                     .ToArray())
        {
            _tokens.TryRemove(token, out _);
        }
    }

    private void RemoveExpiredTokens()
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var token in _tokens
                     .Where(item => item.Value.ExpiresAt <= now)
                     .Select(item => item.Key)
                     .ToArray())
        {
            _tokens.TryRemove(token, out _);
        }
    }
}

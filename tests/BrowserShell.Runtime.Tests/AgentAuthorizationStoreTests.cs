using BrowserShell.Service.SDK;
using BrowserShell.Runtime;

namespace BrowserShell.Runtime.Tests;

public sealed class AgentAuthorizationStoreTests
{
    [Fact]
    public void 非过期凭证可以授权指定服务()
    {
        var store = new AgentAuthorizationStore(new RuntimeSettings
        {
            Clients = [new InitialClientSettings { ClientId = "client", ClientSecret = "secret", ServiceInstanceIds = ["service-1"] }],
        });

        var context = store.Authenticate("client", "secret");

        Assert.NotNull(context);
        Assert.True(store.IsAuthorized(context, "service-1"));
        Assert.False(store.IsAuthorized(context, "service-2"));
    }

    [Fact]
    public void 更新凭证后旧访问令牌立即失效()
    {
        var store = new AgentAuthorizationStore(new RuntimeSettings
        {
            AdminClientId = "owner", AdminClientSecret = "owner-secret",
            Clients = [new InitialClientSettings { ClientId = "client", ClientSecret = "secret", ServiceInstanceIds = ["service-1"] }],
        });
        var token = store.IssueAccessToken("client", "secret")!;

        store.PutClient("client", new AgentClientWrite("new-secret", ["service-1"], null));

        Assert.Null(store.AuthenticateAccessToken(token.AccessToken));
    }
}

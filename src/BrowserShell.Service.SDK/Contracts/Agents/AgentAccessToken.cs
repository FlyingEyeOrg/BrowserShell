using System.Text.Json.Serialization;

namespace BrowserShell.Service.SDK;

/// <summary>BrowserShell 客户端凭证交换得到的短期访问令牌。</summary>
internal sealed record AgentAccessToken(
    [property: JsonPropertyName("access_token")] string AccessToken,
    [property: JsonPropertyName("token_type")] string TokenType,
    [property: JsonPropertyName("expires_in")] int ExpiresIn);

using System.Text.Json.Serialization;

namespace BrowserShell.Runtime;

internal sealed record TokenEnvelope(
    [property: JsonPropertyName("access_token")] string AccessToken);

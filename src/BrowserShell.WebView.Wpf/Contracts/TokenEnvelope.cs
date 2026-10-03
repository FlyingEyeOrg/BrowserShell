using System.Text.Json.Serialization;

namespace BrowserShell.WebView.Wpf;

internal sealed record TokenEnvelope(
    [property: JsonPropertyName("access_token")] string AccessToken);

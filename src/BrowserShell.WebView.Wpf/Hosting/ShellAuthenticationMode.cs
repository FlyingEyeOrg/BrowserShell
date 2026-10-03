namespace BrowserShell.WebView.Wpf;

/// <summary>BrowserShell Runtime 支持的传输认证模式。</summary>
public enum ShellAuthenticationMode
{
    /// <summary>回环或外部加密网络上的资源本地 OAuth Client Credentials Profile。</summary>
    EncryptedNetworkOAuth = 0,

    /// <summary>普通 HTTP 上仅用于控制面的逐请求 HMAC。</summary>
    PlainHttpHmacControlPlane = 1,
}

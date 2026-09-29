namespace WordBubble;

public static class NavigationPolicy
{
    public const string EntryUrl = "https://www.maimemo.com/home/web_study";
    public const string LoginUrl = "https://www.maimemo.com/home/login?continue=home/web_study";
    public static bool IsLoginPage(string? address) => Uri.TryCreate(address, UriKind.Absolute, out var uri) &&
        uri.Scheme == "https" && uri.IsDefaultPort && uri.UserInfo.Length == 0 &&
        uri.IdnHost == "www.maimemo.com" && uri.AbsolutePath.TrimEnd('/') == "/home/login";
    public static bool IsStudyPage(string? address) => Uri.TryCreate(address, UriKind.Absolute, out var uri) &&
        uri.Scheme == "https" && uri.IsDefaultPort && uri.UserInfo.Length == 0 &&
        uri.IdnHost == "tc-apis.maimemo.com" &&
        (uri.AbsolutePath == "/webstudy/app" || uri.AbsolutePath.StartsWith("/webstudy/app/", StringComparison.Ordinal));
    public static bool IsOfficial(string? address)
    {
        if (!Uri.TryCreate(address, UriKind.Absolute, out var uri)) return false;
        return uri.Scheme == Uri.UriSchemeHttps && uri.IsDefaultPort && uri.UserInfo.Length == 0 &&
            (uri.IdnHost.Equals("maimemo.com", StringComparison.OrdinalIgnoreCase) ||
             uri.IdnHost.EndsWith(".maimemo.com", StringComparison.OrdinalIgnoreCase));
    }
    public static bool CanOpenExternally(string? address) =>
        Uri.TryCreate(address, UriKind.Absolute, out var uri) && uri.Scheme == "https" && uri.UserInfo.Length == 0;
}

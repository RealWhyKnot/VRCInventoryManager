namespace VRCInventoryManager.Core;

internal sealed record VrcxAuthCookies(string Auth, string TwoFactorAuth)
{
    public string ToCookieHeader() => $"auth={Auth}; twoFactorAuth={TwoFactorAuth}";
}

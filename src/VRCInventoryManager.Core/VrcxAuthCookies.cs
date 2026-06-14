namespace VRCInventoryManager.Core;

public sealed record VrcxAuthCookies(string Auth, string TwoFactorAuth)
{
    public string ToCookieHeader() => $"auth={Auth}; twoFactorAuth={TwoFactorAuth}";
}

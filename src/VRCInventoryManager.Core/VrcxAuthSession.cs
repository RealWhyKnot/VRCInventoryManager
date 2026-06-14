namespace VRCInventoryManager.Core;

public sealed record VrcxAuthSession(VrcxAuthCookies Cookies, string UserAgent);

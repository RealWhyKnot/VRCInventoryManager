namespace VRCInventoryManager.Tests;

internal sealed class FixedTimeProvider : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => DateTimeOffset.UnixEpoch;

    public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
}

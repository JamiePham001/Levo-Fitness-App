namespace backend.Tests;

internal static class TestData
{
    public static readonly Guid UserId = Guid.NewGuid();
    public static readonly Guid DeviceA = Guid.NewGuid();
    public static readonly Guid DeviceB = Guid.NewGuid();
    public static readonly DateTime T0 = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
}

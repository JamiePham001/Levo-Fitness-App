namespace backend.Entities;

// Base class for entities affected by data sync.
public abstract class SyncEntity
{
    // How far ahead of the server clock a device timestamp may be before it is clamped.
    public static readonly TimeSpan ClockSkewTolerance = TimeSpan.FromMinutes(5);

    public DateTime ClientUpdateAt { get; private set; }  // device clock, as sent by the client (clamped)
    public DateTime ServerUpdateAt { get; private set; } = DateTime.UtcNow;  // server-stamped
    public DateTime? DeletedAt { get; private set; }  // tombstone
    public Guid? UpdatedByDeviceId { get; private set; }

    // Last-write-wins: false (skip the record) if the incoming snapshot isn't newer than what we hold.
    // Stamps the sync fields and the tombstone, so entities only assign their own fields afterwards.
    protected bool BeginSync(DateTime clientUpdateAt, DateTime? deletedAt, Guid deviceId, DateTime now)
    {
        Guard.NotEmpty(deviceId, "DeviceId");
        if (clientUpdateAt == default) throw new ArgumentException("ClientUpdateAt is required.");

        var client = ClampClientTime(clientUpdateAt, now);

        if (client <= ClientUpdateAt) return false;

        ClientUpdateAt = client;
        DeletedAt = deletedAt is null ? null : ToUtc(deletedAt.Value);
        UpdatedByDeviceId = deviceId;
        ServerUpdateAt = ToUtc(now);
        return true;
    }

    // A device with its clock far in the future must not win every later conflict, so client times
    // are capped at now + tolerance. Normalised to UTC because Npgsql only writes UTC to timestamptz.
    protected static DateTime ClampClientTime(DateTime value, DateTime now)
    {
        var latest = ToUtc(now) + ClockSkewTolerance;
        var utc = ToUtc(value);
        return utc > latest ? latest : utc;
    }

    private static DateTime ToUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
    };
}

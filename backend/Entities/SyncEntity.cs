namespace backend.Entities;

public abstract class SyncEntity
{
    public DateTime ClientUpdateAt { get; set; }  // device clock, as sent by the client
    public DateTime ServerUpdateAt { get; set; } = DateTime.UtcNow;  // server-stamped
    public DateTime? DeletedAt { get; set; }
    public Guid? UpdatedByDeviceId { get; set; }

    public void UpdateServerStamp(DateTime now) => ServerUpdateAt = now;
    public void UpdateClientStamp(DateTime now) => ClientUpdateAt = now;  // device clock, as sent by the client
    public void UpdateDeviceId(Guid deviceId) => UpdatedByDeviceId = deviceId;

    // Last-write-wins: false (skip the record) if the incoming snapshot isn't newer than what we hold.
    protected bool BeginSync(DateTime clientUpdateAt, Guid deviceId, DateTime now)
    {
        if (clientUpdateAt <= ClientUpdateAt) return false;
        ClientUpdateAt = clientUpdateAt;
        UpdatedByDeviceId = deviceId;
        ServerUpdateAt = now;
        return true;
    }
}

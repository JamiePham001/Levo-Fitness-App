using System.Diagnostics.Eventing.Reader;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace backend.Entities;

// base class for entities affected by data sync
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
    protected bool BeginSync(DateTime clientUpdateAt, DateTime? deletedAt, Guid deviceId, DateTime now)
    {
        // if the incoming snapshot is older than what we hold, skip it.
        // this prevents a race condition where two clients update at the same time
        // and both send their updates to the server.  the first one will win,
        // but the second one will be ignored because its timestamp is older than ours.
        // if the incoming snapshot is newer, we'll overwrite our record with it.
        // this ensures that all clients have the latest data, even in the event of a network drop
        if (clientUpdateAt <= ClientUpdateAt) return false;
        ClientUpdateAt = clientUpdateAt;
        UpdatedByDeviceId = deviceId;
        ServerUpdateAt = now;
        DeletedAt = deletedAt;

        return true;
    }
}

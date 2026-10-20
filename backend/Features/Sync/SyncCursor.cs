using System.Globalization;

namespace backend.Features.Sync;

// The cursor is the server's bookmark for what a device has already received. Clients treat it as an opaque
// string: they store the one from each response and send it back. It is currently the UTC tick count of the
// moment the last pull started, minus a small overlap.
public static class SyncCursor
{
    // A transaction can be stamped before another sync finishes but commit after it. Starting the next pull a few
    // seconds early catches those rows; clients apply pulled rows idempotently, so seeing one twice is harmless.
    public static readonly TimeSpan Overlap = TimeSpan.FromSeconds(10);

    // Tombstones older than this are purged, so a device that has been away longer must start over.
    public static readonly TimeSpan Retention = TimeSpan.FromDays(90);

    public static string Create(DateTime pullStartedAt)
        => (pullStartedAt - Overlap).Ticks.ToString(CultureInfo.InvariantCulture);

    // No cursor means a first sync, which pulls everything. A cursor that is garbage, out of range or in the
    // future is rejected.
    public static bool TryParse(string? cursor, DateTime now, out DateTime since)
    {
        since = DateTime.MinValue;
        if (string.IsNullOrEmpty(cursor)) return true;

        if (!long.TryParse(cursor, NumberStyles.None, CultureInfo.InvariantCulture, out var ticks)) return false;
        if (ticks < 0 || ticks > DateTime.MaxValue.Ticks) return false;

        since = new DateTime(ticks, DateTimeKind.Utc);
        return since <= now;
    }

    // A first sync (no cursor) is never stale.
    public static bool IsStale(DateTime since, DateTime now) => since != DateTime.MinValue && now - since > Retention;
}

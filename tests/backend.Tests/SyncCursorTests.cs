using backend.Features.Sync;

namespace backend.Tests;

public class SyncCursorTests
{
    private static readonly DateTime Now = new(2026, 6, 1, 12, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void NoCursor_MeansAFirstSyncThatPullsEverything(string? cursor)
    {
        Assert.True(SyncCursor.TryParse(cursor, Now, out var since));
        Assert.Equal(DateTime.MinValue, since);
    }

    [Fact]
    public void Create_StartsTheNextPullSlightlyBeforeThisOne()
    {
        var cursor = SyncCursor.Create(Now);

        Assert.True(SyncCursor.TryParse(cursor, Now, out var since));
        Assert.Equal(Now - SyncCursor.Overlap, since);
        Assert.Equal(DateTimeKind.Utc, since.Kind);
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("-5")]
    [InlineData("12.5")]
    [InlineData("1e9")]
    [InlineData(" 123")]
    [InlineData("99999999999999999999")]
    [InlineData("3155378975999999999")]   // one tick past DateTime.MaxValue
    public void GarbageOrOutOfRangeCursor_IsRejected(string cursor)
        => Assert.False(SyncCursor.TryParse(cursor, Now, out _));

    [Fact]
    public void CursorInTheFuture_IsRejected()
        => Assert.False(SyncCursor.TryParse(Now.AddMinutes(1).Ticks.ToString(), Now, out _));

    [Fact]
    public void CursorExactlyNow_IsAccepted()
        => Assert.True(SyncCursor.TryParse(Now.Ticks.ToString(), Now, out _));

    [Fact]
    public void FirstSync_IsNeverStale()
        => Assert.False(SyncCursor.IsStale(DateTime.MinValue, Now));

    [Fact]
    public void Cursor_IsStaleOnlyAfterTheRetentionWindow()
    {
        Assert.False(SyncCursor.IsStale(Now - SyncCursor.Retention, Now));
        Assert.False(SyncCursor.IsStale(Now.AddDays(-30), Now));
        Assert.True(SyncCursor.IsStale(Now - SyncCursor.Retention - TimeSpan.FromTicks(1), Now));
        Assert.True(SyncCursor.IsStale(Now.AddDays(-91), Now));
    }

    [Fact]
    public void RetentionIsNinetyDays()
        => Assert.Equal(TimeSpan.FromDays(90), SyncCursor.Retention);

}

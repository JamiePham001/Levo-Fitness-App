using backend.Entities;
using backend.Entities.Goals;
using static backend.Tests.TestData;

namespace backend.Tests;

// BeginSync is protected, so the shared sync behaviour is exercised through Goal.
public class SyncEntityTests
{
    private static GoalSync Dto(Guid? id = null, DateTime? clientUpdateAt = null, DateTime? deletedAt = null)
        => new(id ?? Guid.NewGuid(), "Squat", 100, 5, Level.Beginner, clientUpdateAt ?? T0, deletedAt);

    [Fact]
    public void ClientTimestampInTheFuture_IsClampedToNowPlusTolerance()
    {
        var now = T0.AddMinutes(10);

        var goal = Goal.Create(UserId, Dto(clientUpdateAt: now.AddDays(365)), DeviceA, now);

        Assert.Equal(now + SyncEntity.ClockSkewTolerance, goal.ClientUpdateAt);
    }

    [Fact]
    public void ClientTimestampWithinTolerance_IsKept()
    {
        var now = T0;
        var slightlyAhead = now.AddMinutes(4);

        var goal = Goal.Create(UserId, Dto(clientUpdateAt: slightlyAhead), DeviceA, now);

        Assert.Equal(slightlyAhead, goal.ClientUpdateAt);
    }

    [Fact]
    public void ClampedTimestamp_LetsLaterGenuineEditsWinOnceTimePasses()
    {
        var id = Guid.NewGuid();
        var goal = Goal.Create(UserId, Dto(id, clientUpdateAt: T0.AddDays(365)), DeviceA, T0);

        var applied = goal.ApplySync(Dto(id, clientUpdateAt: T0.AddMinutes(30)), DeviceB, T0.AddMinutes(30));

        Assert.True(applied);
        Assert.Equal(DeviceB, goal.UpdatedByDeviceId);
    }

    [Fact]
    public void LocalAndUnspecifiedTimestamps_AreStoredAsUtc()
    {
        var local = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Local);
        var unspecified = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Unspecified);

        var fromLocal = Goal.Create(UserId, Dto(clientUpdateAt: local, deletedAt: local), DeviceA, T0);
        var fromUnspecified = Goal.Create(UserId, Dto(clientUpdateAt: unspecified, deletedAt: unspecified), DeviceA, T0);

        Assert.Equal(DateTimeKind.Utc, fromLocal.ClientUpdateAt.Kind);
        Assert.Equal(local.ToUniversalTime(), fromLocal.ClientUpdateAt);
        Assert.Equal(DateTimeKind.Utc, fromLocal.DeletedAt!.Value.Kind);
        Assert.Equal(DateTimeKind.Utc, fromUnspecified.ClientUpdateAt.Kind);
        Assert.Equal(unspecified.Ticks, fromUnspecified.ClientUpdateAt.Ticks);
        Assert.Equal(DateTimeKind.Utc, fromUnspecified.DeletedAt!.Value.Kind);
    }

    [Fact]
    public void MissingClientTimestamp_IsRejected()
        => Assert.Throws<ArgumentException>(() => Goal.Create(UserId, Dto() with { ClientUpdateAt = default }, DeviceA, T0));

    [Fact]
    public void EmptyDeviceId_IsRejected()
        => Assert.Throws<ArgumentException>(() => Goal.Create(UserId, Dto(), Guid.Empty, T0));

    [Fact]
    public void DeletedAtIsOverwrittenByEachAppliedSnapshot()
    {
        var id = Guid.NewGuid();
        var goal = Goal.Create(UserId, Dto(id, deletedAt: T0), DeviceA, T0);
        Assert.Equal(T0, goal.DeletedAt);

        goal.ApplySync(Dto(id, clientUpdateAt: T0.AddMinutes(1)), DeviceA, T0.AddMinutes(1));

        Assert.Null(goal.DeletedAt);
    }

    [Fact]
    public void StaleSnapshot_DoesNotChangeTombstone()
    {
        var id = Guid.NewGuid();
        var goal = Goal.Create(UserId, Dto(id, clientUpdateAt: T0.AddMinutes(1), deletedAt: T0), DeviceA, T0.AddMinutes(1));

        goal.ApplySync(Dto(id, clientUpdateAt: T0), DeviceB, T0.AddMinutes(2));

        Assert.Equal(T0, goal.DeletedAt);
    }
}

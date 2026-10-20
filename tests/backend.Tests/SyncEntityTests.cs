using backend.Entities.Goals;
using static backend.Tests.TestData;

namespace backend.Tests;

// BeginSync is protected, so the shared sync behaviour is exercised through Goal.
public class SyncEntityTests
{
    private static GoalSync Dto(Guid? id = null, DateTime? clientUpdateAt = null, DateTime? deletedAt = null)
        => new(id ?? Guid.NewGuid(), "Squat", 100, 5, Level.Beginner, clientUpdateAt ?? T0, deletedAt);

    [Fact]
    public void NewerSnapshot_StampsClientTimeServerTimeAndDevice()
    {
        var id = Guid.NewGuid();
        var goal = Goal.Create(UserId, Dto(id), DeviceA, T0);

        var applied = goal.ApplySync(Dto(id, clientUpdateAt: T0.AddMinutes(1)), DeviceB, T0.AddMinutes(2));

        Assert.True(applied);
        Assert.Equal(T0.AddMinutes(1), goal.ClientUpdateAt);
        Assert.Equal(T0.AddMinutes(2), goal.ServerUpdateAt);
        Assert.Equal(DeviceB, goal.UpdatedByDeviceId);
    }

    [Fact]
    public void ClientTimestamp_IsStoredAsSent()
    {
        // The server does not adjust device times: a time ahead of the server clock is kept as sent.
        var ahead = T0.AddDays(1);

        var goal = Goal.Create(UserId, Dto(clientUpdateAt: ahead), DeviceA, T0);

        Assert.Equal(ahead, goal.ClientUpdateAt);
    }

    [Fact]
    public void LaterSnapshotLoses_WhenAnEarlierDeviceClockWasAhead()
    {
        // Last write wins on the device clock, so a snapshot older than the stored time is rejected
        // even if it really happened later.
        var id = Guid.NewGuid();
        var goal = Goal.Create(UserId, Dto(id, clientUpdateAt: T0.AddDays(1)), DeviceA, T0);

        var applied = goal.ApplySync(Dto(id, clientUpdateAt: T0.AddMinutes(30)), DeviceB, T0.AddMinutes(30));

        Assert.False(applied);
        Assert.Equal(DeviceA, goal.UpdatedByDeviceId);
    }

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

    [Fact]
    public void CreatingWithoutAClientTimestamp_IsRejectedInsteadOfReturningABlankEntity()
        => Assert.Throws<ArgumentException>(() => Goal.Create(UserId, Dto() with { ClientUpdateAt = default }, DeviceA, T0));

    [Fact]
    public void UpdatingWithoutAClientTimestamp_IsRejectedAndChangesNothing()
    {
        var id = Guid.NewGuid();
        var goal = Goal.Create(UserId, Dto(id), DeviceA, T0);

        Assert.Throws<ArgumentException>(() => goal.ApplySync(Dto(id) with { Name = "Changed", ClientUpdateAt = default }, DeviceB, T0.AddMinutes(1)));

        Assert.Equal("Squat", goal.Name);
        Assert.Equal(T0, goal.ClientUpdateAt);
        Assert.Equal(DeviceA, goal.UpdatedByDeviceId);
    }
}

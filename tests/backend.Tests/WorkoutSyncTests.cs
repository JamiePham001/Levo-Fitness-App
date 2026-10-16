using backend.Entities.Workouts;
using static backend.Tests.TestData;

namespace backend.Tests;

public class WorkoutSyncTests
{
    private static WorkoutSync Dto(Guid? id = null, int order = 0, Guid? blockId = null,
        DateTime? clientUpdateAt = null, DateTime? deletedAt = null)
        => new(id ?? Guid.NewGuid(), order, blockId, deletedAt, clientUpdateAt ?? T0);

    [Fact]
    public void Create_SetsBlockOwnerAndOrder()
    {
        var blockId = Guid.NewGuid();
        var dto = Dto(order: 2, blockId: blockId);

        var workout = Workout.Create(UserId, dto, DeviceA, T0);

        Assert.Equal(dto.Id, workout.Id);
        Assert.Equal(UserId, workout.UserId);
        Assert.Equal(blockId, workout.BlockId);
        Assert.Equal(2, workout.Order);
    }

    [Fact]
    public void Create_AllowsAWorkoutOutsideAnyProgram()
    {
        var workout = Workout.Create(UserId, Dto(blockId: null), DeviceA, T0);

        Assert.Null(workout.BlockId);
    }

    [Fact]
    public void Create_RejectsEmptyIds()
    {
        Assert.Throws<ArgumentException>(() => Workout.Create(UserId, Dto(id: Guid.Empty), DeviceA, T0));
        Assert.Throws<ArgumentException>(() => Workout.Create(Guid.Empty, Dto(), DeviceA, T0));
        Assert.Throws<ArgumentException>(() => Workout.Create(UserId, Dto(blockId: Guid.Empty), DeviceA, T0));
    }

    [Fact]
    public void NegativeOrder_IsRejected()
        => Assert.Throws<ArgumentException>(() => Workout.Create(UserId, Dto(order: -1), DeviceA, T0));

    [Fact]
    public void ApplySync_CannotMoveWorkoutToAnotherBlock()
    {
        var id = Guid.NewGuid();
        var workout = Workout.Create(UserId, Dto(id, blockId: Guid.NewGuid()), DeviceA, T0);

        Assert.Throws<ArgumentException>(() =>
            workout.ApplySync(Dto(id, blockId: Guid.NewGuid(), clientUpdateAt: T0.AddMinutes(1)), DeviceA, T0.AddMinutes(1)));
        Assert.Throws<ArgumentException>(() =>
            workout.ApplySync(Dto(id, blockId: null, clientUpdateAt: T0.AddMinutes(1)), DeviceA, T0.AddMinutes(1)));
    }

    [Fact]
    public void ApplySync_NewerSnapshot_UpdatesOrder()
    {
        var id = Guid.NewGuid();
        var blockId = Guid.NewGuid();
        var workout = Workout.Create(UserId, Dto(id, order: 0, blockId: blockId), DeviceA, T0);

        var applied = workout.ApplySync(Dto(id, order: 4, blockId: blockId, clientUpdateAt: T0.AddMinutes(1)), DeviceB, T0.AddMinutes(1));

        Assert.True(applied);
        Assert.Equal(4, workout.Order);
        Assert.Equal(DeviceB, workout.UpdatedByDeviceId);
    }

    [Fact]
    public void ApplySync_StaleSnapshot_ChangesNothing()
    {
        var id = Guid.NewGuid();
        var workout = Workout.Create(UserId, Dto(id, order: 1), DeviceA, T0);

        var applied = workout.ApplySync(Dto(id, order: 9, clientUpdateAt: T0), DeviceB, T0.AddMinutes(1));

        Assert.False(applied);
        Assert.Equal(1, workout.Order);
    }

    [Fact]
    public void ApplySync_DeleteSnapshot_SetsTombstone()
    {
        var id = Guid.NewGuid();
        var workout = Workout.Create(UserId, Dto(id), DeviceA, T0);

        workout.ApplySync(Dto(id, clientUpdateAt: T0.AddMinutes(1), deletedAt: T0.AddMinutes(1)), DeviceA, T0.AddMinutes(1));

        Assert.Equal(T0.AddMinutes(1), workout.DeletedAt);
    }
}

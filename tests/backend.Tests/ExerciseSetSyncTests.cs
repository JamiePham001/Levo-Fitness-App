using backend.Entities.ExerciseSets;
using static backend.Tests.TestData;

namespace backend.Tests;

public class ExerciseSetSyncTests
{
    private static readonly Guid ExerciseId = Guid.NewGuid();

    private static SetSync Dto(Guid? id = null, Guid? exerciseId = null, int order = 0, double? weight = 100, int? reps = 5,
        double? rir = 2, double restTime = 120, bool completed = false, DateTime? clientUpdateAt = null, DateTime? deletedAt = null)
        => new(id ?? Guid.NewGuid(), exerciseId ?? ExerciseId, order, weight, reps, rir, restTime, completed, deletedAt,
            clientUpdateAt ?? T0);

    [Fact]
    public void Create_CopiesSnapshotParentAndOwner()
    {
        var dto = Dto(order: 1, weight: 82.5, reps: 8, rir: 1.5, restTime: 90, completed: true);

        var set = ExerciseSet.Create(UserId, dto, DeviceA, T0);

        Assert.Equal(dto.Id, set.Id);
        Assert.Equal(UserId, set.UserId);
        Assert.Equal(ExerciseId, set.ExerciseId);
        Assert.Equal(1, set.Order);
        Assert.Equal(82.5, set.Weight);
        Assert.Equal(8, set.Reps);
        Assert.Equal(1.5, set.RIR);
        Assert.Equal(90, set.RestTime);
        Assert.True(set.Completed);
    }

    [Fact]
    public void PlannedSet_MayHaveNoWeightRepsOrRir()
    {
        var set = ExerciseSet.Create(UserId, Dto(weight: null, reps: null, rir: null), DeviceA, T0);

        Assert.Null(set.Weight);
        Assert.Null(set.Reps);
        Assert.Null(set.RIR);
    }

    [Fact]
    public void Create_RejectsEmptyIds()
    {
        Assert.Throws<ArgumentException>(() => ExerciseSet.Create(UserId, Dto(id: Guid.Empty), DeviceA, T0));
        Assert.Throws<ArgumentException>(() => ExerciseSet.Create(Guid.Empty, Dto(), DeviceA, T0));
        Assert.Throws<ArgumentException>(() => ExerciseSet.Create(UserId, Dto(exerciseId: Guid.Empty), DeviceA, T0));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void InvalidWeight_IsRejected(double weight)
        => Assert.Throws<ArgumentException>(() => ExerciseSet.Create(UserId, Dto(weight: weight), DeviceA, T0));

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public void InvalidReps_AreRejected(int reps)
        => Assert.Throws<ArgumentException>(() => ExerciseSet.Create(UserId, Dto(reps: reps), DeviceA, T0));

    [Theory]
    [InlineData(-0.5)]
    [InlineData(10.5)]
    [InlineData(double.NaN)]
    public void RirOutsideZeroToTen_IsRejected(double rir)
        => Assert.Throws<ArgumentException>(() => ExerciseSet.Create(UserId, Dto(rir: rir), DeviceA, T0));

    [Theory]
    [InlineData(0)]
    [InlineData(10)]
    public void RirAtTheBounds_IsAccepted(double rir)
        => Assert.Equal(rir, ExerciseSet.Create(UserId, Dto(rir: rir), DeviceA, T0).RIR);

    [Theory]
    [InlineData(-1)]
    [InlineData(3601)]
    [InlineData(double.NaN)]
    public void InvalidRestTime_IsRejected(double restTime)
        => Assert.Throws<ArgumentException>(() => ExerciseSet.Create(UserId, Dto(restTime: restTime), DeviceA, T0));

    [Fact]
    public void NegativeOrder_IsRejected()
        => Assert.Throws<ArgumentException>(() => ExerciseSet.Create(UserId, Dto(order: -1), DeviceA, T0));

    [Fact]
    public void ApplySync_CannotMoveSetToAnotherExercise()
    {
        var id = Guid.NewGuid();
        var set = ExerciseSet.Create(UserId, Dto(id), DeviceA, T0);

        Assert.Throws<ArgumentException>(() =>
            set.ApplySync(Dto(id, exerciseId: Guid.NewGuid(), clientUpdateAt: T0.AddMinutes(1)), DeviceA, T0.AddMinutes(1)));
    }

    [Fact]
    public void ApplySync_NewerSnapshot_OverwritesAndCanClearValues()
    {
        var id = Guid.NewGuid();
        var set = ExerciseSet.Create(UserId, Dto(id), DeviceA, T0);

        var applied = set.ApplySync(
            Dto(id, order: 2, weight: null, reps: null, rir: null, restTime: 60, completed: true, clientUpdateAt: T0.AddMinutes(1)),
            DeviceB, T0.AddMinutes(1));

        Assert.True(applied);
        Assert.Equal(2, set.Order);
        Assert.Null(set.Weight);
        Assert.Null(set.Reps);
        Assert.Null(set.RIR);
        Assert.Equal(60, set.RestTime);
        Assert.True(set.Completed);
    }

    [Fact]
    public void ApplySync_StaleSnapshot_ChangesNothing()
    {
        var id = Guid.NewGuid();
        var set = ExerciseSet.Create(UserId, Dto(id, weight: 100), DeviceA, T0);

        var applied = set.ApplySync(Dto(id, weight: 999, clientUpdateAt: T0), DeviceB, T0.AddMinutes(1));

        Assert.False(applied);
        Assert.Equal(100, set.Weight);
    }

    [Fact]
    public void ApplySync_DeleteSnapshot_SetsTombstone()
    {
        var id = Guid.NewGuid();
        var set = ExerciseSet.Create(UserId, Dto(id), DeviceA, T0);

        set.ApplySync(Dto(id, clientUpdateAt: T0.AddMinutes(1), deletedAt: T0.AddMinutes(1)), DeviceA, T0.AddMinutes(1));

        Assert.Equal(T0.AddMinutes(1), set.DeletedAt);
    }
}

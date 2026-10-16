using backend.Entities.Goals;
using GoalEntity = backend.Entities.Goals.Goal;

namespace backend.Tests;

public class GoalSyncTests
{
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid DeviceA = Guid.NewGuid();
    private static readonly Guid DeviceB = Guid.NewGuid();
    private static readonly DateTime T0 = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    private static GoalSync Dto(Guid? id = null, string name = "Squat", double? weight = 100, int? reps = 5,
        Level level = Level.Beginner, DateTime? clientUpdateAt = null, DateTime? deletedAt = null)
        => new(id ?? Guid.NewGuid(), name, weight, reps, level, clientUpdateAt ?? T0, deletedAt);

    [Fact]
    public void Create_CopiesSnapshotAndStampsSync()
    {
        var dto = Dto(name: "  Bench  ", weight: 80, reps: 8, level: Level.Pro);

        var goal = GoalEntity.Create(UserId, dto, DeviceA, T0.AddSeconds(1));

        Assert.Equal(dto.Id, goal.Id);
        Assert.Equal(UserId, goal.UserId);
        Assert.Equal("Bench", goal.Name);
        Assert.Equal(80, goal.Weight);
        Assert.Equal(8, goal.Reps);
        Assert.Equal(Level.Pro, goal.Level);
        Assert.Equal(T0, goal.ClientUpdateAt);
        Assert.Equal(T0.AddSeconds(1), goal.ServerUpdateAt);
        Assert.Equal(DeviceA, goal.UpdatedByDeviceId);
        Assert.Null(goal.DeletedAt);
    }

    [Fact]
    public void Create_AllowsNullWeightAndReps()
    {
        var goal = GoalEntity.Create(UserId, Dto(weight: null, reps: null), DeviceA, T0);

        Assert.Null(goal.Weight);
        Assert.Null(goal.Reps);
    }

    [Fact]
    public void Create_RejectsEmptyIds()
    {
        Assert.Throws<ArgumentException>(() => GoalEntity.Create(UserId, Dto(id: Guid.Empty), DeviceA, T0));
        Assert.Throws<ArgumentException>(() => GoalEntity.Create(Guid.Empty, Dto(), DeviceA, T0));
    }

    [Fact]
    public void ApplySync_NewerSnapshot_OverwritesEverythingClientOwned()
    {
        var id = Guid.NewGuid();
        var goal = GoalEntity.Create(UserId, Dto(id: id), DeviceA, T0);
        var newer = Dto(id: id, name: "Deadlift", weight: 150, reps: 3, level: Level.Mutant, clientUpdateAt: T0.AddMinutes(1));

        var applied = goal.ApplySync(newer, DeviceB, T0.AddMinutes(2));

        Assert.True(applied);
        Assert.Equal("Deadlift", goal.Name);
        Assert.Equal(150, goal.Weight);
        Assert.Equal(3, goal.Reps);
        Assert.Equal(Level.Mutant, goal.Level);
        Assert.Equal(T0.AddMinutes(1), goal.ClientUpdateAt);
        Assert.Equal(T0.AddMinutes(2), goal.ServerUpdateAt);
        Assert.Equal(DeviceB, goal.UpdatedByDeviceId);
        Assert.Equal(UserId, goal.UserId);
        Assert.Equal(id, goal.Id);
    }

    [Theory]
    [InlineData(0)]   // same timestamp (retry of an applied record)
    [InlineData(-1)]  // older timestamp
    public void ApplySync_StaleSnapshot_ChangesNothing(int minutesOffset)
    {
        var id = Guid.NewGuid();
        var goal = GoalEntity.Create(UserId, Dto(id: id, name: "Squat"), DeviceA, T0);
        var stale = Dto(id: id, name: "Stale", clientUpdateAt: T0.AddMinutes(minutesOffset));

        var applied = goal.ApplySync(stale, DeviceB, T0.AddMinutes(5));

        Assert.False(applied);
        Assert.Equal("Squat", goal.Name);
        Assert.Equal(T0, goal.ServerUpdateAt);
        Assert.Equal(DeviceA, goal.UpdatedByDeviceId);
    }

    [Fact]
    public void ApplySync_DeleteSnapshot_SetsTombstone()
    {
        var id = Guid.NewGuid();
        var goal = GoalEntity.Create(UserId, Dto(id: id), DeviceA, T0);
        var deletedAt = T0.AddMinutes(1);

        goal.ApplySync(Dto(id: id, clientUpdateAt: deletedAt, deletedAt: deletedAt), DeviceA, T0.AddMinutes(2));

        Assert.Equal(deletedAt, goal.DeletedAt);
    }

    [Fact]
    public void ApplySync_NewerSnapshotWithoutDeletedAt_ClearsTombstone()
    {
        var id = Guid.NewGuid();
        var goal = GoalEntity.Create(UserId, Dto(id: id, deletedAt: T0), DeviceA, T0);

        goal.ApplySync(Dto(id: id, clientUpdateAt: T0.AddMinutes(1)), DeviceA, T0.AddMinutes(2));

        Assert.Null(goal.DeletedAt);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void ApplySync_RejectsBlankName(string name)
        => Assert.Throws<ArgumentException>(() => GoalEntity.Create(UserId, Dto(name: name), DeviceA, T0));

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void ApplySync_RejectsNonPositiveWeight(double weight)
        => Assert.Throws<ArgumentException>(() => GoalEntity.Create(UserId, Dto(weight: weight), DeviceA, T0));

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void ApplySync_RejectsNonPositiveReps(int reps)
        => Assert.Throws<ArgumentException>(() => GoalEntity.Create(UserId, Dto(reps: reps), DeviceA, T0));

    [Fact]
    public void ApplySync_RejectsUndefinedLevel()
        => Assert.Throws<ArgumentException>(() => GoalEntity.Create(UserId, Dto(level: (Level)99), DeviceA, T0));

    [Fact]
    public void ApplySync_InvalidSnapshot_DoesNotMutateExistingGoal()
    {
        var id = Guid.NewGuid();
        var goal = GoalEntity.Create(UserId, Dto(id: id, name: "Squat"), DeviceA, T0);

        Assert.Throws<ArgumentException>(() =>
            goal.ApplySync(Dto(id: id, name: "", clientUpdateAt: T0.AddMinutes(1)), DeviceB, T0.AddMinutes(2)));

        Assert.Equal("Squat", goal.Name);
        Assert.Equal(T0, goal.ClientUpdateAt);
        Assert.Equal(DeviceA, goal.UpdatedByDeviceId);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void ApplySync_RejectsNonFiniteWeight(double weight)
        => Assert.Throws<ArgumentException>(() => GoalEntity.Create(UserId, Dto(weight: weight), DeviceA, T0));

    [Fact]
    public void ApplySync_RejectsNameLongerThanColumn()
        => Assert.Throws<ArgumentException>(() => GoalEntity.Create(UserId, Dto(name: new string('a', 101)), DeviceA, T0));

    [Fact]
    public void Create_AcceptsLevelNone()
        => Assert.Equal(Level.None, GoalEntity.Create(UserId, Dto(level: Level.None), DeviceA, T0).Level);
}

using backend.Entities.Exercises;
using static backend.Tests.TestData;

namespace backend.Tests;

public class ExerciseSyncTests
{
    private static readonly Guid WorkoutId = Guid.NewGuid();
    private static readonly Guid MovementId = Guid.NewGuid();

    private static ExerciseSync Dto(Guid? id = null, Guid? workoutId = null, Guid? movementId = null, string name = "Squat",
        string? notes = null, int order = 0, bool completed = false, DateTime? clientUpdateAt = null, DateTime? deletedAt = null)
        => new(id ?? Guid.NewGuid(), workoutId ?? WorkoutId, movementId ?? MovementId, name, notes, order, completed,
            clientUpdateAt ?? T0, deletedAt);

    [Fact]
    public void Create_CopiesSnapshotParentAndOwner()
    {
        var dto = Dto(name: "  Squat ", notes: " felt heavy ", order: 3, completed: true);

        var exercise = Exercise.Create(UserId, dto, DeviceA, T0);

        Assert.Equal(dto.Id, exercise.Id);
        Assert.Equal(UserId, exercise.UserId);
        Assert.Equal(WorkoutId, exercise.WorkoutId);
        Assert.Equal(MovementId, exercise.MovementId);
        Assert.Equal("Squat", exercise.Name);
        Assert.Equal("felt heavy", exercise.Notes);
        Assert.Equal(3, exercise.Order);
        Assert.True(exercise.Completed);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("  ")]
    public void Notes_AreOptional(string? notes)
    {
        var exercise = Exercise.Create(UserId, Dto(notes: notes), DeviceA, T0);

        Assert.Null(exercise.Notes);
    }

    [Fact]
    public void Create_RejectsEmptyIds()
    {
        Assert.Throws<ArgumentException>(() => Exercise.Create(UserId, Dto(id: Guid.Empty), DeviceA, T0));
        Assert.Throws<ArgumentException>(() => Exercise.Create(Guid.Empty, Dto(), DeviceA, T0));
        Assert.Throws<ArgumentException>(() => Exercise.Create(UserId, Dto(workoutId: Guid.Empty), DeviceA, T0));
        Assert.Throws<ArgumentException>(() => Exercise.Create(UserId, Dto(movementId: Guid.Empty), DeviceA, T0));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void BlankName_IsRejected(string name)
        => Assert.Throws<ArgumentException>(() => Exercise.Create(UserId, Dto(name: name), DeviceA, T0));

    [Fact]
    public void NegativeOrder_IsRejected()
        => Assert.Throws<ArgumentException>(() => Exercise.Create(UserId, Dto(order: -1), DeviceA, T0));

    [Fact]
    public void NotesLongerThanColumn_AreRejected()
        => Assert.Throws<ArgumentException>(() => Exercise.Create(UserId, Dto(notes: new string('a', 2001)), DeviceA, T0));

    [Fact]
    public void ApplySync_CannotMoveExerciseToAnotherWorkout()
    {
        var id = Guid.NewGuid();
        var exercise = Exercise.Create(UserId, Dto(id), DeviceA, T0);

        Assert.Throws<ArgumentException>(() =>
            exercise.ApplySync(Dto(id, workoutId: Guid.NewGuid(), clientUpdateAt: T0.AddMinutes(1)), DeviceA, T0.AddMinutes(1)));
        Assert.Equal(WorkoutId, exercise.WorkoutId);
    }

    [Fact]
    public void ApplySync_NewerSnapshot_CanSwapTheMovement()
    {
        var id = Guid.NewGuid();
        var newMovement = Guid.NewGuid();
        var exercise = Exercise.Create(UserId, Dto(id), DeviceA, T0);

        var applied = exercise.ApplySync(
            Dto(id, movementId: newMovement, name: "Front Squat", completed: true, clientUpdateAt: T0.AddMinutes(1)), DeviceB, T0.AddMinutes(1));

        Assert.True(applied);
        Assert.Equal(newMovement, exercise.MovementId);
        Assert.Equal("Front Squat", exercise.Name);
        Assert.True(exercise.Completed);
        Assert.Equal(DeviceB, exercise.UpdatedByDeviceId);
    }

    [Fact]
    public void ApplySync_ClientTimeSlightlyAheadOfServer_IsStillAccepted()
    {
        var id = Guid.NewGuid();
        var exercise = Exercise.Create(UserId, Dto(id, clientUpdateAt: T0.AddSeconds(30)), DeviceA, T0);

        Assert.Equal(T0.AddSeconds(30), exercise.ClientUpdateAt);
    }

    [Fact]
    public void ApplySync_StaleSnapshot_ChangesNothing()
    {
        var id = Guid.NewGuid();
        var exercise = Exercise.Create(UserId, Dto(id, name: "Squat"), DeviceA, T0);

        var applied = exercise.ApplySync(Dto(id, name: "Stale", clientUpdateAt: T0), DeviceB, T0.AddMinutes(1));

        Assert.False(applied);
        Assert.Equal("Squat", exercise.Name);
    }

    [Fact]
    public void ApplySync_DeleteSnapshot_SetsTombstone()
    {
        var id = Guid.NewGuid();
        var exercise = Exercise.Create(UserId, Dto(id), DeviceA, T0);

        exercise.ApplySync(Dto(id, clientUpdateAt: T0.AddMinutes(1), deletedAt: T0.AddMinutes(1)), DeviceA, T0.AddMinutes(1));

        Assert.Equal(T0.AddMinutes(1), exercise.DeletedAt);
    }
}

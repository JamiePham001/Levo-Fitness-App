using backend.Entities.ExerciseSets;
using backend.Entities.Exercises;
using backend.Entities.Goals;
using backend.Entities.Movements;
using backend.Entities.ProgramBlocks;
using backend.Entities.Programs;
using backend.Entities.Users;
using backend.Entities.Workouts;
using static backend.Tests.TestData;

namespace backend.Tests;

// ToSync is the inverse of ApplySync: creating from a snapshot and reading it back must give the same snapshot.
// Every field below holds a distinct value (and the timestamps differ from each other) so a swapped or
// forgotten field in a ToSync mapping shows up as a failure.
public class ToSyncTests
{
    private static readonly DateTime Updated = T0.AddMinutes(1);
    private static readonly DateTime Deleted = T0.AddMinutes(2);
    private static readonly DateTime Created = T0.AddHours(-3);

    [Fact]
    public void Goal_RoundTrips()
    {
        var dto = new GoalSync(Guid.NewGuid(), "Squat 140", 140, 5, Level.Advanced, Updated, Deleted);

        Assert.Equal(dto, Goal.Create(UserId, dto, DeviceA, T0).ToSync());
    }

    [Fact]
    public void Goal_WithoutOptionalValuesOrTombstone()
    {
        var dto = new GoalSync(Guid.NewGuid(), "Get strong", null, null, Level.None, Updated, null);

        Assert.Equal(dto, Goal.Create(UserId, dto, DeviceA, T0).ToSync());
    }

    [Fact]
    public void Movement_RoundTrips()
    {
        var dto = new MovementSync(Guid.NewGuid(), "Back Squat", ["legs", "quads"], Updated, Deleted);

        var sync = Movement.Create(UserId, dto, DeviceA, T0).ToSync();

        // Arrays compare by reference inside a record, so compare the keywords separately.
        Assert.Equal(dto.Keywords, sync.Keywords);
        Assert.Equal(dto with { Keywords = null }, sync with { Keywords = null });
    }

    [Fact]
    public void Movement_KeywordsAreReturnedAsACopy()
    {
        var movement = Movement.Create(UserId, new MovementSync(Guid.NewGuid(), "Row", ["back"], Updated, null), DeviceA, T0);

        var sync = movement.ToSync();
        sync.Keywords![0] = "changed";

        Assert.Equal(["back"], movement.Keywords);
    }

    [Fact]
    public void Program_RoundTrips()
    {
        var dto = new ProgramSync(Guid.NewGuid(), "PPL", ProgramType.Program, Updated, Deleted);

        Assert.Equal(dto, TrainingProgram.Create(UserId, dto, DeviceA, T0).ToSync());
    }

    [Fact]
    public void ProgramBlock_RoundTrips()
    {
        var dto = new BlockSync(Guid.NewGuid(), Guid.NewGuid(), "Week 1", 3, Deleted, Updated);

        Assert.Equal(dto, ProgramBlock.Create(UserId, dto, DeviceA, T0).ToSync());
    }

    [Fact]
    public void ProgramBlock_WithoutName()
    {
        var dto = new BlockSync(Guid.NewGuid(), Guid.NewGuid(), null, 0, null, Updated);

        Assert.Equal(dto, ProgramBlock.Create(UserId, dto, DeviceA, T0).ToSync());
    }

    [Fact]
    public void Workout_RoundTrips()
    {
        var dto = new WorkoutSync(Guid.NewGuid(), "Push Day", 2, Guid.NewGuid(), Created, Deleted, Updated);

        Assert.Equal(dto, Workout.Create(UserId, dto, DeviceA, T0).ToSync());
    }

    [Fact]
    public void Workout_WithoutBlockOrName()
    {
        var dto = new WorkoutSync(Guid.NewGuid(), null, 0, null, Created, null, Updated);

        Assert.Equal(dto, Workout.Create(UserId, dto, DeviceA, T0).ToSync());
    }

    [Fact]
    public void Exercise_RoundTrips()
    {
        var dto = new ExerciseSync(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Squat", "felt heavy", 4, true, Updated, Deleted);

        Assert.Equal(dto, Exercise.Create(UserId, dto, DeviceA, T0).ToSync());
    }

    [Fact]
    public void Exercise_WithoutNotes()
    {
        var dto = new ExerciseSync(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Squat", null, 0, false, Updated, null);

        Assert.Equal(dto, Exercise.Create(UserId, dto, DeviceA, T0).ToSync());
    }

    [Fact]
    public void ExerciseSet_RoundTrips()
    {
        var dto = new SetSync(Guid.NewGuid(), Guid.NewGuid(), 3, 82.5, 8, 1.5, 90, true, Deleted, Updated);

        Assert.Equal(dto, ExerciseSet.Create(UserId, dto, DeviceA, T0).ToSync());
    }

    [Fact]
    public void ExerciseSet_PlannedWithoutValues()
    {
        var dto = new SetSync(Guid.NewGuid(), Guid.NewGuid(), 0, null, null, null, 120, false, null, Updated);

        Assert.Equal(dto, ExerciseSet.Create(UserId, dto, DeviceA, T0).ToSync());
    }

    [Fact]
    public void User_RoundTrips()
    {
        var dto = new UserSync(UserId, Gender.Female, 62.5, Measurement.Imperial, Updated);

        var user = User.CreateOAuth(dto, "sam", "sam@example.com", null, "google", DeviceA, T0);

        Assert.Equal(dto, user.ToSync());
    }

    [Fact]
    public void ToSync_ReflectsLaterUpdates()
    {
        var id = Guid.NewGuid();
        var goal = Goal.Create(UserId, new GoalSync(id, "Old", 100, 5, Level.Beginner, Updated, null), DeviceA, T0);
        var newer = new GoalSync(id, "New", 120, 3, Level.Pro, Deleted, Deleted);

        goal.ApplySync(newer, DeviceB, T0.AddMinutes(5));

        Assert.Equal(newer, goal.ToSync());
    }
}

using backend.Database;
using backend.Entities.ExerciseSets;
using backend.Entities.Exercises;
using backend.Entities.Goals;
using backend.Entities.Movements;
using backend.Entities.ProgramBlocks;
using backend.Entities.Programs;
using backend.Entities.Users;
using backend.Entities.Workouts;
using backend.Features.Sync;
using Microsoft.EntityFrameworkCore;
using static backend.Tests.TestData;

namespace backend.Tests;

// The pull query against the EF in-memory provider: which rows come back and in what shape.
public class SyncPullTests
{
    private static readonly Guid Me = Guid.NewGuid();
    private static readonly Guid Other = Guid.NewGuid();
    private static readonly DateTime Since = T0;
    private static readonly DateTime Until = T0.AddHours(1);

    private static AppDbContext NewDb()
        => new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static GoalSync GoalDto(Guid? id = null, DateTime? deletedAt = null)
        => new(id ?? Guid.NewGuid(), "Squat", 100, 5, Level.Pro, T0.AddMinutes(-30), deletedAt);

    private static async Task<AppDbContext> DbWithGoal(Guid owner, Guid device, DateTime serverTime, Guid? id = null, DateTime? deletedAt = null)
    {
        var db = NewDb();
        db.Goals.Add(Goal.Create(owner, GoalDto(id, deletedAt), device, serverTime));
        await db.SaveChangesAsync();
        return db;
    }

    [Fact]
    public async Task EmptyDatabase_ReturnsEmptyListsAndNoProfile()
    {
        using var db = NewDb();

        var changes = await SyncPull.BuildAsync(db, Me, DeviceA, Since, Until);

        Assert.Null(changes.user);
        Assert.Empty(changes.goals!);
        Assert.Empty(changes.movements!);
        Assert.Empty(changes.programs!);
        Assert.Empty(changes.blocks!);
        Assert.Empty(changes.workouts!);
        Assert.Empty(changes.exercises!);
        Assert.Empty(changes.sets!);
    }

    [Fact]
    public async Task ReturnsRowsChangedByAnotherDeviceAsSyncRecords()
    {
        var id = Guid.NewGuid();
        using var db = await DbWithGoal(Me, DeviceB, T0.AddMinutes(10), id);

        var changes = await SyncPull.BuildAsync(db, Me, DeviceA, Since, Until);

        var goal = Assert.Single(changes.goals!);
        Assert.Equal(GoalDto(id), goal);
    }

    [Fact]
    public async Task LeavesOutRowsWrittenByTheCallingDevice()
    {
        using var db = await DbWithGoal(Me, DeviceA, T0.AddMinutes(10));

        var changes = await SyncPull.BuildAsync(db, Me, DeviceA, Since, Until);

        Assert.Empty(changes.goals!);
    }

    [Fact]
    public async Task LeavesOutOtherUsersRows()
    {
        using var db = await DbWithGoal(Other, DeviceB, T0.AddMinutes(10));

        var changes = await SyncPull.BuildAsync(db, Me, DeviceA, Since, Until);

        Assert.Empty(changes.goals!);
    }

    [Fact]
    public async Task SinceIsExclusiveAndUntilIsInclusive()
    {
        var db = NewDb();
        var atSince = Guid.NewGuid();
        var justAfterSince = Guid.NewGuid();
        var atUntil = Guid.NewGuid();
        var afterUntil = Guid.NewGuid();
        db.Goals.Add(Goal.Create(Me, GoalDto(atSince), DeviceB, Since));
        db.Goals.Add(Goal.Create(Me, GoalDto(justAfterSince), DeviceB, Since.AddTicks(1)));
        db.Goals.Add(Goal.Create(Me, GoalDto(atUntil), DeviceB, Until));
        db.Goals.Add(Goal.Create(Me, GoalDto(afterUntil), DeviceB, Until.AddTicks(1)));
        await db.SaveChangesAsync();

        var changes = await SyncPull.BuildAsync(db, Me, DeviceA, Since, Until);

        Assert.Equal([justAfterSince, atUntil], changes.goals!.Select(g => g.Id));
    }

    [Fact]
    public async Task FirstSync_ReturnsEverythingFromTheBeginning()
    {
        using var db = await DbWithGoal(Me, DeviceB, new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        var changes = await SyncPull.BuildAsync(db, Me, DeviceA, DateTime.MinValue, Until);

        Assert.Single(changes.goals!);
    }

    [Fact]
    public async Task IncludesTombstonesSoClientsCanDeleteTheirCopy()
    {
        var deletedAt = T0.AddMinutes(5);
        using var db = await DbWithGoal(Me, DeviceB, T0.AddMinutes(10), deletedAt: deletedAt);

        var changes = await SyncPull.BuildAsync(db, Me, DeviceA, Since, Until);

        Assert.Equal(deletedAt, Assert.Single(changes.goals!).DeletedAt);
    }

    [Fact]
    public async Task ReturnsRowsOldestFirst()
    {
        var db = NewDb();
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var third = Guid.NewGuid();
        db.Goals.Add(Goal.Create(Me, GoalDto(third), DeviceB, T0.AddMinutes(30)));
        db.Goals.Add(Goal.Create(Me, GoalDto(first), DeviceB, T0.AddMinutes(10)));
        db.Goals.Add(Goal.Create(Me, GoalDto(second), DeviceB, T0.AddMinutes(20)));
        await db.SaveChangesAsync();

        var changes = await SyncPull.BuildAsync(db, Me, DeviceA, Since, Until);

        Assert.Equal([first, second, third], changes.goals!.Select(g => g.Id));
    }

    [Fact]
    public async Task ReturnsEveryTable()
    {
        var db = NewDb();
        var at = T0.AddMinutes(10);
        var updated = T0.AddMinutes(-30);
        var programId = Guid.NewGuid();
        var blockId = Guid.NewGuid();
        var workoutId = Guid.NewGuid();
        var exerciseId = Guid.NewGuid();

        db.Users.Add(User.CreateOAuth(new UserSync(Me, Gender.Female, 60, Measurement.Metric, updated), "sam", "sam@example.com", null, "google", DeviceB, at));
        db.Goals.Add(Goal.Create(Me, GoalDto(), DeviceB, at));
        db.Movements.Add(Movement.Create(Me, new MovementSync(Guid.NewGuid(), "Squat", ["legs"], updated, null), DeviceB, at));
        db.Programs.Add(TrainingProgram.Create(Me, new ProgramSync(programId, "PPL", ProgramType.Split, updated, null), DeviceB, at));
        db.ProgramBlocks.Add(ProgramBlock.Create(Me, new BlockSync(blockId, programId, "Week 1", 0, null, updated), DeviceB, at));
        db.Workouts.Add(Workout.Create(Me, new WorkoutSync(workoutId, "Push", 0, blockId, updated, null, updated), DeviceB, at));
        db.Exercises.Add(Exercise.Create(Me, new ExerciseSync(exerciseId, workoutId, Guid.NewGuid(), "Bench", null, 0, false, updated, null), DeviceB, at));
        db.ExerciseSets.Add(ExerciseSet.Create(Me, new SetSync(Guid.NewGuid(), exerciseId, 0, 80, 8, 2, 120, false, null, updated), DeviceB, at));
        await db.SaveChangesAsync();

        var changes = await SyncPull.BuildAsync(db, Me, DeviceA, Since, Until);

        Assert.Equal(Me, changes.user!.Id);
        Assert.Single(changes.goals!);
        Assert.Single(changes.movements!);
        Assert.Single(changes.programs!);
        Assert.Single(changes.blocks!);
        Assert.Single(changes.workouts!);
        Assert.Single(changes.exercises!);
        Assert.Single(changes.sets!);
    }

    [Fact]
    public async Task ProfileChangedByTheCallingDevice_IsNotReturned()
    {
        var db = NewDb();
        db.Users.Add(User.CreateOAuth(new UserSync(Me, Gender.Male, 80, Measurement.Metric, T0.AddMinutes(-30)), "sam", "sam@example.com", null, "apple", DeviceA, T0.AddMinutes(10)));
        await db.SaveChangesAsync();

        var changes = await SyncPull.BuildAsync(db, Me, DeviceA, Since, Until);

        Assert.Null(changes.user);
    }

    [Fact]
    public async Task PulledRowsAreNotTracked()
    {
        using var db = await DbWithGoal(Me, DeviceB, T0.AddMinutes(10));
        db.ChangeTracker.Clear();

        await SyncPull.BuildAsync(db, Me, DeviceA, Since, Until);

        Assert.Empty(db.ChangeTracker.Entries());
    }
}

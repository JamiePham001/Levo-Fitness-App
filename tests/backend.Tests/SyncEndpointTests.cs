using System.Net;
using System.Net.Http.Json;
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

namespace backend.Tests;

// Real requests through the whole slice: authentication, apply, pull, cursor.
public sealed class SyncEndpointTests(SyncApiFactory factory) : IClassFixture<SyncApiFactory>
{
    private static readonly Guid DeviceA = Guid.NewGuid();
    private static readonly Guid DeviceB = Guid.NewGuid();

    private static DateTime Now => DateTime.UtcNow;

    private static GoalSync GoalDto(Guid? id = null, string name = "Squat", DateTime? updatedAt = null, DateTime? deletedAt = null)
        => new(id ?? Guid.NewGuid(), name, 100, 5, Level.Beginner, updatedAt ?? Now.AddMinutes(-1), deletedAt);

    private static ProgramSync ProgramDto(Guid? id = null) => new(id ?? Guid.NewGuid(), "PPL", ProgramType.Split, Now.AddMinutes(-1), null);

    private static BlockSync BlockDto(Guid programId, Guid? id = null) => new(id ?? Guid.NewGuid(), programId, "Week 1", 0, null, Now.AddMinutes(-1));

    private static WorkoutSync WorkoutDto(Guid? blockId, Guid? id = null) => new(id ?? Guid.NewGuid(), "Push", 0, blockId, Now.AddHours(-1), null, Now.AddMinutes(-1));

    private static ExerciseSync ExerciseDto(Guid workoutId, Guid? id = null) => new(id ?? Guid.NewGuid(), workoutId, Guid.NewGuid(), "Bench", null, 0, false, Now.AddMinutes(-1), null);

    private static SetSync SetDto(Guid exerciseId, Guid? id = null) => new(id ?? Guid.NewGuid(), exerciseId, 0, 80, 8, 2, 120, false, null, Now.AddMinutes(-1));

    private static Task<HttpResponseMessage> Send(HttpClient client, Guid deviceId, SyncChanges? changes = null, string? cursor = null)
        => client.PostAsJsonAsync("/sync", new SyncRequest(deviceId, cursor, changes));

    private static async Task<SyncResponse> Sync(HttpClient client, Guid deviceId, SyncChanges? changes = null, string? cursor = null)
    {
        var response = await Send(client, deviceId, changes, cursor);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<SyncResponse>())!;
    }

    private Task<Goal?> FindGoal(Guid id) => factory.WithDb(db => db.Goals.AsNoTracking().SingleOrDefaultAsync(g => g.Id == id));

    // ---- authentication

    [Fact]
    public async Task WithoutAToken_IsUnauthorized()
    {
        var response = await factory.CreateClient().PostAsJsonAsync("/sync", new SyncRequest(DeviceA, null, null));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task TokenWithTheWrongAudience_IsUnauthorized()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", factory.Token(Guid.NewGuid(), audience: "anon"));

        var response = await client.PostAsJsonAsync("/sync", new SyncRequest(DeviceA, null, null));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetIsNotAllowed()
    {
        var response = await factory.ClientFor(Guid.NewGuid()).GetAsync("/sync");

        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
    }

    // ---- request validation

    [Fact]
    public async Task EmptyBody_IsAFirstSyncWithNothingToApply()
    {
        var response = await Sync(factory.ClientFor(Guid.NewGuid()), DeviceA);

        Assert.False(response.fullResyncRequired);
        Assert.False(response.hasMore);
        Assert.Empty(response.changes.goals!);
    }

    [Fact]
    public async Task EmptyDeviceId_IsABadRequest()
    {
        var response = await Send(factory.ClientFor(Guid.NewGuid()), Guid.Empty);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData("garbage")]
    [InlineData("-1")]
    public async Task InvalidCursor_IsABadRequest(string cursor)
    {
        var response = await Send(factory.ClientFor(Guid.NewGuid()), DeviceA, cursor: cursor);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CursorInTheFuture_IsABadRequest()
    {
        var response = await Send(factory.ClientFor(Guid.NewGuid()), DeviceA, cursor: Now.AddHours(1).Ticks.ToString());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task StaleCursor_AsksTheClientToResyncAndAppliesNothing()
    {
        var userId = Guid.NewGuid();
        var goal = GoalDto();

        var response = await Sync(factory.ClientFor(userId), DeviceA, new SyncChanges(goals: [goal]), cursor: Now.AddDays(-91).Ticks.ToString());

        Assert.True(response.fullResyncRequired);
        Assert.Empty(response.changes.goals!);
        Assert.Null(await FindGoal(goal.Id));
    }

    [Fact]
    public async Task CursorJustInsideTheRetentionWindow_IsAccepted()
    {
        var response = await Sync(factory.ClientFor(Guid.NewGuid()), DeviceA, cursor: Now.AddDays(-89).Ticks.ToString());

        Assert.False(response.fullResyncRequired);
    }

    // ---- applying what the client sends

    [Fact]
    public async Task CreatesRecords_OwnedByTheTokenUserAndStampedWithTheDevice()
    {
        var userId = Guid.NewGuid();
        var goal = GoalDto();

        await Sync(factory.ClientFor(userId), DeviceA, new SyncChanges(goals: [goal]));

        var saved = await FindGoal(goal.Id);
        Assert.Equal(userId, saved!.UserId);
        Assert.Equal(DeviceA, saved.UpdatedByDeviceId);
        Assert.Equal(goal.Name, saved.Name);
        Assert.Equal(goal.ClientUpdateAt, saved.ClientUpdateAt);
    }

    [Fact]
    public async Task CreatesAWholeProgramHierarchyInOneRequest()
    {
        var userId = Guid.NewGuid();
        var movement = new MovementSync(Guid.NewGuid(), "Bench Press", ["chest"], Now.AddMinutes(-1), null);
        var program = ProgramDto();
        var block = BlockDto(program.Id);
        var workout = WorkoutDto(block.Id);
        var exercise = ExerciseDto(workout.Id);
        var set = SetDto(exercise.Id);

        await Sync(factory.ClientFor(userId), DeviceA, new SyncChanges(
            movements: [movement], programs: [program], blocks: [block], workouts: [workout], exercises: [exercise], sets: [set]));

        await factory.WithDb(async db =>
        {
            Assert.Equal(userId, (await db.Movements.SingleAsync(m => m.Id == movement.Id)).UserId);
            Assert.Equal(userId, (await db.Programs.SingleAsync(p => p.Id == program.Id)).UserId);
            Assert.Equal(program.Id, (await db.ProgramBlocks.SingleAsync(b => b.Id == block.Id)).ProgramId);
            Assert.Equal(block.Id, (await db.Workouts.SingleAsync(w => w.Id == workout.Id)).BlockId);
            Assert.Equal(workout.Id, (await db.Exercises.SingleAsync(e => e.Id == exercise.Id)).WorkoutId);
            Assert.Equal(exercise.Id, (await db.ExerciseSets.SingleAsync(s => s.Id == set.Id)).ExerciseId);
            return 0;
        });
    }

    [Fact]
    public async Task AWorkoutOutsideAnyProgram_NeedsNoParent()
    {
        var workout = WorkoutDto(null);

        await Sync(factory.ClientFor(Guid.NewGuid()), DeviceA, new SyncChanges(workouts: [workout]));

        Assert.NotNull(await factory.WithDb(db => db.Workouts.SingleOrDefaultAsync(w => w.Id == workout.Id)));
    }

    [Fact]
    public async Task UpdatesAnExistingRecord_WhenTheSnapshotIsNewer()
    {
        var client = factory.ClientFor(Guid.NewGuid());
        var id = Guid.NewGuid();
        await Sync(client, DeviceA, new SyncChanges(goals: [GoalDto(id, "Old", Now.AddMinutes(-10))]));

        await Sync(client, DeviceA, new SyncChanges(goals: [GoalDto(id, "New", Now.AddMinutes(-5))]));

        Assert.Equal("New", (await FindGoal(id))!.Name);
    }

    [Fact]
    public async Task IgnoresAnOlderSnapshot()
    {
        var client = factory.ClientFor(Guid.NewGuid());
        var id = Guid.NewGuid();
        await Sync(client, DeviceA, new SyncChanges(goals: [GoalDto(id, "Current", Now.AddMinutes(-5))]));

        await Sync(client, DeviceB, new SyncChanges(goals: [GoalDto(id, "Stale", Now.AddMinutes(-10))]));

        var saved = await FindGoal(id);
        Assert.Equal("Current", saved!.Name);
        Assert.Equal(DeviceA, saved.UpdatedByDeviceId);
    }

    [Fact]
    public async Task ResendingTheSameRecord_IsHarmless()
    {
        var client = factory.ClientFor(Guid.NewGuid());
        var goal = GoalDto();
        var changes = new SyncChanges(goals: [goal]);

        await Sync(client, DeviceA, changes);
        await Sync(client, DeviceA, changes);

        Assert.Equal(1, await factory.WithDb(db => db.Goals.CountAsync(g => g.Id == goal.Id)));
    }

    [Fact]
    public async Task TheSameIdTwiceInOneRequest_KeepsTheNewest()
    {
        var id = Guid.NewGuid();

        await Sync(factory.ClientFor(Guid.NewGuid()), DeviceA, new SyncChanges(
            goals: [GoalDto(id, "Older", Now.AddMinutes(-10)), GoalDto(id, "Newest", Now.AddMinutes(-2)), GoalDto(id, "Middle", Now.AddMinutes(-5))]));

        Assert.Equal("Newest", (await FindGoal(id))!.Name);
    }

    [Fact]
    public async Task DeleteIsStoredAsATombstone()
    {
        var client = factory.ClientFor(Guid.NewGuid());
        var id = Guid.NewGuid();
        var deletedAt = Now.AddMinutes(-3);
        await Sync(client, DeviceA, new SyncChanges(goals: [GoalDto(id, updatedAt: Now.AddMinutes(-10))]));

        await Sync(client, DeviceA, new SyncChanges(goals: [GoalDto(id, updatedAt: deletedAt, deletedAt: deletedAt)]));

        Assert.Equal(deletedAt, (await FindGoal(id))!.DeletedAt);
    }

    [Fact]
    public async Task UpdatesTheProfile_OfTheTokenUserOnly()
    {
        var userId = Guid.NewGuid();
        var someoneElse = Guid.NewGuid();
        var seededAt = Now.AddHours(-1);
        await factory.Seed(db =>
        {
            db.Users.Add(User.CreateOAuth(new UserSync(userId, Gender.Male, 80, Measurement.Metric, seededAt), "me", "me@example.com", null, "google", DeviceA, seededAt));
            db.Users.Add(User.CreateOAuth(new UserSync(someoneElse, Gender.Male, 70, Measurement.Metric, seededAt), "other", "other@example.com", null, "google", DeviceA, seededAt));
        });

        // The payload names someone else's id; it must be ignored in favour of the token's.
        await Sync(factory.ClientFor(userId), DeviceB, new SyncChanges(user: new UserSync(someoneElse, Gender.Female, 61.5, Measurement.Imperial, Now.AddMinutes(-1))));

        await factory.WithDb(async db =>
        {
            var mine = await db.Users.AsNoTracking().SingleAsync(u => u.Id == userId);
            Assert.Equal(61.5, mine.Weight);
            Assert.Equal(Gender.Female, mine.Gender);
            Assert.Equal(Measurement.Imperial, mine.Measurement);
            var theirs = await db.Users.AsNoTracking().SingleAsync(u => u.Id == someoneElse);
            Assert.Equal(70, theirs.Weight);
            return 0;
        });
    }

    [Fact]
    public async Task ProfileUpdateWithoutAProfileRow_IsNotFound()
    {
        var response = await Send(factory.ClientFor(Guid.NewGuid()), DeviceA,
            new SyncChanges(user: new UserSync(Guid.NewGuid(), Gender.Male, 80, Measurement.Metric, Now.AddMinutes(-1))));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ---- ownership and validation

    [Fact]
    public async Task ARecordThatBelongsToAnotherUser_IsForbiddenAndUntouched()
    {
        var victim = Guid.NewGuid();
        var id = Guid.NewGuid();
        await Sync(factory.ClientFor(victim), DeviceA, new SyncChanges(goals: [GoalDto(id, "Mine", Now.AddMinutes(-10))]));

        var response = await Send(factory.ClientFor(Guid.NewGuid()), DeviceB, new SyncChanges(goals: [GoalDto(id, "Hijacked", Now.AddMinutes(-1))]));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var saved = await FindGoal(id);
        Assert.Equal("Mine", saved!.Name);
        Assert.Equal(victim, saved.UserId);
    }

    [Fact]
    public async Task ForbiddenRecord_StopsTheWholeRequestSoNothingIsSaved()
    {
        var victim = Guid.NewGuid();
        var taken = Guid.NewGuid();
        await Sync(factory.ClientFor(victim), DeviceA, new SyncChanges(goals: [GoalDto(taken)]));
        var innocent = GoalDto();

        var response = await Send(factory.ClientFor(Guid.NewGuid()), DeviceB, new SyncChanges(goals: [innocent, GoalDto(taken)]));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Null(await FindGoal(innocent.Id));
    }

    [Fact]
    public async Task AnInvalidRecord_IsABadRequestAndNothingInTheRequestIsSaved()
    {
        var valid = GoalDto();
        var invalid = new MovementSync(Guid.NewGuid(), "   ", null, Now.AddMinutes(-1), null);

        var response = await Send(factory.ClientFor(Guid.NewGuid()), DeviceA, new SyncChanges(goals: [valid], movements: [invalid]));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Null(await FindGoal(valid.Id));
    }

    [Fact]
    public async Task ABlockWithoutItsProgram_IsABadRequest()
    {
        var block = BlockDto(Guid.NewGuid());

        var response = await Send(factory.ClientFor(Guid.NewGuid()), DeviceA, new SyncChanges(blocks: [block]));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(await factory.WithDb(db => db.ProgramBlocks.Where(b => b.Id == block.Id).ToListAsync()));
    }

    [Fact]
    public async Task ChildrenOfAnotherUsersParent_AreRejectedWithTheSameErrorAsAMissingParent()
    {
        var owner = Guid.NewGuid();
        var program = ProgramDto();
        var block = BlockDto(program.Id);
        var workout = WorkoutDto(block.Id);
        var exercise = ExerciseDto(workout.Id);
        await Sync(factory.ClientFor(owner), DeviceA, new SyncChanges(programs: [program], blocks: [block], workouts: [workout], exercises: [exercise]));
        var intruder = factory.ClientFor(Guid.NewGuid());

        var missing = await Send(intruder, DeviceB, new SyncChanges(blocks: [BlockDto(Guid.NewGuid())]));
        var missingMessage = await missing.Content.ReadAsStringAsync();

        foreach (var changes in new[]
        {
            new SyncChanges(blocks: [BlockDto(program.Id)]),
            new SyncChanges(workouts: [WorkoutDto(block.Id)]),
            new SyncChanges(exercises: [ExerciseDto(workout.Id)]),
            new SyncChanges(sets: [SetDto(exercise.Id)]),
        })
        {
            var response = await Send(intruder, DeviceB, changes);

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal(missingMessage, await response.Content.ReadAsStringAsync());
        }
    }

    [Fact]
    public async Task WorkoutWithAnUnknownBlock_ExerciseWithAnUnknownWorkout_AndSetWithAnUnknownExercise_AreBadRequests()
    {
        var client = factory.ClientFor(Guid.NewGuid());

        foreach (var changes in new[]
        {
            new SyncChanges(workouts: [WorkoutDto(Guid.NewGuid())]),
            new SyncChanges(exercises: [ExerciseDto(Guid.NewGuid())]),
            new SyncChanges(sets: [SetDto(Guid.NewGuid())]),
        })
        {
            Assert.Equal(HttpStatusCode.BadRequest, (await Send(client, DeviceA, changes)).StatusCode);
        }
    }

    // ---- what comes back

    [Fact]
    public async Task ReturnsWhatAnotherDeviceChanged_ButNotTheCallersOwnWrites()
    {
        var userId = Guid.NewGuid();
        var fromA = GoalDto(name: "From A");
        var fromB = GoalDto(name: "From B");
        await Sync(factory.ClientFor(userId), DeviceA, new SyncChanges(goals: [fromA]));

        var response = await Sync(factory.ClientFor(userId), DeviceB, new SyncChanges(goals: [fromB]));

        var pulled = Assert.Single(response.changes.goals!);
        Assert.Equal(fromA.Id, pulled.Id);
        Assert.Equal("From A", pulled.Name);
    }

    [Fact]
    public async Task ADeviceJoiningLater_PullsEverythingThatExists()
    {
        var userId = Guid.NewGuid();
        var program = ProgramDto();
        var goal = GoalDto();
        await Sync(factory.ClientFor(userId), DeviceA, new SyncChanges(goals: [goal], programs: [program]));

        var response = await Sync(factory.ClientFor(userId), DeviceB);

        Assert.Equal(goal.Id, Assert.Single(response.changes.goals!).Id);
        Assert.Equal(program.Id, Assert.Single(response.changes.programs!).Id);
    }

    [Fact]
    public async Task TombstonesAreDeliveredToOtherDevices()
    {
        var userId = Guid.NewGuid();
        var deletedAt = Now.AddMinutes(-3);
        var goal = GoalDto(updatedAt: deletedAt, deletedAt: deletedAt);
        await Sync(factory.ClientFor(userId), DeviceA, new SyncChanges(goals: [goal]));

        var response = await Sync(factory.ClientFor(userId), DeviceB);

        Assert.Equal(deletedAt, Assert.Single(response.changes.goals!).DeletedAt);
    }

    [Fact]
    public async Task NeverReturnsAnotherUsersRows()
    {
        await Sync(factory.ClientFor(Guid.NewGuid()), DeviceA, new SyncChanges(goals: [GoalDto()]));

        var response = await Sync(factory.ClientFor(Guid.NewGuid()), DeviceB);

        Assert.Empty(response.changes.goals!);
    }

    [Fact]
    public async Task ReturnsTheOtherDevicesProfileChange()
    {
        var userId = Guid.NewGuid();
        var seededAt = Now.AddHours(-1);
        await factory.Seed(db => db.Users.Add(
            User.CreateOAuth(new UserSync(userId, Gender.Male, 80, Measurement.Metric, seededAt), "me", "me@example.com", null, "google", DeviceA, seededAt)));
        await Sync(factory.ClientFor(userId), DeviceA, new SyncChanges(user: new UserSync(userId, Gender.Male, 77, Measurement.Metric, Now.AddMinutes(-1))));

        var response = await Sync(factory.ClientFor(userId), DeviceB);

        Assert.Equal(77, response.changes.user!.Weight);
    }

    [Fact]
    public async Task TheReturnedCursor_StartsJustBeforeThisPull()
    {
        var before = Now;

        var response = await Sync(factory.ClientFor(Guid.NewGuid()), DeviceA);

        Assert.True(long.TryParse(response.cursor, out var ticks));
        var cursor = new DateTime(ticks, DateTimeKind.Utc);
        Assert.InRange(cursor, before - SyncCursor.Overlap - TimeSpan.FromSeconds(1), Now - SyncCursor.Overlap + TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task TheReturnedCursor_ExcludesOlderRows_AndStillPicksUpNewOnes()
    {
        var userId = Guid.NewGuid();
        var old = Guid.NewGuid();
        var oldServerTime = Now.AddHours(-1);
        await factory.Seed(db => db.Goals.Add(Goal.Create(userId, GoalDto(old), DeviceA, oldServerTime)));
        var first = await Sync(factory.ClientFor(userId), DeviceB);
        Assert.Contains(old, first.changes.goals!.Select(g => g.Id));
        var fresh = GoalDto(name: "Fresh");
        await Sync(factory.ClientFor(userId), DeviceA, new SyncChanges(goals: [fresh]));

        var second = await Sync(factory.ClientFor(userId), DeviceB, cursor: first.cursor);

        Assert.DoesNotContain(old, second.changes.goals!.Select(g => g.Id));
        Assert.Contains(fresh.Id, second.changes.goals!.Select(g => g.Id));
    }

    [Fact]
    public async Task ACursorAfterTheWrite_SkipsThatRow()
    {
        var userId = Guid.NewGuid();
        await Sync(factory.ClientFor(userId), DeviceA, new SyncChanges(goals: [GoalDto()]));
        await Task.Delay(20);

        var response = await Sync(factory.ClientFor(userId), DeviceB, cursor: Now.Ticks.ToString());

        Assert.Empty(response.changes.goals!);
    }

    [Fact]
    public async Task EachResponseCarriesACursorAndNoMorePages()
    {
        var response = await Sync(factory.ClientFor(Guid.NewGuid()), DeviceA);

        Assert.False(string.IsNullOrEmpty(response.cursor));
        Assert.False(response.hasMore);
        Assert.False(response.fullResyncRequired);
    }
}

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

        Assert.False(response.FullResyncRequired);
        Assert.False(response.HasMore);
        Assert.Empty(response.Changes.Goals!);
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

        var response = await Sync(factory.ClientFor(userId), DeviceA, new SyncChanges(Goals: [goal]), cursor: Now.AddDays(-91).Ticks.ToString());

        Assert.True(response.FullResyncRequired);
        Assert.Empty(response.Changes.Goals!);
        Assert.Null(await FindGoal(goal.Id));
    }

    [Fact]
    public async Task CursorJustInsideTheRetentionWindow_IsAccepted()
    {
        var response = await Sync(factory.ClientFor(Guid.NewGuid()), DeviceA, cursor: Now.AddDays(-89).Ticks.ToString());

        Assert.False(response.FullResyncRequired);
    }

    // ---- applying what the client sends

    [Fact]
    public async Task CreatesRecords_OwnedByTheTokenUserAndStampedWithTheDevice()
    {
        var userId = Guid.NewGuid();
        var goal = GoalDto();

        await Sync(factory.ClientFor(userId), DeviceA, new SyncChanges(Goals: [goal]));

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
            Movements: [movement], Programs: [program], Blocks: [block], Workouts: [workout], Exercises: [exercise], Sets: [set]));

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

        await Sync(factory.ClientFor(Guid.NewGuid()), DeviceA, new SyncChanges(Workouts: [workout]));

        Assert.NotNull(await factory.WithDb(db => db.Workouts.SingleOrDefaultAsync(w => w.Id == workout.Id)));
    }

    [Fact]
    public async Task UpdatesAnExistingRecord_WhenTheSnapshotIsNewer()
    {
        var client = factory.ClientFor(Guid.NewGuid());
        var id = Guid.NewGuid();
        await Sync(client, DeviceA, new SyncChanges(Goals: [GoalDto(id, "Old", Now.AddMinutes(-10))]));

        await Sync(client, DeviceA, new SyncChanges(Goals: [GoalDto(id, "New", Now.AddMinutes(-5))]));

        Assert.Equal("New", (await FindGoal(id))!.Name);
    }

    [Fact]
    public async Task IgnoresAnOlderSnapshot()
    {
        var client = factory.ClientFor(Guid.NewGuid());
        var id = Guid.NewGuid();
        await Sync(client, DeviceA, new SyncChanges(Goals: [GoalDto(id, "Current", Now.AddMinutes(-5))]));

        await Sync(client, DeviceB, new SyncChanges(Goals: [GoalDto(id, "Stale", Now.AddMinutes(-10))]));

        var saved = await FindGoal(id);
        Assert.Equal("Current", saved!.Name);
        Assert.Equal(DeviceA, saved.UpdatedByDeviceId);
    }

    [Fact]
    public async Task ResendingTheSameRecord_IsHarmless()
    {
        var client = factory.ClientFor(Guid.NewGuid());
        var goal = GoalDto();
        var changes = new SyncChanges(Goals: [goal]);

        await Sync(client, DeviceA, changes);
        await Sync(client, DeviceA, changes);

        Assert.Equal(1, await factory.WithDb(db => db.Goals.CountAsync(g => g.Id == goal.Id)));
    }

    [Fact]
    public async Task TheSameIdTwiceInOneRequest_KeepsTheNewest()
    {
        var id = Guid.NewGuid();

        await Sync(factory.ClientFor(Guid.NewGuid()), DeviceA, new SyncChanges(
            Goals: [GoalDto(id, "Older", Now.AddMinutes(-10)), GoalDto(id, "Newest", Now.AddMinutes(-2)), GoalDto(id, "Middle", Now.AddMinutes(-5))]));

        Assert.Equal("Newest", (await FindGoal(id))!.Name);
    }

    [Fact]
    public async Task DeleteIsStoredAsATombstone()
    {
        var client = factory.ClientFor(Guid.NewGuid());
        var id = Guid.NewGuid();
        var deletedAt = Now.AddMinutes(-3);
        await Sync(client, DeviceA, new SyncChanges(Goals: [GoalDto(id, updatedAt: Now.AddMinutes(-10))]));

        await Sync(client, DeviceA, new SyncChanges(Goals: [GoalDto(id, updatedAt: deletedAt, deletedAt: deletedAt)]));

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
        await Sync(factory.ClientFor(userId), DeviceB, new SyncChanges(User: new UserSync(someoneElse, Gender.Female, 61.5, Measurement.Imperial, Now.AddMinutes(-1))));

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

    // ---- per-record results

    [Fact]
    public async Task EveryAppliedRecordIsListedByIdAndNothingIsRejected()
    {
        var program = ProgramDto();
        var block = BlockDto(program.Id);
        var goals = new[] { GoalDto(), GoalDto() };

        var response = await Sync(factory.ClientFor(Guid.NewGuid()), DeviceA, new SyncChanges(Goals: goals, Programs: [program], Blocks: [block]));

        Assert.Equal(goals.Select(g => g.Id), response.Results.Goals.Applied);
        Assert.Equal([program.Id], response.Results.Programs.Applied);
        Assert.Equal([block.Id], response.Results.Blocks.Applied);
        Assert.Empty(response.Results.Goals.Rejected);
        Assert.Empty(response.Results.Programs.Rejected);
        Assert.Empty(response.Results.Blocks.Rejected);
    }

    [Fact]
    public async Task TablesWithNothingSent_HaveEmptyResults()
    {
        var response = await Sync(factory.ClientFor(Guid.NewGuid()), DeviceA, new SyncChanges(Goals: [GoalDto()]));

        Assert.Empty(response.Results.Movements.Applied);
        Assert.Empty(response.Results.Sets.Rejected);
        Assert.Empty(response.Results.User.Applied);
    }

    [Fact]
    public async Task AnInvalidRecordIsRejectedWithItsReason_AndTheValidOnesStillApply()
    {
        var valid = GoalDto();
        var blank = GoalDto(name: "   ");

        var response = await Sync(factory.ClientFor(Guid.NewGuid()), DeviceA, new SyncChanges(Goals: [valid, blank]));

        Assert.Equal([valid.Id], response.Results.Goals.Applied);
        var rejection = Assert.Single(response.Results.Goals.Rejected);
        Assert.Equal(blank.Id, rejection.Id);
        Assert.Equal(RejectionReason.Invalid, rejection.Reason);
        Assert.False(string.IsNullOrWhiteSpace(rejection.Message));
        Assert.Null(rejection.ServerVersion);
        Assert.NotNull(await FindGoal(valid.Id));
        Assert.Null(await FindGoal(blank.Id));
    }

    [Fact]
    public async Task ARecordWithoutAClientTimestamp_IsRejectedAsInvalid()
    {
        var goal = GoalDto() with { ClientUpdateAt = default };

        var response = await Sync(factory.ClientFor(Guid.NewGuid()), DeviceA, new SyncChanges(Goals: [goal]));

        Assert.Equal(RejectionReason.Invalid, Assert.Single(response.Results.Goals.Rejected).Reason);
        Assert.Null(await FindGoal(goal.Id));
    }

    [Fact]
    public async Task InvalidRecordsInOneTableDoNotAffectOtherTables()
    {
        var goal = GoalDto();
        var invalidMovement = new MovementSync(Guid.NewGuid(), "   ", null, Now.AddMinutes(-1), null);

        var response = await Sync(factory.ClientFor(Guid.NewGuid()), DeviceA, new SyncChanges(Goals: [goal], Movements: [invalidMovement]));

        Assert.Equal([goal.Id], response.Results.Goals.Applied);
        Assert.Equal(RejectionReason.Invalid, Assert.Single(response.Results.Movements.Rejected).Reason);
    }

    [Fact]
    public async Task AStaleRecordIsRejectedWithTheNewerServerVersion()
    {
        var client = factory.ClientFor(Guid.NewGuid());
        var id = Guid.NewGuid();
        var current = GoalDto(id, "Current", Now.AddMinutes(-5));
        await Sync(client, DeviceA, new SyncChanges(Goals: [current]));

        var response = await Sync(client, DeviceB, new SyncChanges(Goals: [GoalDto(id, "Stale", Now.AddMinutes(-10))]));

        Assert.Empty(response.Results.Goals.Applied);
        var rejection = Assert.Single(response.Results.Goals.Rejected);
        Assert.Equal(RejectionReason.Stale, rejection.Reason);
        Assert.Equal(current, rejection.ServerVersion);
    }

    [Fact]
    public async Task ResendingAnAppliedRecord_IsReportedAsStale()
    {
        var client = factory.ClientFor(Guid.NewGuid());
        var changes = new SyncChanges(Goals: [GoalDto()]);
        await Sync(client, DeviceA, changes);

        var response = await Sync(client, DeviceA, changes);

        Assert.Equal(RejectionReason.Stale, Assert.Single(response.Results.Goals.Rejected).Reason);
    }

    [Fact]
    public async Task AStaleProfileIsRejectedWithTheServerVersion()
    {
        var userId = Guid.NewGuid();
        var seededAt = Now.AddMinutes(-5);
        await factory.Seed(db => db.Users.Add(
            User.CreateOAuth(new UserSync(userId, Gender.Male, 80, Measurement.Metric, seededAt), "me", "me@example.com", null, "google", DeviceA, seededAt)));

        var response = await Sync(factory.ClientFor(userId), DeviceB, new SyncChanges(User: new UserSync(userId, Gender.Female, 50, Measurement.Imperial, seededAt.AddMinutes(-1))));

        var rejection = Assert.Single(response.Results.User.Rejected);
        Assert.Equal(RejectionReason.Stale, rejection.Reason);
        Assert.Equal(80, rejection.ServerVersion!.Weight);
    }

    [Fact]
    public async Task AnAppliedProfileUpdateIsListed()
    {
        var userId = Guid.NewGuid();
        var seededAt = Now.AddMinutes(-5);
        await factory.Seed(db => db.Users.Add(
            User.CreateOAuth(new UserSync(userId, Gender.Male, 80, Measurement.Metric, seededAt), "me", "me@example.com", null, "google", DeviceA, seededAt)));

        var response = await Sync(factory.ClientFor(userId), DeviceB, new SyncChanges(User: new UserSync(userId, Gender.Male, 79, Measurement.Metric, Now.AddMinutes(-1))));

        Assert.Equal([userId], response.Results.User.Applied);
        Assert.Empty(response.Results.User.Rejected);
    }

    [Fact]
    public async Task AnInvalidProfileUpdateIsRejected()
    {
        var userId = Guid.NewGuid();
        var seededAt = Now.AddMinutes(-5);
        await factory.Seed(db => db.Users.Add(
            User.CreateOAuth(new UserSync(userId, Gender.Male, 80, Measurement.Metric, seededAt), "me", "me@example.com", null, "google", DeviceA, seededAt)));

        var response = await Sync(factory.ClientFor(userId), DeviceB, new SyncChanges(User: new UserSync(userId, Gender.Male, -3, Measurement.Metric, Now.AddMinutes(-1))));

        Assert.Equal(RejectionReason.Invalid, Assert.Single(response.Results.User.Rejected).Reason);
    }

    [Fact]
    public async Task ARecordThatBelongsToAnotherUser_IsRejectedAndUntouched()
    {
        var victim = Guid.NewGuid();
        var id = Guid.NewGuid();
        await Sync(factory.ClientFor(victim), DeviceA, new SyncChanges(Goals: [GoalDto(id, "Mine", Now.AddMinutes(-10))]));

        var response = await Sync(factory.ClientFor(Guid.NewGuid()), DeviceB, new SyncChanges(Goals: [GoalDto(id, "Hijacked", Now.AddMinutes(-1))]));

        var rejection = Assert.Single(response.Results.Goals.Rejected);
        Assert.Equal(RejectionReason.NotOwner, rejection.Reason);
        Assert.Null(rejection.ServerVersion);
        var saved = await FindGoal(id);
        Assert.Equal("Mine", saved!.Name);
        Assert.Equal(victim, saved.UserId);
    }

    [Fact]
    public async Task ARejectedForeignRecord_DoesNotStopTheRestOfTheRequest()
    {
        var taken = Guid.NewGuid();
        await Sync(factory.ClientFor(Guid.NewGuid()), DeviceA, new SyncChanges(Goals: [GoalDto(taken)]));
        var innocent = GoalDto();

        var response = await Sync(factory.ClientFor(Guid.NewGuid()), DeviceB, new SyncChanges(Goals: [innocent, GoalDto(taken)]));

        Assert.Equal([innocent.Id], response.Results.Goals.Applied);
        Assert.Equal(RejectionReason.NotOwner, Assert.Single(response.Results.Goals.Rejected).Reason);
        Assert.NotNull(await FindGoal(innocent.Id));
    }

    [Fact]
    public async Task ChildrenWhoseParentIsMissing_AreRejectedAndNotSaved()
    {
        var block = BlockDto(Guid.NewGuid());
        var orphanWorkout = WorkoutDto(Guid.NewGuid());
        var orphanExercise = ExerciseDto(Guid.NewGuid());
        var orphanSet = SetDto(Guid.NewGuid());

        var response = await Sync(factory.ClientFor(Guid.NewGuid()), DeviceA,
            new SyncChanges(Blocks: [block], Workouts: [orphanWorkout], Exercises: [orphanExercise], Sets: [orphanSet]));

        Assert.Equal(RejectionReason.ParentNotFound, Assert.Single(response.Results.Blocks.Rejected).Reason);
        Assert.Equal(RejectionReason.ParentNotFound, Assert.Single(response.Results.Workouts.Rejected).Reason);
        Assert.Equal(RejectionReason.ParentNotFound, Assert.Single(response.Results.Exercises.Rejected).Reason);
        Assert.Equal(RejectionReason.ParentNotFound, Assert.Single(response.Results.Sets.Rejected).Reason);
        Assert.Empty(await factory.WithDb(db => db.ProgramBlocks.Where(b => b.Id == block.Id).ToListAsync()));
        Assert.Empty(await factory.WithDb(db => db.Workouts.Where(w => w.Id == orphanWorkout.Id).ToListAsync()));
    }

    [Fact]
    public async Task ChildrenOfAnotherUsersParent_AreRejectedWithTheSameErrorAsAMissingParent()
    {
        var owner = Guid.NewGuid();
        var program = ProgramDto();
        var block = BlockDto(program.Id);
        var workout = WorkoutDto(block.Id);
        var exercise = ExerciseDto(workout.Id);
        await Sync(factory.ClientFor(owner), DeviceA, new SyncChanges(Programs: [program], Blocks: [block], Workouts: [workout], Exercises: [exercise]));
        var intruder = factory.ClientFor(Guid.NewGuid());

        var missing = (await Sync(intruder, DeviceB, new SyncChanges(Blocks: [BlockDto(Guid.NewGuid())]))).Results.Blocks.Rejected.Single();

        var foreign = await Sync(intruder, DeviceB, new SyncChanges(
            Blocks: [BlockDto(program.Id)], Workouts: [WorkoutDto(block.Id)], Exercises: [ExerciseDto(workout.Id)], Sets: [SetDto(exercise.Id)]));

        var blockRejection = foreign.Results.Blocks.Rejected.Single();
        var workoutRejection = foreign.Results.Workouts.Rejected.Single();
        var exerciseRejection = foreign.Results.Exercises.Rejected.Single();
        var setRejection = foreign.Results.Sets.Rejected.Single();
        foreach (var (reason, message) in new[]
        {
            (blockRejection.Reason, blockRejection.Message),
            (workoutRejection.Reason, workoutRejection.Message),
            (exerciseRejection.Reason, exerciseRejection.Message),
            (setRejection.Reason, setRejection.Message),
        })
        {
            Assert.Equal(missing.Reason, reason);
            Assert.Equal(missing.Message, message);
        }
    }

    [Fact]
    public async Task ARecordCannotMoveToAnotherParent()
    {
        var client = factory.ClientFor(Guid.NewGuid());
        var program = ProgramDto();
        var otherProgram = ProgramDto();
        var block = BlockDto(program.Id);
        await Sync(client, DeviceA, new SyncChanges(Programs: [program, otherProgram], Blocks: [block]));

        var response = await Sync(client, DeviceA, new SyncChanges(Blocks: [BlockDto(otherProgram.Id, block.Id) with { ClientUpdateAt = Now }]));

        Assert.Equal(RejectionReason.Invalid, Assert.Single(response.Results.Blocks.Rejected).Reason);
        Assert.Equal(program.Id, (await factory.WithDb(db => db.ProgramBlocks.AsNoTracking().SingleAsync(b => b.Id == block.Id))).ProgramId);
    }

    [Fact]
    public async Task MixedRequest_ReportsEachRecordInExactlyOneList()
    {
        var client = factory.ClientFor(Guid.NewGuid());
        var staleId = Guid.NewGuid();
        await Sync(client, DeviceA, new SyncChanges(Goals: [GoalDto(staleId, updatedAt: Now.AddMinutes(-1))]));
        var fresh = GoalDto();
        var invalid = GoalDto(name: "");
        var stale = GoalDto(staleId, updatedAt: Now.AddMinutes(-9));

        var response = await Sync(client, DeviceB, new SyncChanges(Goals: [fresh, invalid, stale]));

        Assert.Equal([fresh.Id], response.Results.Goals.Applied);
        Assert.Equal([invalid.Id, stale.Id], response.Results.Goals.Rejected.Select(r => r.Id));
    }

    [Fact]
    public async Task ResultsAreEmptyWhenTheClientMustResync()
    {
        var response = await Sync(factory.ClientFor(Guid.NewGuid()), DeviceA, new SyncChanges(Goals: [GoalDto()]), cursor: Now.AddDays(-91).Ticks.ToString());

        Assert.Empty(response.Results.Goals.Applied);
        Assert.Empty(response.Results.Goals.Rejected);
    }

    // ---- what comes back

    [Fact]
    public async Task ReturnsWhatAnotherDeviceChanged_ButNotTheCallersOwnWrites()
    {
        var userId = Guid.NewGuid();
        var fromA = GoalDto(name: "From A");
        var fromB = GoalDto(name: "From B");
        await Sync(factory.ClientFor(userId), DeviceA, new SyncChanges(Goals: [fromA]));

        var response = await Sync(factory.ClientFor(userId), DeviceB, new SyncChanges(Goals: [fromB]));

        var pulled = Assert.Single(response.Changes.Goals!);
        Assert.Equal(fromA.Id, pulled.Id);
        Assert.Equal("From A", pulled.Name);
    }

    [Fact]
    public async Task ADeviceJoiningLater_PullsEverythingThatExists()
    {
        var userId = Guid.NewGuid();
        var program = ProgramDto();
        var goal = GoalDto();
        await Sync(factory.ClientFor(userId), DeviceA, new SyncChanges(Goals: [goal], Programs: [program]));

        var response = await Sync(factory.ClientFor(userId), DeviceB);

        Assert.Equal(goal.Id, Assert.Single(response.Changes.Goals!).Id);
        Assert.Equal(program.Id, Assert.Single(response.Changes.Programs!).Id);
    }

    [Fact]
    public async Task TombstonesAreDeliveredToOtherDevices()
    {
        var userId = Guid.NewGuid();
        var deletedAt = Now.AddMinutes(-3);
        var goal = GoalDto(updatedAt: deletedAt, deletedAt: deletedAt);
        await Sync(factory.ClientFor(userId), DeviceA, new SyncChanges(Goals: [goal]));

        var response = await Sync(factory.ClientFor(userId), DeviceB);

        Assert.Equal(deletedAt, Assert.Single(response.Changes.Goals!).DeletedAt);
    }

    [Fact]
    public async Task NeverReturnsAnotherUsersRows()
    {
        await Sync(factory.ClientFor(Guid.NewGuid()), DeviceA, new SyncChanges(Goals: [GoalDto()]));

        var response = await Sync(factory.ClientFor(Guid.NewGuid()), DeviceB);

        Assert.Empty(response.Changes.Goals!);
    }

    [Fact]
    public async Task ReturnsTheOtherDevicesProfileChange()
    {
        var userId = Guid.NewGuid();
        var seededAt = Now.AddHours(-1);
        await factory.Seed(db => db.Users.Add(
            User.CreateOAuth(new UserSync(userId, Gender.Male, 80, Measurement.Metric, seededAt), "me", "me@example.com", null, "google", DeviceA, seededAt)));
        await Sync(factory.ClientFor(userId), DeviceA, new SyncChanges(User: new UserSync(userId, Gender.Male, 77, Measurement.Metric, Now.AddMinutes(-1))));

        var response = await Sync(factory.ClientFor(userId), DeviceB);

        Assert.Equal(77, response.Changes.User!.Weight);
    }

    [Fact]
    public async Task TheReturnedCursor_StartsJustBeforeThisPull()
    {
        var before = Now;

        var response = await Sync(factory.ClientFor(Guid.NewGuid()), DeviceA);

        Assert.True(long.TryParse(response.Cursor, out var ticks));
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
        Assert.Contains(old, first.Changes.Goals!.Select(g => g.Id));
        var fresh = GoalDto(name: "Fresh");
        await Sync(factory.ClientFor(userId), DeviceA, new SyncChanges(Goals: [fresh]));

        var second = await Sync(factory.ClientFor(userId), DeviceB, cursor: first.Cursor);

        Assert.DoesNotContain(old, second.Changes.Goals!.Select(g => g.Id));
        Assert.Contains(fresh.Id, second.Changes.Goals!.Select(g => g.Id));
    }

    [Fact]
    public async Task ACursorAfterTheWrite_SkipsThatRow()
    {
        var userId = Guid.NewGuid();
        await Sync(factory.ClientFor(userId), DeviceA, new SyncChanges(Goals: [GoalDto()]));
        await Task.Delay(20);

        var response = await Sync(factory.ClientFor(userId), DeviceB, cursor: Now.Ticks.ToString());

        Assert.Empty(response.Changes.Goals!);
    }

    [Fact]
    public async Task EachResponseCarriesACursorAndNoMorePages()
    {
        var response = await Sync(factory.ClientFor(Guid.NewGuid()), DeviceA);

        Assert.False(string.IsNullOrEmpty(response.Cursor));
        Assert.False(response.HasMore);
        Assert.False(response.FullResyncRequired);
    }
}

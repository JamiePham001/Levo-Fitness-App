using System.Security.Claims;
using backend.Database;
using backend.Entities.ExerciseSets;
using backend.Entities.Exercises;
using backend.Entities.Goals;
using backend.Entities.Movements;
using backend.Entities.ProgramBlocks;
using backend.Entities.Programs;
using backend.Entities.Users;
using backend.Entities.Workouts;
using Microsoft.AspNetCore.Http.HttpResults;

namespace backend.Features.Sync;

public static class Sync
{
    public const string Route = "/sync";

    public static void MapSync(this IEndpointRouteBuilder app)
    {
        app.MapPost(Route, Handle)
           .WithName("SyncData")
           .WithSummary("Merge client and server data")
           .Produces<SyncResponse>()
           .RequireAuthorization();
    }

    // Applies the changes the client sends, then returns what changed on the server since the client's cursor.
    public static async Task<Results<Ok<SyncResponse>, BadRequest<string>, NotFound, ForbidHttpResult>> Handle(AppDbContext db, ClaimsPrincipal user, SyncRequest req)
    {
        var userId = Guid.Parse(user.FindFirst("sub")!.Value);   // trusted: the signature was verified
        var deviceId = req.deviceId;
        var changes = req.changes ?? new SyncChanges();
        var now = DateTime.UtcNow;

        if (deviceId == Guid.Empty) return TypedResults.BadRequest("deviceId is required.");
        if (!SyncCursor.TryParse(req.cursor, now, out var since)) return TypedResults.BadRequest("Invalid cursor.");

        // The client may have missed tombstones that were purged, so it has to start over.
        if (SyncCursor.IsStale(since, now)) return TypedResults.Ok(new SyncResponse(req.cursor ?? "", false, true, SyncChanges.Empty));

        try
        {
            // Apply what the client sent, parents before children. A record is created when the server has no
            // row with that id; a row that belongs to another user is never touched.
            if (changes.user is not null)
            {
                var profile = await db.Users.FindAsync(userId);
                if (profile is null) return TypedResults.NotFound();
                profile.ApplySync(changes.user, deviceId, now);
            }

            foreach (var goal in changes.goals ?? [])
            {
                var existing = await db.Goals.FindAsync(goal.Id);
                if (existing is null) db.Goals.Add(Goal.Create(userId, goal, deviceId, now));
                else if (existing.UserId != userId) return TypedResults.Forbid();
                else existing.ApplySync(goal, deviceId, now);
            }

            foreach (var movement in changes.movements ?? [])
            {
                var existing = await db.Movements.FindAsync(movement.Id);
                if (existing is null) db.Movements.Add(Movement.Create(userId, movement, deviceId, now));
                else if (existing.UserId != userId) return TypedResults.Forbid();
                else existing.ApplySync(movement, deviceId, now);
            }

            foreach (var program in changes.programs ?? [])
            {
                var existing = await db.Programs.FindAsync(program.Id);
                if (existing is null) db.Programs.Add(TrainingProgram.Create(userId, program, deviceId, now));
                else if (existing.UserId != userId) return TypedResults.Forbid();
                else existing.ApplySync(program, deviceId, now);
            }

            foreach (var block in changes.blocks ?? [])
            {
                var existing = await db.ProgramBlocks.FindAsync(block.Id);
                if (existing is null)
                {
                    var parent = await db.Programs.FindAsync(block.ProgramId);
                    if (parent is null || parent.UserId != userId) return TypedResults.BadRequest(ParentNotFound);
                    db.ProgramBlocks.Add(ProgramBlock.Create(userId, block, deviceId, now));
                }
                else if (existing.UserId != userId) return TypedResults.Forbid();
                else existing.ApplySync(block, deviceId, now);
            }

            foreach (var workout in changes.workouts ?? [])
            {
                var existing = await db.Workouts.FindAsync(workout.Id);
                if (existing is null)
                {
                    if (workout.BlockId is { } blockId)
                    {
                        var parent = await db.ProgramBlocks.FindAsync(blockId);
                        if (parent is null || parent.UserId != userId) return TypedResults.BadRequest(ParentNotFound);
                    }
                    db.Workouts.Add(Workout.Create(userId, workout, deviceId, now));
                }
                else if (existing.UserId != userId) return TypedResults.Forbid();
                else existing.ApplySync(workout, deviceId, now);
            }

            foreach (var exercise in changes.exercises ?? [])
            {
                var existing = await db.Exercises.FindAsync(exercise.Id);
                if (existing is null)
                {
                    var parent = await db.Workouts.FindAsync(exercise.WorkoutId);
                    if (parent is null || parent.UserId != userId) return TypedResults.BadRequest(ParentNotFound);
                    db.Exercises.Add(Exercise.Create(userId, exercise, deviceId, now));
                }
                else if (existing.UserId != userId) return TypedResults.Forbid();
                else existing.ApplySync(exercise, deviceId, now);
            }

            foreach (var set in changes.sets ?? [])
            {
                var existing = await db.ExerciseSets.FindAsync(set.Id);
                if (existing is null)
                {
                    var parent = await db.Exercises.FindAsync(set.ExerciseId);
                    if (parent is null || parent.UserId != userId) return TypedResults.BadRequest(ParentNotFound);
                    db.ExerciseSets.Add(ExerciseSet.Create(userId, set, deviceId, now));
                }
                else if (existing.UserId != userId) return TypedResults.Forbid();
                else existing.ApplySync(set, deviceId, now);
            }

            await db.SaveChangesAsync();
        }
        catch (ArgumentException ex)
        {
            // Entity validation failed: nothing was saved, so the client can fix the record and retry.
            return TypedResults.BadRequest(ex.Message);
        }

        // Send back everything other devices changed since the client's cursor.
        var pulled = await SyncPull.BuildAsync(db, userId, deviceId, since, now);
        return TypedResults.Ok(new SyncResponse(SyncCursor.Create(now), false, false, pulled));
    }

    // Deliberately the same message whether the parent is missing or belongs to someone else.
    private const string ParentNotFound = "A record refers to a parent that does not exist.";
}

public sealed record SyncChanges(
    UserSync? user = null,
    GoalSync[]? goals = null,
    MovementSync[]? movements = null,
    ProgramSync[]? programs = null,
    BlockSync[]? blocks = null,
    WorkoutSync[]? workouts = null,
    ExerciseSync[]? exercises = null,
    SetSync[]? sets = null)
{
    // What the server returns when nothing changed: every list present and empty.
    public static SyncChanges Empty => new(null, [], [], [], [], [], [], []);
}

public sealed record SyncRequest(Guid deviceId, string? cursor, SyncChanges? changes);

public sealed record SyncResponse(string cursor, bool hasMore, bool fullResyncRequired, SyncChanges changes);

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

    // Applies the changes the client sends (SyncPush), then returns what changed on the server since the
    // client's cursor (SyncPull). The response says, record by record, what was applied and what was rejected.
    public static async Task<Results<Ok<SyncResponse>, BadRequest<string>>> Handle(AppDbContext db, ClaimsPrincipal user, SyncRequest req)
    {
        var userId = Guid.Parse(user.FindFirst("sub")!.Value);   // trusted: the signature was verified
        var deviceId = req.DeviceId;
        var changes = req.Changes ?? new SyncChanges();
        var now = DateTime.UtcNow;

        if (deviceId == Guid.Empty) return TypedResults.BadRequest("deviceId is required.");
        if (!SyncCursor.TryParse(req.Cursor, now, out var since)) return TypedResults.BadRequest("Invalid cursor.");

        // The client may have missed tombstones that were purged, so it has to start over.
        if (SyncCursor.IsStale(since, now)) return TypedResults.Ok(new SyncResponse(req.Cursor ?? "", false, true, SyncResults.Empty, SyncChanges.Empty));

        var results = await SyncPush.ApplyAsync(db, userId, deviceId, changes, now);
        var pulled = await SyncPull.BuildAsync(db, userId, deviceId, since, now);

        return TypedResults.Ok(new SyncResponse(SyncCursor.Create(now), false, false, results, pulled));
    }
}

public sealed record SyncChanges(
    UserSync? User = null,
    GoalSync[]? Goals = null,
    MovementSync[]? Movements = null,
    ProgramSync[]? Programs = null,
    BlockSync[]? Blocks = null,
    WorkoutSync[]? Workouts = null,
    ExerciseSync[]? Exercises = null,
    SetSync[]? Sets = null)
{
    // What the server returns when nothing changed: every list present and empty.
    public static SyncChanges Empty => new(null, [], [], [], [], [], [], []);
}

public sealed record SyncRequest(Guid DeviceId, string? Cursor, SyncChanges? Changes);

public sealed record SyncResponse(string Cursor, bool HasMore, bool FullResyncRequired, SyncResults Results, SyncChanges Changes);

// What happened to each record the client sent, per table. A record appears in exactly one of the two lists.
public sealed record SyncResults(
    TableResult<UserSync> User,
    TableResult<GoalSync> Goals,
    TableResult<MovementSync> Movements,
    TableResult<ProgramSync> Programs,
    TableResult<BlockSync> Blocks,
    TableResult<WorkoutSync> Workouts,
    TableResult<ExerciseSync> Exercises,
    TableResult<SetSync> Sets)
{
    public static SyncResults Empty => new(
        TableResult.None<UserSync>(), TableResult.None<GoalSync>(), TableResult.None<MovementSync>(), TableResult.None<ProgramSync>(),
        TableResult.None<BlockSync>(), TableResult.None<WorkoutSync>(), TableResult.None<ExerciseSync>(), TableResult.None<SetSync>());
}

public sealed record TableResult<T>(Guid[] Applied, SyncRejection<T>[] Rejected) where T : class;

public static class TableResult
{
    public static TableResult<T> None<T>() where T : class => new([], []);
    public static TableResult<T> OneApplied<T>(Guid id) where T : class => new([id], []);
    public static TableResult<T> OneRejected<T>(SyncRejection<T> rejection) where T : class => new([], [rejection]);
}

// Why a record was not applied. ServerVersion is set only for Stale: the newer record the server holds.
public sealed record SyncRejection<T>(Guid Id, string Reason, string? Message = null, T? ServerVersion = null) where T : class;

public static class RejectionReason
{
    public const string Stale = "stale";                  // the server already has a newer version
    public const string Invalid = "invalid";              // the record failed validation
    public const string NotOwner = "notOwner";            // the id belongs to another user
    public const string ParentNotFound = "parentNotFound"; // the parent is missing or not the user's
    public const string NotFound = "notFound";            // the profile does not exist yet
}

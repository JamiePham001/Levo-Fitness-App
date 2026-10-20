using backend.Database;
using backend.Entities.ExerciseSets;
using backend.Entities.Exercises;
using backend.Entities.Goals;
using backend.Entities.Movements;
using backend.Entities.ProgramBlocks;
using backend.Entities.Programs;
using backend.Entities.Users;
using backend.Entities.Workouts;
using Microsoft.EntityFrameworkCore;

namespace backend.Features.Sync;

public static class SyncPush
{
    // Deliberately the same message whether the parent is missing or belongs to someone else.
    public const string ParentNotFoundMessage = "A record refers to a parent that does not exist.";

    // Applies what the client sent, parents before children, and saves. Every record is judged on its own:
    // an invalid, stale or foreign record is rejected with a reason and the rest still apply. A record is
    // created when the server has no row with that id; a row that belongs to another user is never touched.
    public static async Task<SyncResults> ApplyAsync(AppDbContext db, Guid userId, Guid deviceId, SyncChanges changes, DateTime now)
    {
        var user = await ApplyProfileAsync(db, userId, deviceId, changes.User, now);

        var goals = await UpsertAsync(db.Goals, changes.Goals, userId,
            idOf: g => g.Id, ownerOf: g => g.UserId, create: g => Goal.Create(userId, g, deviceId, now),
            apply: (e, g) => e.ApplySync(g, deviceId, now), toSync: e => e.ToSync());

        var movements = await UpsertAsync(db.Movements, changes.Movements, userId,
            idOf: m => m.Id, ownerOf: m => m.UserId, create: m => Movement.Create(userId, m, deviceId, now),
            apply: (e, m) => e.ApplySync(m, deviceId, now), toSync: e => e.ToSync());

        var programs = await UpsertAsync(db.Programs, changes.Programs, userId,
            idOf: p => p.Id, ownerOf: p => p.UserId, create: p => TrainingProgram.Create(userId, p, deviceId, now),
            apply: (e, p) => e.ApplySync(p, deviceId, now), toSync: e => e.ToSync());

        var blocks = await UpsertAsync(db.ProgramBlocks, changes.Blocks, userId,
            idOf: b => b.Id, ownerOf: b => b.UserId, create: b => ProgramBlock.Create(userId, b, deviceId, now),
            apply: (e, b) => e.ApplySync(b, deviceId, now), toSync: e => e.ToSync(),
            parentOk: async b => await OwnsAsync(db.Programs, b.ProgramId, p => p.UserId, userId));

        var workouts = await UpsertAsync(db.Workouts, changes.Workouts, userId,
            idOf: w => w.Id, ownerOf: w => w.UserId, create: w => Workout.Create(userId, w, deviceId, now),
            apply: (e, w) => e.ApplySync(w, deviceId, now), toSync: e => e.ToSync(),
            parentOk: async w => w.BlockId is not { } blockId || await OwnsAsync(db.ProgramBlocks, blockId, b => b.UserId, userId));

        var exercises = await UpsertAsync(db.Exercises, changes.Exercises, userId,
            idOf: x => x.Id, ownerOf: x => x.UserId, create: x => Exercise.Create(userId, x, deviceId, now),
            apply: (e, x) => e.ApplySync(x, deviceId, now), toSync: e => e.ToSync(),
            parentOk: async x => await OwnsAsync(db.Workouts, x.WorkoutId, w => w.UserId, userId));

        var sets = await UpsertAsync(db.ExerciseSets, changes.Sets, userId,
            idOf: s => s.Id, ownerOf: s => s.UserId, create: s => ExerciseSet.Create(userId, s, deviceId, now),
            apply: (e, s) => e.ApplySync(s, deviceId, now), toSync: e => e.ToSync(),
            parentOk: async s => await OwnsAsync(db.Exercises, s.ExerciseId, x => x.UserId, userId));

        await db.SaveChangesAsync();
        return new SyncResults(user, goals, movements, programs, blocks, workouts, exercises, sets);
    }

    // The profile row is created at signup, never by sync, and is always the token user's: the id in the
    // payload is ignored.
    private static async Task<TableResult<UserSync>> ApplyProfileAsync(AppDbContext db, Guid userId, Guid deviceId, UserSync? dto, DateTime now)
    {
        if (dto is null) return TableResult.None<UserSync>();

        var profile = await db.Users.FindAsync(userId);
        if (profile is null) return TableResult.OneRejected(new SyncRejection<UserSync>(userId, RejectionReason.NotFound, "The profile does not exist yet."));

        try
        {
            return profile.ApplySync(dto, deviceId, now)
                ? TableResult.OneApplied<UserSync>(userId)
                : TableResult.OneRejected(new SyncRejection<UserSync>(userId, RejectionReason.Stale, ServerVersion: profile.ToSync()));
        }
        catch (ArgumentException ex)
        {
            return TableResult.OneRejected(new SyncRejection<UserSync>(userId, RejectionReason.Invalid, ex.Message));
        }
    }

    private static async Task<TableResult<TDto>> UpsertAsync<TEntity, TDto>(
        DbSet<TEntity> set, TDto[]? dtos, Guid userId,
        Func<TDto, Guid> idOf, Func<TEntity, Guid> ownerOf, Func<TDto, TEntity> create,
        Func<TEntity, TDto, bool> apply, Func<TEntity, TDto> toSync,
        Func<TDto, Task<bool>>? parentOk = null)
        where TEntity : class
        where TDto : class
    {
        var applied = new List<Guid>();
        var rejected = new List<SyncRejection<TDto>>();

        foreach (var dto in dtos ?? [])
        {
            var id = idOf(dto);
            try
            {
                var existing = await set.FindAsync(id);
                if (existing is null)
                {
                    if (parentOk is not null && !await parentOk(dto))
                    {
                        rejected.Add(new SyncRejection<TDto>(id, RejectionReason.ParentNotFound, ParentNotFoundMessage));
                        continue;
                    }

                    set.Add(create(dto));
                    applied.Add(id);
                }
                else if (ownerOf(existing) != userId)
                {
                    rejected.Add(new SyncRejection<TDto>(id, RejectionReason.NotOwner, "The record belongs to another user."));
                }
                else if (apply(existing, dto))
                {
                    applied.Add(id);
                }
                else
                {
                    // The server already holds a newer version; send it back so the client can replace its copy.
                    rejected.Add(new SyncRejection<TDto>(id, RejectionReason.Stale, ServerVersion: toSync(existing)));
                }
            }
            catch (ArgumentException ex)
            {
                // Entities validate before they change, so a rejected record leaves no trace.
                rejected.Add(new SyncRejection<TDto>(id, RejectionReason.Invalid, ex.Message));
            }
        }

        return new TableResult<TDto>([.. applied], [.. rejected]);
    }

    // Parents created earlier in the same request are found through the change tracker.
    private static async Task<bool> OwnsAsync<TParent>(DbSet<TParent> parents, Guid id, Func<TParent, Guid> ownerOf, Guid userId) where TParent : class
    {
        var parent = await parents.FindAsync(id);
        return parent is not null && ownerOf(parent) == userId;
    }
}

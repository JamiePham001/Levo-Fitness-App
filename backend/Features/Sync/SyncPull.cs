using backend.Database;
using backend.Entities;
using Microsoft.EntityFrameworkCore;

namespace backend.Features.Sync;

public static class SyncPull
{
    // Everything the user's other devices changed in (since, until], as the records clients understand.
    // Tombstones are included so the client can delete its copy. Rows written by this device are left out.
    public static async Task<SyncChanges> BuildAsync(AppDbContext db, Guid userId, Guid deviceId, DateTime since, DateTime until)
    {
        var profile = await Changed(db.Users.AsNoTracking().Where(u => u.Id == userId), since, until, deviceId).FirstOrDefaultAsync();
        var goals = await Changed(db.Goals.AsNoTracking().Where(g => g.UserId == userId), since, until, deviceId).ToArrayAsync();
        var movements = await Changed(db.Movements.AsNoTracking().Where(m => m.UserId == userId), since, until, deviceId).ToArrayAsync();
        var programs = await Changed(db.Programs.AsNoTracking().Where(p => p.UserId == userId), since, until, deviceId).ToArrayAsync();
        var blocks = await Changed(db.ProgramBlocks.AsNoTracking().Where(b => b.UserId == userId), since, until, deviceId).ToArrayAsync();
        var workouts = await Changed(db.Workouts.AsNoTracking().Where(w => w.UserId == userId), since, until, deviceId).ToArrayAsync();
        var exercises = await Changed(db.Exercises.AsNoTracking().Where(e => e.UserId == userId), since, until, deviceId).ToArrayAsync();
        var sets = await Changed(db.ExerciseSets.AsNoTracking().Where(s => s.UserId == userId), since, until, deviceId).ToArrayAsync();

        return new SyncChanges(
            user: profile?.ToSync(),
            goals: [.. goals.Select(g => g.ToSync())],
            movements: [.. movements.Select(m => m.ToSync())],
            programs: [.. programs.Select(p => p.ToSync())],
            blocks: [.. blocks.Select(b => b.ToSync())],
            workouts: [.. workouts.Select(w => w.ToSync())],
            exercises: [.. exercises.Select(e => e.ToSync())],
            sets: [.. sets.Select(s => s.ToSync())]);
    }

    private static IQueryable<T> Changed<T>(IQueryable<T> rows, DateTime since, DateTime until, Guid deviceId) where T : SyncEntity
        => rows
            .Where(r => r.ServerUpdateAt > since && r.ServerUpdateAt <= until && r.UpdatedByDeviceId != deviceId)
            .OrderBy(r => r.ServerUpdateAt);
}

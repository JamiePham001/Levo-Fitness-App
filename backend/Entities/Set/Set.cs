namespace backend.Entities.ExerciseSets;

public class ExerciseSet : SyncEntity
{
    // Client-generated so sets can be created offline.
    public Guid Id { get; private set; }
    public Guid UserId { get; private set; }
    // Parent exercise; fixed at creation.
    public Guid ExerciseId { get; private set; }
    public int Order { get; private set; }
    // Null until the set is planned or performed.
    public double? Weight { get; private set; }
    public int? Reps { get; private set; }
    // Reps in reserve.
    public double? RIR { get; private set; }
    // Seconds; defaults to 2 minutes.
    public double RestTime { get; private set; } = 120;
    public bool Completed { get; private set; }

    public static ExerciseSet Create(Guid userId, SetSync dto, Guid deviceId, DateTime now)
    {
        Guard.NotEmpty(dto.Id, "Id");
        Guard.NotEmpty(userId, "UserId");
        Guard.NotEmpty(dto.ExerciseId, "ExerciseId");
        var set = new ExerciseSet { Id = dto.Id, UserId = userId, ExerciseId = dto.ExerciseId };
        set.ApplySync(dto, deviceId, now);
        return set;
    }

    public bool ApplySync(SetSync dto, Guid deviceId, DateTime now)
    {
        if (dto.ExerciseId != ExerciseId) throw new ArgumentException("A set cannot move to another exercise.");
        Guard.NonNegative(dto.Order, "Order");
        Guard.Positive(dto.Weight, "Weight");
        Guard.Positive(dto.Reps, "Reps");
        Guard.InRange(dto.RIR, 0, 10, "RIR");
        Guard.InRange(dto.RestTime, 0, 3600, "RestTime");
        if (!BeginSync(dto.ClientUpdateAt, dto.DeletedAt, deviceId, now)) return false;

        Order = dto.Order;
        Weight = dto.Weight;
        Reps = dto.Reps;
        RIR = dto.RIR;
        RestTime = dto.RestTime;
        Completed = dto.Completed;
        return true;
    }
}

namespace backend.Entities.Movements;

public class Movement : SyncEntity
{
    private const int MaxKeywords = 20;

    // Client-generated so movements can be created offline.
    public Guid Id { get; private set; }
    public Guid UserId { get; private set; }
    public string Name { get; private set; } = default!;
    public List<string> Keywords { get; private set; } = [];

    public static Movement Create(Guid userId, MovementSync dto, Guid deviceId, DateTime now)
    {
        Guard.NotEmpty(dto.Id, "Id");
        Guard.NotEmpty(userId, "UserId");
        var movement = new Movement { Id = dto.Id, UserId = userId };
        movement.ApplySync(dto, deviceId, now);
        return movement;
    }

    public bool ApplySync(MovementSync dto, Guid deviceId, DateTime now)
    {
        var name = Guard.Text(dto.Name, 100, "Name");
        var keywords = NormalizeKeywords(dto.Keywords);
        if (!BeginSync(dto.ClientUpdateAt, dto.DeletedAt, deviceId, now)) return false;

        Name = name;
        Keywords = keywords;
        return true;
    }

    // Keywords feed search: trimmed, lower-cased, de-duplicated, blanks dropped. Null means none.
    private static List<string> NormalizeKeywords(string[]? keywords)
    {
        var result = (keywords ?? [])
            .Where(k => !string.IsNullOrWhiteSpace(k))
            .Select(k => Guard.Text(k, 50, "Keyword").ToLowerInvariant())
            .Distinct()
            .ToList();
        if (result.Count > MaxKeywords) throw new ArgumentException($"A movement cannot have more than {MaxKeywords} keywords.");
        return result;
    }
}

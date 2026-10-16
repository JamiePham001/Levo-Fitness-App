namespace backend.Entities.ProgramBlocks;

public class ProgramBlock : SyncEntity
{
    // Client-generated so blocks can be created offline.
    public Guid Id { get; private set; }
    public Guid UserId { get; private set; }
    // Parent program; fixed at creation.
    public Guid ProgramId { get; private set; }
    public string? Name { get; private set; }
    public int Order { get; private set; }

    public static ProgramBlock Create(Guid userId, BlockSync dto, Guid deviceId, DateTime now)
    {
        Guard.NotEmpty(dto.Id, "Id");
        Guard.NotEmpty(userId, "UserId");
        Guard.NotEmpty(dto.ProgramId, "ProgramId");
        var block = new ProgramBlock { Id = dto.Id, UserId = userId, ProgramId = dto.ProgramId };
        block.ApplySync(dto, deviceId, now);
        return block;
    }

    public bool ApplySync(BlockSync dto, Guid deviceId, DateTime now)
    {
        if (dto.ProgramId != ProgramId) throw new ArgumentException("A block cannot move to another program.");
        var name = Guard.OptionalText(dto.Name, 100, "Name");
        Guard.NonNegative(dto.Order, "Order");
        if (!BeginSync(dto.ClientUpdateAt, dto.DeletedAt, deviceId, now)) return false;

        Name = name;
        Order = dto.Order;
        return true;
    }
}

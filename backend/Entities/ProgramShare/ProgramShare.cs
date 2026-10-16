namespace backend.Entities.ProgramShares;

// Online-only: created through an ordinary endpoint, not synced. Revoking a share deletes the row.
public class ProgramShare
{
    public Guid Id { get; private set; }
    public Guid ProgramId { get; private set; }
    public Guid ReceiverId { get; private set; }
    public Guid SenderId { get; private set; }
    public ShareAccess Access { get; private set; } = ShareAccess.Download;
    public DateTime CreatedAt { get; private set; }

    public static ProgramShare Create(Guid programId, Guid receiverId, Guid senderId, ShareAccess access, DateTime now)
    {
        Guard.NotEmpty(programId, "ProgramId");
        Guard.NotEmpty(receiverId, "ReceiverId");
        Guard.NotEmpty(senderId, "SenderId");
        if (receiverId == senderId) throw new ArgumentException("A program cannot be shared with its sender.");
        Guard.Defined(access, "Access");

        return new ProgramShare
        {
            Id = Guid.NewGuid(),
            ProgramId = programId,
            ReceiverId = receiverId,
            SenderId = senderId,
            Access = access,
            CreatedAt = now
        };
    }
}

public enum ShareAccess { Download, Collab }

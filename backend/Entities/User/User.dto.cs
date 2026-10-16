namespace backend.Entities.Users;

public record UserSync(Guid Id, Gender Gender, double Weight, Measurement Measurement, DateTime ClientUpdateAt);

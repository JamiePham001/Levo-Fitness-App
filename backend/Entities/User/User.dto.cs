namespace backend.Entities.User;

public record UserRegularRegisterRequest(Guid id, string username, string email, Gender gender, double? weight);
public record UserOAuthRegisterRequest(Guid id, string username, string email, string avatarUrl, string provider, Gender gender, double? weight);

using backend.Entities.Users;
using static backend.Tests.TestData;

namespace backend.Tests;

public class UserTests
{
    private static UserSync Dto(Guid? id = null, Gender gender = Gender.Male, double weight = 80,
        Measurement measurement = Measurement.Metric, DateTime? clientUpdateAt = null)
        => new(id ?? UserId, gender, weight, measurement, clientUpdateAt ?? T0);

    private static User NewUser(UserSync? dto = null, string username = "sam", string email = "sam@example.com",
        string? avatarUrl = "https://img/sam.png", string provider = "google")
        => User.CreateOAuth(dto ?? Dto(), username, email, avatarUrl, provider, DeviceA, T0);

    [Fact]
    public void CreateOAuth_CombinesTokenValuesAndSnapshot()
    {
        var user = NewUser(Dto(gender: Gender.Female, weight: 62.5, measurement: Measurement.Imperial),
            username: "Sam", email: "sam@example.com", provider: " Apple ");

        Assert.Equal(UserId, user.Id);
        Assert.Equal("Sam", user.Username);
        Assert.Equal("sam@example.com", user.Email);
        Assert.Equal("apple", user.Provider);
        Assert.Equal(Gender.Female, user.Gender);
        Assert.Equal(62.5, user.Weight);
        Assert.Equal(Measurement.Imperial, user.Measurement);
        Assert.Equal(Role.Member, user.Role);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void MissingAvatar_IsAllowed(string? avatarUrl)
        => Assert.Null(NewUser(avatarUrl: avatarUrl).AvatarUrl);

    [Theory]
    [InlineData("facebook")]
    [InlineData("")]
    public void UnsupportedProvider_IsRejected(string provider)
        => Assert.Throws<ArgumentException>(() => NewUser(provider: provider));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void BlankUsernameOrEmail_IsRejected(string value)
    {
        Assert.Throws<ArgumentException>(() => NewUser(username: value));
        Assert.Throws<ArgumentException>(() => NewUser(email: value));
    }

    [Theory]
    [InlineData("sam")]
    [InlineData("Sam_99")]
    [InlineData("s")]
    [InlineData("sam.lee-1")]
    [InlineData("sam!@#$%^&*()+=~")]
    [InlineData("ünïcödé")]
    public void Username_AllowsLettersDigitsAndSpecialCharacters(string username)
        => Assert.Equal(username, NewUser(username: username).Username);

    [Theory]
    [InlineData("sam lee")]
    [InlineData(" sam")]
    [InlineData("sam ")]
    [InlineData("sam\tlee")]
    [InlineData("sam\nlee")]
    [InlineData("sam\u00A0lee")]
    [InlineData("sam\u0000lee")]
    public void Username_WithWhitespaceOrControlCharacters_IsRejected(string username)
        => Assert.Throws<ArgumentException>(() => NewUser(username: username));

    [Fact]
    public void UsernameOfExactlyThirtyCharacters_IsAccepted()
        => Assert.Equal(30, NewUser(username: new string('a', 30)).Username.Length);

    [Fact]
    public void OverlongUsername_IsRejected()
        => Assert.Throws<ArgumentException>(() => NewUser(username: new string('a', 31)));

    [Fact]
    public void EmptyId_IsRejected()
        => Assert.Throws<ArgumentException>(() => NewUser(Dto(id: Guid.Empty)));

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void InvalidWeight_IsRejected(double weight)
        => Assert.Throws<ArgumentException>(() => NewUser(Dto(weight: weight)));

    [Fact]
    public void UndefinedGenderOrMeasurement_IsRejected()
    {
        Assert.Throws<ArgumentException>(() => NewUser(Dto(gender: (Gender)9)));
        Assert.Throws<ArgumentException>(() => NewUser(Dto(measurement: (Measurement)9)));
    }

    [Fact]
    public void ApplySync_NewerSnapshot_UpdatesOnlyClientOwnedFields()
    {
        var user = NewUser(username: "sam", email: "sam@example.com");

        var applied = user.ApplySync(Dto(gender: Gender.Female, weight: 70, measurement: Measurement.Imperial, clientUpdateAt: T0.AddMinutes(1)),
            DeviceB, T0.AddMinutes(1));

        Assert.True(applied);
        Assert.Equal(Gender.Female, user.Gender);
        Assert.Equal(70, user.Weight);
        Assert.Equal(Measurement.Imperial, user.Measurement);
        Assert.Equal("sam", user.Username);
        Assert.Equal("sam@example.com", user.Email);
        Assert.Equal("google", user.Provider);
        Assert.Equal(Role.Member, user.Role);
        Assert.Equal(DeviceB, user.UpdatedByDeviceId);
    }

    [Fact]
    public void ApplySync_NeverTombstonesTheUser()
    {
        var user = NewUser();

        user.ApplySync(Dto(clientUpdateAt: T0.AddMinutes(1)), DeviceA, T0.AddMinutes(1));

        Assert.Null(user.DeletedAt);
    }

    [Fact]
    public void ApplySync_StaleSnapshot_ChangesNothing()
    {
        var user = NewUser(Dto(weight: 80));

        var applied = user.ApplySync(Dto(weight: 50, clientUpdateAt: T0), DeviceB, T0.AddMinutes(1));

        Assert.False(applied);
        Assert.Equal(80, user.Weight);
    }

    [Fact]
    public void UpdateRole_ChangesRoleAndRejectsUndefined()
    {
        var user = NewUser();

        user.UpdateRole(Role.Premium);

        Assert.Equal(Role.Premium, user.Role);
        Assert.Throws<ArgumentException>(() => user.UpdateRole((Role)9));
    }
}

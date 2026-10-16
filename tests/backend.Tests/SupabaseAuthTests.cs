using System.Security.Claims;
using System.Text;
using backend.Features.Auth;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace backend.Tests;

public class SupabaseAuthTests
{
    private const string Secret = "test-secret-with-more-than-thirty-two-characters!";
    private const string Issuer = "https://abcdefgh.supabase.co/auth/v1";

    private static readonly JsonWebTokenHandler Handler = new();

    private static JwtBearerOptions Options(string issuers = Issuer)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSupabaseAuth(Secret, issuers);
        using var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>().Get(JwtBearerDefaults.AuthenticationScheme);
    }

    private static string Token(string issuer = Issuer, string audience = "authenticated", DateTime? expires = null, string secret = Secret, string? sub = null)
        => Handler.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = issuer,
            Audience = audience,
            Subject = new ClaimsIdentity([new Claim("sub", sub ?? Guid.NewGuid().ToString())]),
            NotBefore = DateTime.UtcNow.AddMinutes(-1),
            Expires = expires ?? DateTime.UtcNow.AddHours(1),
            SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret)), SecurityAlgorithms.HmacSha256)
        });

    private static Task<TokenValidationResult> Validate(string token, JwtBearerOptions? options = null)
        => Handler.ValidateTokenAsync(token, (options ?? Options()).TokenValidationParameters);

    [Fact]
    public async Task ValidToken_IsAccepted()
        => Assert.True((await Validate(Token())).IsValid);

    [Fact]
    public async Task SubClaim_IsKeptAsSub()
    {
        var sub = Guid.NewGuid().ToString();

        var result = await Validate(Token(sub: sub));

        Assert.Equal(sub, result.ClaimsIdentity.FindFirst("sub")?.Value);
        Assert.False(Options().MapInboundClaims);
    }

    [Fact]
    public async Task TokenSignedWithAnotherSecret_IsRejected()
        => Assert.False((await Validate(Token(secret: "another-secret-with-more-than-thirty-two-chars"))).IsValid);

    [Fact]
    public async Task TokenFromAnotherIssuer_IsRejected()
        => Assert.False((await Validate(Token(issuer: "https://other.supabase.co/auth/v1"))).IsValid);

    [Theory]
    [InlineData("anon")]
    [InlineData("service_role")]
    public async Task TokenForAnotherAudience_IsRejected(string audience)
        => Assert.False((await Validate(Token(audience: audience))).IsValid);

    [Fact]
    public async Task ExpiredToken_IsRejectedImmediately()
        => Assert.False((await Validate(Token(expires: DateTime.UtcNow.AddSeconds(-2)))).IsValid);

    [Fact]
    public async Task UnsignedToken_IsRejected()
    {
        static string B64(string json) => Convert.ToBase64String(Encoding.UTF8.GetBytes(json)).TrimEnd('=');
        var exp = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds();
        var unsigned = $"{B64("{\"alg\":\"none\",\"typ\":\"JWT\"}")}.{B64($"{{\"sub\":\"x\",\"aud\":\"authenticated\",\"iss\":\"{Issuer}\",\"exp\":{exp}}}")}.";

        Assert.False((await Validate(unsigned)).IsValid);
    }

    [Fact]
    public async Task GarbageToken_IsRejected()
        => Assert.False((await Validate("not-a-jwt")).IsValid);

    [Fact]
    public async Task SeveralIssuers_AreAllAccepted()
    {
        var options = Options($"{Issuer}, https://second.supabase.co/auth/v1");

        Assert.True((await Validate(Token(), options)).IsValid);
        Assert.True((await Validate(Token(issuer: "https://second.supabase.co/auth/v1"), options)).IsValid);
        Assert.False((await Validate(Token(issuer: "https://third.supabase.co/auth/v1"), options)).IsValid);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" , ")]
    public void NoIssuer_FailsAtStartup(string issuers)
        => Assert.Throws<ArgumentException>(() => new ServiceCollection().AddSupabaseAuth(Secret, issuers));

    [Fact]
    public void RegistersBearerAsTheDefaultScheme()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSupabaseAuth(Secret, Issuer);
        using var provider = services.BuildServiceProvider();

        var options = provider.GetRequiredService<IOptions<Microsoft.AspNetCore.Authentication.AuthenticationOptions>>().Value;

        Assert.Equal(JwtBearerDefaults.AuthenticationScheme, options.DefaultAuthenticateScheme ?? options.DefaultScheme);
    }
}

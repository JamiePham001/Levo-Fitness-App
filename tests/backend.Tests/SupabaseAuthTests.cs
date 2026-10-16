using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using backend.Features.Auth;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace backend.Tests;

// Runs real requests through the authentication pipeline with an in-memory server. The ES256 key pair stands in
// for the project's key, served through the same IConfigurationManager hook the JWKS download uses in production.
public sealed class SupabaseAuthTests : IAsyncLifetime
{
    private const string Issuer = "https://abcdefgh.supabase.co/auth/v1";
    private static readonly JsonWebTokenHandler Handler = new();

    private readonly ECDsaSecurityKey _key = NewKey("test-key");
    private WebApplication _app = default!;
    private HttpClient _client = default!;

    public async Task InitializeAsync()
    {
        (_app, _client) = await StartAsync(Issuer, new StaticKeys(_key));
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _app.DisposeAsync();
    }

    private static ECDsaSecurityKey NewKey(string kid) => new(ECDsa.Create(ECCurve.NamedCurves.nistP256)) { KeyId = kid };

    private static async Task<(WebApplication, HttpClient)> StartAsync(string issuers, StaticKeys keys)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Services.AddSupabaseAuth(issuers, keys);

        var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapGet("/me", (ClaimsPrincipal user) => user.FindFirst("sub")?.Value).RequireAuthorization();
        app.MapGet("/open", () => "ok");
        await app.StartAsync();
        return (app, app.GetTestClient());
    }

    private string Token(string issuer = Issuer, string audience = "authenticated", DateTime? expires = null,
        SecurityKey? key = null, string algorithm = SecurityAlgorithms.EcdsaSha256, string? sub = null)
        => Handler.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = issuer,
            Audience = audience,
            Subject = new ClaimsIdentity([new Claim("sub", sub ?? Guid.NewGuid().ToString())]),
            NotBefore = DateTime.UtcNow.AddMinutes(-1),
            Expires = expires ?? DateTime.UtcNow.AddHours(1),
            SigningCredentials = new SigningCredentials(key ?? _key, algorithm)
        });

    private static Task<HttpResponseMessage> Get(HttpClient client, string path, string? token = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        if (token is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client.SendAsync(request);
    }

    private async Task AssertStatus(HttpStatusCode expected, string? token, HttpClient? client = null)
        => Assert.Equal(expected, (await Get(client ?? _client, "/me", token)).StatusCode);

    [Fact]
    public async Task ValidToken_IsAcceptedAndExposesTheUserIdAsSub()
    {
        var sub = Guid.NewGuid().ToString();

        var response = await Get(_client, "/me", Token(sub: sub));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(sub, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task UnprotectedEndpoints_StillWorkWithoutAToken()
        => Assert.Equal(HttpStatusCode.OK, (await Get(_client, "/open")).StatusCode);

    [Fact]
    public async Task MissingToken_IsUnauthorized()
        => await AssertStatus(HttpStatusCode.Unauthorized, null);

    [Fact]
    public async Task GarbageToken_IsUnauthorized()
        => await AssertStatus(HttpStatusCode.Unauthorized, "not-a-jwt");

    [Fact]
    public async Task TokenSignedWithAnotherKey_IsUnauthorized()
        => await AssertStatus(HttpStatusCode.Unauthorized, Token(key: NewKey("test-key")));

    [Fact]
    public async Task TokenFromAnotherIssuer_IsUnauthorized()
        => await AssertStatus(HttpStatusCode.Unauthorized, Token(issuer: "https://other.supabase.co/auth/v1"));

    [Theory]
    [InlineData("anon")]
    [InlineData("service_role")]
    public async Task TokenForAnotherAudience_IsUnauthorized(string audience)
        => await AssertStatus(HttpStatusCode.Unauthorized, Token(audience: audience));

    [Fact]
    public async Task ExpiredToken_IsUnauthorizedImmediately()
        => await AssertStatus(HttpStatusCode.Unauthorized, Token(expires: DateTime.UtcNow.AddSeconds(-2)));

    [Fact]
    public async Task UnsignedToken_IsUnauthorized()
    {
        static string B64(string json) => Convert.ToBase64String(Encoding.UTF8.GetBytes(json)).TrimEnd('=');
        var exp = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds();
        var unsigned = $"{B64("{\"alg\":\"none\",\"typ\":\"JWT\"}")}.{B64($"{{\"sub\":\"x\",\"aud\":\"authenticated\",\"iss\":\"{Issuer}\",\"exp\":{exp}}}")}.";

        await AssertStatus(HttpStatusCode.Unauthorized, unsigned);
    }

    [Fact]
    public async Task SymmetricTokenSignedWithThePublicKeyMaterial_IsUnauthorized()
    {
        // Algorithm-confusion attack: HS256 using the public key bytes as the secret.
        var publicKey = _key.ECDsa.ExportSubjectPublicKeyInfo();
        var forged = Token(key: new SymmetricSecurityKey(publicKey) { KeyId = "test-key" }, algorithm: SecurityAlgorithms.HmacSha256);

        await AssertStatus(HttpStatusCode.Unauthorized, forged);
    }

    [Fact]
    public async Task SeveralIssuers_AreAllAccepted()
    {
        var (app, client) = await StartAsync($"{Issuer}, https://second.supabase.co/auth/v1", new StaticKeys(_key));
        await using var _ = app;
        using var __ = client;

        await AssertStatus(HttpStatusCode.OK, Token(), client);
        await AssertStatus(HttpStatusCode.OK, Token(issuer: "https://second.supabase.co/auth/v1"), client);
        await AssertStatus(HttpStatusCode.Unauthorized, Token(issuer: "https://third.supabase.co/auth/v1"), client);
    }

    [Fact]
    public async Task RotatedKeys_AreAcceptedAlongsideTheOldOnes()
    {
        var newKey = NewKey("rotated-key");
        var (app, client) = await StartAsync(Issuer, new StaticKeys(_key, newKey));
        await using var _ = app;
        using var __ = client;

        await AssertStatus(HttpStatusCode.OK, Token(), client);
        await AssertStatus(HttpStatusCode.OK, Token(key: newKey), client);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" , ")]
    public void NoIssuer_FailsAtStartup(string issuers)
        => Assert.Throws<ArgumentException>(() => new ServiceCollection().AddSupabaseAuth(issuers, new StaticKeys(NewKey("k"))));

    private sealed class StaticKeys : IConfigurationManager<OpenIdConnectConfiguration>
    {
        private readonly OpenIdConnectConfiguration _configuration = new();

        public StaticKeys(params SecurityKey[] keys)
        {
            foreach (var key in keys) _configuration.SigningKeys.Add(key);
        }

        public Task<OpenIdConnectConfiguration> GetConfigurationAsync(CancellationToken cancel) => Task.FromResult(_configuration);

        public void RequestRefresh()
        {
        }
    }
}

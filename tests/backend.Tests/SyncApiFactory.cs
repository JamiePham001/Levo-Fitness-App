using System.Net.Http.Headers;
using System.Security.Claims;
using System.Security.Cryptography;
using backend.Database;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace backend.Tests;

// Starts the real application in memory. The Postgres database is replaced with the EF in-memory provider and the
// Supabase signing keys with a local ES256 key pair, so tests can mint tokens that the real auth pipeline accepts.
public sealed class SyncApiFactory : WebApplicationFactory<Program>
{
    public const string Issuer = "https://test.supabase.co/auth/v1";

    private static readonly JsonWebTokenHandler TokenHandler = new();

    private readonly ECDsaSecurityKey _key = new(ECDsa.Create(ECCurve.NamedCurves.nistP256)) { KeyId = "test-key" };
    private readonly string _databaseName = Guid.NewGuid().ToString();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Not Development, so the developer's user secrets are never loaded into a test run.
        builder.UseEnvironment("Testing");
        builder.UseSetting("Local:ConnectionString", "Host=unused");
        builder.UseSetting("SupaBase:ConnectionString", "Host=unused");
        builder.UseSetting("SupaBase:ValidIssuers", Issuer);

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<DbContextOptions<AppDbContext>>();
            services.RemoveAll<IDbContextOptionsConfiguration<AppDbContext>>();
            services.AddDbContext<AppDbContext>(options => options.UseInMemoryDatabase(_databaseName));

            services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme,
                options => options.ConfigurationManager = new StaticKeys(_key));
        });
    }

    public string Token(Guid userId, string audience = "authenticated")
        => TokenHandler.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = Issuer,
            Audience = audience,
            Subject = new ClaimsIdentity([new Claim("sub", userId.ToString())]),
            NotBefore = DateTime.UtcNow.AddMinutes(-1),
            Expires = DateTime.UtcNow.AddHours(1),
            SigningCredentials = new SigningCredentials(_key, SecurityAlgorithms.EcdsaSha256)
        });

    // A client that is signed in as the given user.
    public HttpClient ClientFor(Guid userId)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token(userId));
        return client;
    }

    // Reads or seeds the in-memory database directly.
    public async Task<T> WithDb<T>(Func<AppDbContext, Task<T>> action)
    {
        await using var scope = Services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    public Task Seed(Action<AppDbContext> add) => WithDb(async db =>
    {
        add(db);
        await db.SaveChangesAsync();
        return 0;
    });

    private sealed class StaticKeys(SecurityKey key) : IConfigurationManager<OpenIdConnectConfiguration>
    {
        private readonly OpenIdConnectConfiguration _configuration = new() { SigningKeys = { key } };

        public Task<OpenIdConnectConfiguration> GetConfigurationAsync(CancellationToken cancel) => Task.FromResult(_configuration);

        public void RequestRefresh()
        {
        }
    }
}

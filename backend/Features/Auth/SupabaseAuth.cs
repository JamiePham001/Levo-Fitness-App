using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace backend.Features.Auth;

public static class SupabaseAuth
{
    // Validates Supabase access tokens signed with the project's asymmetric key (ES256). The public keys are
    // downloaded from the project's JWKS endpoint, cached, and refreshed when a token names an unknown key,
    // so key rotation needs no config change. Tests pass their own key source instead of the network.
    public static IServiceCollection AddSupabaseAuth(this IServiceCollection services, string issuers,
        IConfigurationManager<OpenIdConnectConfiguration>? signingKeys = null)
    {
        var validIssuers = ParseIssuers(issuers);
        if (validIssuers.Length == 0) throw new ArgumentException("At least one issuer is required.", nameof(issuers));

        signingKeys ??= new ConfigurationManager<OpenIdConnectConfiguration>(
            validIssuers[0] + JwksPath,
            new JwksRetriever(),
            new HttpDocumentRetriever { RequireHttps = true })
        {
            AutomaticRefreshInterval = TimeSpan.FromHours(1)
        };

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(o =>
        {
            o.MapInboundClaims = false; // keep the raw "sub" claim instead of remapping it
            o.ConfigurationManager = signingKeys;
            o.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true,
                ValidAlgorithms = [SecurityAlgorithms.EcdsaSha256],
                ValidateIssuer = true,
                ValidIssuers = validIssuers,
                ValidateAudience = true,
                ValidAudience = "authenticated",
                ValidateLifetime = true,
                ClockSkew = TimeSpan.Zero
            };
        });
        services.AddAuthorization();
        return services;
    }

    private const string JwksPath = "/.well-known/jwks.json";

    // The setting holds one or more comma-separated issuers. It may also be given as the full JWKS address
    // (https://<project>.supabase.co/auth/v1/.well-known/jwks.json): the issuer in a token is the part before
    // /.well-known, so that suffix and any trailing slash are dropped.
    public static string[] ParseIssuers(string issuers)
        => [.. issuers
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(issuer => issuer.EndsWith(JwksPath, StringComparison.OrdinalIgnoreCase) ? issuer[..^JwksPath.Length] : issuer)
            .Select(issuer => issuer.TrimEnd('/'))
            .Where(issuer => issuer.Length > 0)];

    // A JWKS document is not an OpenID configuration, so wrap its keys in one for the JWT bearer handler.
    private sealed class JwksRetriever : IConfigurationRetriever<OpenIdConnectConfiguration>
    {
        public async Task<OpenIdConnectConfiguration> GetConfigurationAsync(string address, IDocumentRetriever retriever, CancellationToken cancel)
        {
            var keySet = new JsonWebKeySet(await retriever.GetDocumentAsync(address, cancel));
            var configuration = new OpenIdConnectConfiguration { JsonWebKeySet = keySet };
            foreach (var key in keySet.GetSigningKeys()) configuration.SigningKeys.Add(key);
            return configuration;
        }
    }
}

using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

namespace backend.Features.Auth;

public static class SupabaseAuth
{
    // Validates Supabase access tokens signed with the project's shared JWT secret (HS256).
    public static IServiceCollection AddSupabaseAuth(this IServiceCollection services, string signatureKey, string issuers)
    {
        var validIssuers = issuers.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (validIssuers.Length == 0) throw new ArgumentException("At least one issuer is required.", nameof(issuers));

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(o =>
        {
            o.MapInboundClaims = false; // keep the raw "sub" claim instead of remapping it
            o.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signatureKey)),
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
}

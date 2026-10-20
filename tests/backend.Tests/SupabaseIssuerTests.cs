using backend.Features.Auth;

namespace backend.Tests;

public class SupabaseIssuerTests
{
    private const string Issuer = "https://abcdefgh.supabase.co/auth/v1";

    [Fact]
    public void PlainIssuer_IsKept()
        => Assert.Equal([Issuer], SupabaseAuth.ParseIssuers(Issuer));

    [Fact]
    public void FullJwksAddress_IsReducedToTheIssuerTokensCarry()
        => Assert.Equal([Issuer], SupabaseAuth.ParseIssuers(Issuer + "/.well-known/jwks.json"));

    [Theory]
    [InlineData("/")]
    [InlineData("/.well-known/jwks.json")]
    [InlineData("/.WELL-KNOWN/JWKS.JSON")]
    [InlineData("//")]
    public void TrailingSlashAndJwksSuffix_AreDropped(string suffix)
        => Assert.Equal([Issuer], SupabaseAuth.ParseIssuers(Issuer + suffix));

    [Fact]
    public void SeveralIssuers_AreSplitTrimmedAndNormalisedIndividually()
        => Assert.Equal(
            [Issuer, "https://second.supabase.co/auth/v1"],
            SupabaseAuth.ParseIssuers($" {Issuer}/.well-known/jwks.json , https://second.supabase.co/auth/v1/ "));

    [Theory]
    [InlineData("")]
    [InlineData(" , ")]
    [InlineData("/")]
    public void BlankSettings_GiveNoIssuers(string value)
        => Assert.Empty(SupabaseAuth.ParseIssuers(value));
}

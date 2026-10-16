using backend.Database;
using Npgsql;

namespace backend.Tests;

public class PostgresConnectionStringTests
{
    [Fact]
    public void KeywordStyleString_PassesThroughUnchanged()
    {
        const string value = "Host=localhost;Port=5432;Database=levo;Username=postgres;Password=secret";

        Assert.Equal(value, PostgresConnectionString.Normalize(value));
    }

    [Theory]
    [InlineData("postgresql")]
    [InlineData("postgres")]
    [InlineData("POSTGRESQL")]
    public void Uri_IsConvertedToNpgsqlKeywords(string scheme)
    {
        var result = new NpgsqlConnectionStringBuilder(
            PostgresConnectionString.Normalize($"{scheme}://postgres.abcdefgh:s3cret@aws-0-eu.pooler.supabase.com:6543/postgres"));

        Assert.Equal("aws-0-eu.pooler.supabase.com", result.Host);
        Assert.Equal(6543, result.Port);
        Assert.Equal("postgres.abcdefgh", result.Username);
        Assert.Equal("s3cret", result.Password);
        Assert.Equal("postgres", result.Database);
        Assert.Equal(SslMode.Require, result.SslMode);
    }

    [Fact]
    public void Uri_PercentEncodedPassword_IsDecoded()
    {
        var result = new NpgsqlConnectionStringBuilder(
            PostgresConnectionString.Normalize("postgresql://postgres:p%40ss%3Aw%2Frd@db.abcdefgh.supabase.co:5432/postgres"));

        Assert.Equal("p@ss:w/rd", result.Password);
    }

    [Fact]
    public void Uri_WithoutPort_UsesTheDefaultPort()
    {
        var result = new NpgsqlConnectionStringBuilder(
            PostgresConnectionString.Normalize("postgresql://postgres:pw@db.abcdefgh.supabase.co/postgres"));

        Assert.Equal(5432, result.Port);
    }

    [Fact]
    public void Uri_QueryParametersAreIgnored()
    {
        var result = new NpgsqlConnectionStringBuilder(
            PostgresConnectionString.Normalize("postgresql://postgres:pw@db.abcdefgh.supabase.co:5432/postgres?sslmode=require"));

        Assert.Equal("postgres", result.Database);
    }
}

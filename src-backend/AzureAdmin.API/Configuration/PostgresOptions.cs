namespace AzureAdmin.API.Configuration;

public sealed class PostgresOptions
{
    public const string SectionName = "Postgres";

    public required string Host { get; init; }
    public int Port { get; init; } = 5432;
    public required string Database { get; init; }
    public required string Username { get; init; }
    public required string Password { get; init; }

    /// <summary>
    /// Npgsql SSL Mode (Disable, Allow, Prefer, Require, VerifyCA, VerifyFull). Left empty, Npgsql's
    /// default (Prefer) applies. Azure Database for PostgreSQL requires TLS: use Require or stricter.
    /// </summary>
    public string? SslMode { get; init; }

    public string ToConnectionString()
    {
        var connectionString = $"Host={Host};Port={Port};Database={Database};Username={Username};Password={Password}";
        return string.IsNullOrWhiteSpace(SslMode)
            ? connectionString
            : $"{connectionString};SSL Mode={SslMode}";
    }
}

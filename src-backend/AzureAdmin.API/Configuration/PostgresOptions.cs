namespace AzureAdmin.API.Configuration;

public sealed class PostgresOptions
{
    public const string SectionName = "Postgres";

    public required string Host { get; init; }
    public int Port { get; init; } = 5432;
    public required string Database { get; init; }
    public required string Username { get; init; }
    public required string Password { get; init; }

    public string ToConnectionString() =>
        $"Host={Host};Port={Port};Database={Database};Username={Username};Password={Password}";
}

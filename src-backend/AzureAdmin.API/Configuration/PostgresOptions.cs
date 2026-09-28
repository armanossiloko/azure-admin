using System.ComponentModel.DataAnnotations;
using Npgsql;

namespace AzureAdmin.API.Configuration;

public sealed class PostgresOptions
{
    public const string SectionName = "Postgres";

    [Required]
    public required string Host { get; init; }

    [Range(1, 65535)]
    public int Port { get; init; } = 5432;

    [Required]
    public required string Database { get; init; }

    [Required]
    public required string Username { get; init; }

    [Required]
    public required string Password { get; init; }

    /// <summary>
    /// TLS mode of the connection. Encrypted by default; set to Disable/Prefer only for a local database
    /// without TLS. Require encrypts without validating the server certificate, VerifyCA/VerifyFull do.
    /// </summary>
    public SslMode SslMode { get; init; } = SslMode.Require;

    public string ToConnectionString() =>
        new NpgsqlConnectionStringBuilder
        {
            Host = Host,
            Port = Port,
            Database = Database,
            Username = Username,
            Password = Password,
            SslMode = SslMode,
        }.ConnectionString;
}

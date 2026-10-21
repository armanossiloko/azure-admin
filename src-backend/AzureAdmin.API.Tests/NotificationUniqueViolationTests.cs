using AzureAdmin.API.Services.Notifications;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace AzureAdmin.API.Tests;

public sealed class NotificationUniqueViolationTests
{
    [Fact]
    public void Unique_violation_from_postgres_is_recognised()
    {
        var ex = new DbUpdateException("insert failed", new PostgresException("duplicate key", "ERROR", "ERROR", PostgresErrorCodes.UniqueViolation));

        Assert.True(NotificationService.IsUniqueViolation(ex));
    }

    [Fact]
    public void Other_database_errors_are_not_swallowed()
    {
        var ex = new DbUpdateException("insert failed", new PostgresException("fk", "ERROR", "ERROR", PostgresErrorCodes.ForeignKeyViolation));

        Assert.False(NotificationService.IsUniqueViolation(ex));
    }
}

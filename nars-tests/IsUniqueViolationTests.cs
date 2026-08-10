using Microsoft.EntityFrameworkCore;
using NarsApi.Services;
using Npgsql;
using Xunit;

namespace NarsApi.Tests;

/// <summary>
/// Unit tests for <see cref="UserProfileService.IsUniqueViolation"/> — the
/// PostgreSQL constraint matching behind the duplicate-username/email mapping
/// in <see cref="UserProfileService.UpdateCredentialsAsync"/>. The public
/// method's pre-check makes the DbUpdateException path unreachable without
/// concurrent writers, so the matcher is tested directly.
/// </summary>
public class IsUniqueViolationTests
{
    private static DbUpdateException DbExceptionWith(Exception inner) =>
        new("An error occurred while saving changes.", inner);

    private static PostgresException PostgresError(string sqlState, string? constraintName) =>
        new(
            "duplicate key value violates unique constraint",
            "ERROR", "ERROR", sqlState,
            "Key already exists.", null,
            0, 0, null, null,
            "public", "users", null, "text",
            constraintName, null, "0", "nars");

    [Fact]
    public void PostgresUniqueViolation_EmailConstraint_IsDetected()
    {
        var ex = DbExceptionWith(PostgresError("23505", "IX_users_email"));

        var isUnique = UserProfileService.IsUniqueViolation(ex, out var constraint);

        Assert.True(isUnique);
        Assert.Equal("IX_users_email", constraint);
    }

    [Fact]
    public void PostgresUniqueViolation_UsernameConstraint_IsDetected()
    {
        var ex = DbExceptionWith(PostgresError("23505", "IX_users_username"));

        var isUnique = UserProfileService.IsUniqueViolation(ex, out var constraint);

        Assert.True(isUnique);
        Assert.Equal("IX_users_username", constraint);
    }

    [Fact]
    public void NestedUniqueViolation_IsDetected()
    {
        // Npgsql wraps the PostgresException one level deeper inside the
        // DbUpdateException chain; the walker must still find it.
        var ex = DbExceptionWith(new InvalidOperationException("inner", PostgresError("23505", "IX_users_email")));

        var isUnique = UserProfileService.IsUniqueViolation(ex, out _);

        Assert.True(isUnique);
    }

    [Fact]
    public void NonUniqueDatabaseFailure_IsNotMatched()
    {
        var ex = DbExceptionWith(PostgresError("57014", null)); // statement_canceled

        var isUnique = UserProfileService.IsUniqueViolation(ex, out _);

        Assert.False(isUnique);
    }

    [Fact]
    public void NonPostgresException_IsNotMatched()
    {
        var isUnique = UserProfileService.IsUniqueViolation(
            DbExceptionWith(new InvalidOperationException("boom")), out _);

        Assert.False(isUnique);
    }
}

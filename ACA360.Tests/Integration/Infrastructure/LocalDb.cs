using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

namespace ACA360.Tests.Integration.Infrastructure;

/// <summary>
/// The developer's local ACA360 database, read from the same place the app reads it (the web
/// project's user secrets, or ConnectionStrings__DefaultConnection). Integration tests run only
/// when it is a db_ACA360 on this machine and can be reached; anywhere else they are skipped,
/// so they can never touch a shared, BETA or production database.
/// </summary>
public static class LocalDb
{
    public const string DatabaseName = "db_ACA360";

    private static readonly Lazy<(string? ConnectionString, string? SkipReason)> State = new(Probe);

    public static string ConnectionString => State.Value.ConnectionString
        ?? throw new InvalidOperationException(State.Value.SkipReason);

    public static string? SkipReason => State.Value.SkipReason;

    public static SqlConnection Open()
    {
        var connection = new SqlConnection(ConnectionString);
        connection.Open();
        return connection;
    }

    public static T Scalar<T>(string sql, object? args = null)
    {
        using var db = Open();
        return db.ExecuteScalar<T>(sql, args)!;
    }

    public static int Execute(string sql, object? args = null)
    {
        using var db = Open();
        return db.Execute(sql, args);
    }

    private static (string?, string?) Probe()
    {
        var configured = new ConfigurationBuilder()
            .AddUserSecrets(typeof(Program).Assembly, optional: true)
            .AddEnvironmentVariables()
            .Build()
            .GetConnectionString("DefaultConnection");
        if (string.IsNullOrWhiteSpace(configured))
            return (null, "Integration test skipped: no ConnectionStrings:DefaultConnection in the web project's user secrets.");

        SqlConnectionStringBuilder builder;
        try { builder = new SqlConnectionStringBuilder(configured); }
        catch (Exception ex) { return (null, "Integration test skipped: unreadable connection string (" + ex.Message + ")."); }

        if (!string.Equals(builder.InitialCatalog, DatabaseName, StringComparison.OrdinalIgnoreCase))
            return (null, $"Integration test skipped: the database is '{builder.InitialCatalog}', not {DatabaseName}.");

        var server = builder.DataSource.Split('\\', ',')[0].Trim();
        if (server.StartsWith("tcp:", StringComparison.OrdinalIgnoreCase)) server = server[4..];
        var local = new[] { ".", "(local)", "localhost", "127.0.0.1", "::1", "(localdb)", Environment.MachineName };
        if (!local.Contains(server, StringComparer.OrdinalIgnoreCase))
            return (null, $"Integration test skipped: server '{server}' is not this machine. These tests only run against a local database.");

        builder.ConnectTimeout = 5;
        try
        {
            using var connection = new SqlConnection(builder.ConnectionString);
            connection.Open();
        }
        catch (Exception ex)
        {
            return (null, "Integration test skipped: the local database cannot be reached (" + ex.Message + ").");
        }
        return (configured, null);
    }
}

/// <summary>A [Fact] that is skipped (not failed) when the local database is not available.</summary>
public sealed class LocalDbFactAttribute : FactAttribute
{
    public LocalDbFactAttribute()
    {
        if (LocalDb.SkipReason != null) Skip = LocalDb.SkipReason;
    }
}

/// <summary>A [Theory] that is skipped (not failed) when the local database is not available.</summary>
public sealed class LocalDbTheoryAttribute : TheoryAttribute
{
    public LocalDbTheoryAttribute()
    {
        if (LocalDb.SkipReason != null) Skip = LocalDb.SkipReason;
    }
}

using System.Security.Cryptography;
using Dapper;

namespace ACA360.Tests.Integration.Infrastructure;

/// <summary>Role ids in dbo.tbl_Roles.</summary>
public static class RoleIds
{
    public const int SuperAdmin = 2;
    public const int Admin = 3;
    public const int AcaDirector = 4;
    public const int DaSupervisor = 5;
    public const int AmSupervisor = 6;
    public const int Broker = 10;
    public const int DataAnalyst = 11;
    public const int Employer = 13;
    public const int StaffAC = 14;
    public const int StaffAU = 15;
    public const int AccountManager = 16;
}

public sealed record TestUser(long UserId, long RefId, string Email, string Password, int RoleId);

/// <summary>
/// Creates throw-away staff logins in the local database, each with a random password made up
/// on the spot (nothing is stored in the repository), and removes them again. Every login the
/// tests make has an e-mail starting with <see cref="Prefix"/>, so a run that crashed half-way is
/// cleaned up by the next one.
/// </summary>
public static class TestUsers
{
    public const string Prefix = "aca360.tests.";

    public static TestUser CreateStaff(int roleId, string label)
    {
        var email = $"{Prefix}{label}.{Guid.NewGuid().ToString("N")[..12]}@example.com";
        var password = "T1!" + Convert.ToBase64String(RandomNumberGenerator.GetBytes(18));
        var hash = BCrypt.Net.BCrypt.HashPassword(password);

        using var db = LocalDb.Open();
        using var tx = db.BeginTransaction();
        var staffId = db.ExecuteScalar<long>(
            @"INSERT INTO dbo.StaffAccounts (Staff_FName, Staff_LName, Staff_Email, Staff_Status, createdon)
              OUTPUT inserted.Staff_ID VALUES ('Test', @label, @email, 1, SYSDATETIME());",
            new { label = label.ToUpperInvariant(), email }, tx);
        var userId = db.ExecuteScalar<long>(
            @"INSERT INTO dbo.tbl_User (User_Name, User_Password, Role_ID, Flag, Ref_ID, isMFA, CreatedOn)
              OUTPUT inserted.User_ID VALUES (@email, @hash, @roleId, 1, @staffId, 0, GETDATE());",
            new { email, hash, roleId, staffId }, tx);
        tx.Commit();
        return new TestUser(userId, staffId, email, password, roleId);
    }

    private static (string Email, string Password, string Hash) NewCredentials(string label)
    {
        var email = $"{Prefix}{label}.{Guid.NewGuid().ToString("N")[..12]}@example.com";
        var password = "T1!" + Convert.ToBase64String(RandomNumberGenerator.GetBytes(18));
        return (email, password, BCrypt.Net.BCrypt.HashPassword(password));
    }

    /// <summary>An Employer login as Tracker > Employer > Portal login makes it: Ref_ID is the employer.</summary>
    public static TestUser CreateEmployer(long employerId, string label)
    {
        var (email, password, hash) = NewCredentials(label);
        var userId = LocalDb.Scalar<long>(
            @"INSERT INTO dbo.tbl_User (User_Name, User_Password, Role_ID, Flag, Ref_ID, isMFA, CreatedOn)
              OUTPUT inserted.User_ID VALUES (@email, @hash, 13, 1, @employerId, 0, GETDATE());",
            new { email, hash, employerId });
        return new TestUser(userId, employerId, email, password, RoleIds.Employer);
    }

    /// <summary>A Broker login with its BrokerAccounts record (Ref_ID = Broker_ID).</summary>
    public static TestUser CreateBroker(string label)
    {
        var (email, password, hash) = NewCredentials(label);
        using var db = LocalDb.Open();
        using var tx = db.BeginTransaction();
        var brokerId = db.ExecuteScalar<long>(
            @"INSERT INTO dbo.BrokerAccounts (Broker_FName, Broker_LName, Broker_Email, Broker_Status, createdon)
              OUTPUT inserted.Broker_ID VALUES ('Test', @label, @email, 1, SYSDATETIME());",
            new { label = label.ToUpperInvariant(), email }, tx);
        var userId = db.ExecuteScalar<long>(
            @"INSERT INTO dbo.tbl_User (User_Name, User_Password, Role_ID, Flag, Ref_ID, isMFA, CreatedOn)
              OUTPUT inserted.User_ID VALUES (@email, @hash, 10, 1, @brokerId, 0, GETDATE());",
            new { email, hash, brokerId }, tx);
        tx.Commit();
        return new TestUser(userId, brokerId, email, password, RoleIds.Broker);
    }

    /// <summary>Puts an employer in a broker's book (tbl_Employer_Broker_Assignment).</summary>
    public static void AssignToBroker(TestUser broker, long employerId) =>
        LocalDb.Execute(
            "INSERT INTO dbo.tbl_Employer_Broker_Assignment (EmployerID, Broker_UserID, AuthorizedBy_AM_ID, IsActive) VALUES (@e, @u, @u, 1)",
            new { e = employerId, u = broker.UserId });

    /// <summary>Gives an Account Manager (portfolio) or Data Analyst (operational) one employer.</summary>
    public static void Assign(TestUser user, int employerId)
    {
        if (user.RoleId == RoleIds.DataAnalyst)
            LocalDb.Execute(
                "INSERT INTO dbo.tbl_Operational_Assignment (DA_UserID, EmployerID, AuthorizedBy_AM_ID, IsActive) VALUES (@u, @e, @u, 1)",
                new { u = user.UserId, e = employerId });
        else
            LocalDb.Execute(
                "INSERT INTO dbo.tbl_Portfolio_Assignment (AM_UserID, EmployerID, AssignedBy_AdminID, IsActive) VALUES (@u, @e, @u, 1)",
                new { u = user.UserId, e = employerId });
    }

    /// <summary>Removes every test login (and what signing in left behind) from the local database.</summary>
    public static void RemoveAll()
    {
        if (LocalDb.SkipReason != null) return;
        using var db = LocalDb.Open();
        db.Execute(@"
            DECLARE @u TABLE (User_ID BIGINT, Ref_ID BIGINT);
            INSERT @u SELECT User_ID, Ref_ID FROM dbo.tbl_User WHERE [User_Name] LIKE @pattern;
            DELETE FROM dbo.tbl_Portfolio_Assignment   WHERE AM_UserID IN (SELECT User_ID FROM @u);
            DELETE FROM dbo.tbl_Operational_Assignment WHERE DA_UserID IN (SELECT User_ID FROM @u);
            DELETE FROM dbo.tbl_Employer_Broker_Assignment WHERE Broker_UserID IN (SELECT User_ID FROM @u);
            DELETE FROM dbo.tbl_AuthSessions           WHERE Subject IN (SELECT CONCAT(N'user:', User_ID) FROM @u);
            DELETE FROM dbo.UserPreferences            WHERE UserId IN (SELECT CAST(User_ID AS NVARCHAR(50)) FROM @u);
            DELETE FROM dbo.UserDashboardLayout        WHERE UserId IN (SELECT User_ID FROM @u);
            DELETE FROM dbo.tbl_UserShortcuts          WHERE UserId IN (SELECT User_ID FROM @u);
            DELETE FROM dbo.tbl_CalendarEvents         WHERE UserId IN (SELECT User_ID FROM @u);
            DELETE FROM dbo.tbl_UserNotifications      WHERE UserId IN (SELECT User_ID FROM @u);
            IF OBJECT_ID('dbo.NotificationPreferences') IS NOT NULL DELETE FROM dbo.NotificationPreferences WHERE UserId IN (SELECT User_ID FROM @u);
            DELETE FROM dbo.UserUIPreferences          WHERE UserId IN (SELECT User_ID FROM @u);
            DELETE FROM dbo.tbl_User                   WHERE User_ID IN (SELECT User_ID FROM @u);
            DELETE FROM dbo.StaffAccounts              WHERE Staff_Email LIKE @pattern;
            DELETE FROM dbo.BrokerAccounts             WHERE Broker_Email LIKE @pattern;",
            new { pattern = Prefix + "%@example.com" });
    }
}

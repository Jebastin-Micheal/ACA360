using System.Security.Cryptography;
using Dapper;

namespace ACA360.Tests.Integration.Infrastructure;

public sealed record TestEmployee(int Id, int EmployerId, int Year, string FirstName, string LastName, string Ssn, string Email)
{
    public string Last4 => Ssn[^4..];
}

/// <summary>
/// Throw-away employees in the local database: one row in Employee (last name
/// <see cref="LastName"/>) under a real employer, plus 1095-C codes for one year so a form
/// can be produced. SSNs start with 9, which the SSA never issues. Removed with everything
/// the portal, consent, e-mail and code screens wrote for them.
/// </summary>
public static class TestEmployees
{
    public const string LastName = "ACA360TESTS";

    public static TestEmployee Create(int employerId, int year, string firstName = "Test")
    {
        var digits = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");
        var ssn = $"9{digits[..2]}-{RandomNumberGenerator.GetInt32(10, 100)}-{digits[2..]}";
        var email = $"{TestUsers.Prefix}employee.{Guid.NewGuid().ToString("N")[..12]}@example.com";

        using var db = LocalDb.Open();
        using var tx = db.BeginTransaction();
        var id = db.ExecuteScalar<int>(
            @"INSERT INTO dbo.Employee (fullName, firstName, lastName, ssn, birthday, email, address, city, state, zip,
                                        EmployerId, Status, HireDate, CoverageElected)
              OUTPUT inserted.id
              VALUES (@full, @firstName, @lastName, @ssn, '1980-04-15', @email, '1 Test Street', 'Testville', 'NJ', '07001',
                      @employerId, 1, '2020-01-01', 1);",
            new { full = $"{firstName} {LastName}", firstName, lastName = LastName, ssn, email, employerId }, tx);
        db.Execute(
            @"INSERT INTO dbo.EmployeeCode (employeeId, employerId, filingYear, getForm, ALLM_COC, ALLM_LCMP, ALLM_SHC, PlanStartMonth, IsLocked)
              VALUES (@id, @employerId, @year, 1, '1E', 95.50, '2C', 1, 0);",
            new { id, employerId, year }, tx);
        tx.Commit();
        return new TestEmployee(id, employerId, year, firstName, LastName, ssn, email);
    }

    /// <summary>Removes every test employee and what was written for them.</summary>
    public static void RemoveAll()
    {
        if (LocalDb.SkipReason != null) return;
        using var db = LocalDb.Open();
        db.Execute(@"
            DECLARE @e TABLE (id INT PRIMARY KEY);
            INSERT @e SELECT id FROM dbo.Employee WHERE lastName = @lastName;
            DELETE FROM dbo.EmployeeConsentEvent      WHERE EmployeeConsentId IN (SELECT Id FROM dbo.EmployeeConsent WHERE EmployeeId IN (SELECT id FROM @e));
            DELETE FROM dbo.EmployeeConsent           WHERE EmployeeId IN (SELECT id FROM @e);
            DELETE FROM dbo.EmployeeFormNotifyRequest WHERE EmployeeId IN (SELECT id FROM @e);
            DELETE FROM dbo.EmployeePortalUser        WHERE EmployeeId IN (SELECT id FROM @e);
            DELETE FROM dbo.SecureDownloadLinks       WHERE EmployeeId IN (SELECT id FROM @e);
            DELETE FROM dbo.EmployeeCodeAuditTrail    WHERE EmployeeId IN (SELECT id FROM @e);
            IF OBJECT_ID('dbo.EmployeeChangeHistory') IS NOT NULL DELETE FROM dbo.EmployeeChangeHistory WHERE EmployeeId IN (SELECT id FROM @e);
            DELETE FROM dbo.AuditLog                  WHERE employeeId IN (SELECT id FROM @e);
            DELETE FROM dbo.CommunicationLog          WHERE EntityType = 'Employee' AND RelatedEntityId IN (SELECT id FROM @e);
            IF OBJECT_ID('dbo.EmployeeCodeMonthDetail') IS NOT NULL DELETE FROM dbo.EmployeeCodeMonthDetail WHERE EmployeeCodeId IN (SELECT id FROM dbo.EmployeeCode WHERE employeeId IN (SELECT id FROM @e));
            DELETE FROM dbo.EmployeeCode              WHERE employeeId IN (SELECT id FROM @e);
            DELETE FROM dbo.Employee                  WHERE id IN (SELECT id FROM @e);",
            new { lastName = LastName });
    }
}

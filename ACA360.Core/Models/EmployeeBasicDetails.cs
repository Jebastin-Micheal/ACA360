using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.ComponentModel.DataAnnotations.Schema;

namespace ACA360.Core.Models
{
    public class EmployeeBasicDetails
    {
        public string? EmployerId { get; set; }
        public string? EmployerName { get; set; }
        public string? FilingYear { get; set; }
        public string? EmployeeID { get; set; }
        public string? FirstName { get; set; }
        public string? MiddleName { get; set; }
        public string? LastName { get; set; }
        public string? Suffix { get; set; }
        public string? SSN { get; set; }
        public DateTime? Birthday { get; set; }
        public string? Email { get; set; }
        public string? Address { get; set; }
        public string? Address2 { get; set; }
        public string? City { get; set; }
        public string? State { get; set; }
        public int? StateId { get; set; }
        public string? Zip { get; set; }
        public string? Country { get; set; }
        public int? CountryId { get; set; }
        public short? IsForeign { get; set; }
        public short? IsExPatriot { get; set; }
        public short? getForm { get; set; }
        public short? IsCorrected { get; set; }
        public short? ConsentedToElectronic { get; set; }

        public List<string>? EditedFields { get; set; }

        public EmployeeCode? MonthlyCodes { get; set; } = new EmployeeCode();

        public List<CoveredIndividualModel>? CoveredIndividuals { get; set; } = new List<CoveredIndividualModel>();
        public List<CoveredIndividualModel>? CoveredIndividualCodes { get; set; } = new List<CoveredIndividualModel>();
        public EmployeeHireDetails? employeeHire { get; set; } = new EmployeeHireDetails();
        public List<EmployeeEnrollmentInfo>? EmployeeEnrollment { get; set; } = new List<EmployeeEnrollmentInfo>();
        public EmployeePayrollInfo? employeePayroll { get; set; } = new EmployeePayrollInfo();
        public List<Flag>? Flag { get; set; } = new List<Flag>();
        public List<AuditLog>? AuditLog { get; set; } = new List<AuditLog>();
        public List<EmployeeHireDetails> EmployeeHireDetail { get; set; } = new List<EmployeeHireDetails>();
        public List<EmployeeStatus> EmployeeStatus { get; set; } = new List<EmployeeStatus>();
        public List<EmployeePayroll> EmployeePayrollDetails { get; set; } = new List<EmployeePayroll>();


    }
    public class drp_Country
    {
        public string? Country_ID { get; set; }
        public string? Country_Name { get; set; }

    }
    public class drp_Plan_Type
    {
        public string? Plan_Type_ID { get; set; }
        public string? Plan_Type_Name { get; set; }

    }
    public class drp_Employer
    {
        public string? Employer_ID { get; set; }
        public string? Employer_Name { get; set; }

    }
    public class EmployeeHireDetails
    {
        [Column("id")]
        public int? Id { get; set; }

        [Column("employeeCodeId")]
        public int? EmployeeCodeId { get; set; }

        [Column("startDate")]
        public DateTime? Hire_StartDate { get; set; }

        [Column("endDate")]
        public DateTime? Hire_EndDate { get; set; }
    }

    public class EmployeeEnrollmentInfo
    {
        [Column("id")]
        public int? Id { get; set; }

        [Column("employeeCodeId")]
        public int? EmployeeCodeId { get; set; }

        [Column("planId")]
        public int? planId { get; set; }

        // planName doesn't exist in the SQL table, so we leave it unmapped!
        [NotMapped]
        public string? planName { get; set; }

        [Column("coverageOfferDate")]
        public DateTime? CoverageOfferDate { get; set; }

        [Column("unionMember")]
        public int? IsUnionMember { get; set; }

        [Column("contributionStartDate")]
        public DateTime? Union_ContributionStartDate { get; set; }

        [Column("contributionEndDate")]
        public DateTime? Union_ContributionEndDate { get; set; }

        [Column("isEnrolled")]
        public int? IsMedicalEnrolled { get; set; }

        [Column("coverageStartDate")]
        public DateTime? Medical_CoverageStartDate { get; set; }

        [Column("coverageEndDate")]
        public DateTime? Medical_CoverageEndDate { get; set; }

        [Column("COBRAEnrolled")]
        public int? IsCOBRAEnrolled { get; set; }

        [Column("COBRAStartDate")]
        public DateTime? COBRA_StartDate { get; set; }

        [Column("COBRAEndDate")]
        public DateTime? COBRA_EndDate { get; set; }

        [Column("RetireeEnrolled")]
        public int? IsRetireeEnrolled { get; set; }

        [Column("RetireeStartDate")]
        public DateTime? Retiree_StartDate { get; set; }

        [Column("RetireeEndDate")]
        public DateTime? Retiree_EndDate { get; set; }
    }

    public class EmployeeStatus
    {
        [Column("id")]
        public int? StatusId { get; set; }

        [Column("employeeCodeId")]
        public int? EmployeeCodeId { get; set; }

        [Column("status")]
        public string? Status { get; set; }

        [Column("startDate")]
        public DateTime? StatusStartDate { get; set; }

        [Column("endDate")]
        public DateTime? StatusEndDate { get; set; }
    }

    public class EmployeePayroll
    {
        [Column("id")]
        public int? Id { get; set; }

        [Column("employeeCodeId")]
        public int? EmployeeCodeId { get; set; }

        [Column("payPeriodHourlyAmount")]
        public decimal? payPeriodHourlyAmount { get; set; }

        [Column("payPeriodSalaryAmount")]
        public decimal? payPeriodSalaryAmount { get; set; }

        [Column("payPeriodTotalHours")]
        public decimal? payPeriodTotalHours { get; set; }

        [Column("payPeriodAdditional")]
        public decimal? payPeriodAdditional { get; set; }

        [Column("payPeriodStartDate")]
        public DateTime? payPeriodStartDate { get; set; }

        [Column("payPeriodEndDate")]
        public DateTime? payPeriodEndDate { get; set; }
    }

    public class EmployeePayrollInfo
    {
        public int? Id { get; set; }
        public int? EmployeeCodeId { get; set; }
        public decimal payPeriodHourlyAmount { get; set; }
        public decimal payPeriodSalaryAmount { get; set; }
        public decimal payPeriodTotalHours { get; set; }
        public decimal payPeriodAdditional { get; set; }
        public DateTime? payPeriodStartDate { get; set; }
        public DateTime? payPeriodEndDate { get; set; }
    }
}

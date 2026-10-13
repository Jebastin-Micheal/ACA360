using ACA360.Core.Models;
using System;
using System.Data;

namespace ACA360.Web.Extensions
{
    /// <summary>
    /// Extension methods mapping Employee web models securely into DataTables for the Data Access Layer.
    /// Removes ~150 lines of boilerplate schema mapping from EmployeeController.cs.
    /// </summary>
    public static class EmployeeMappingExtensions
    {
        public static DataTable ToHireDataTable(this EmployeeBasicDetails employee)
        {
            DataTable dt = new();
            dt.Columns.Add("Id", typeof(int));
            dt.Columns.Add("EmployeeCodeId", typeof(int));
            dt.Columns.Add("Hire_StartDate", typeof(DateTime));
            dt.Columns.Add("Hire_EndDate", typeof(DateTime));

            employee.EmployeeHireDetail?.ForEach(h =>
                dt.Rows.Add(h.Id, h.EmployeeCodeId, h.Hire_StartDate ?? (object)DBNull.Value, h.Hire_EndDate ?? (object)DBNull.Value));

            return dt;
        }

        public static DataTable ToStatusDataTable(this EmployeeBasicDetails employee)
        {
            DataTable dt = new();
            dt.Columns.Add("StatusId", typeof(int));
            dt.Columns.Add("EmployeeCodeId", typeof(int));
            dt.Columns.Add("Status", typeof(string));
            dt.Columns.Add("StatusStartDate", typeof(DateTime));
            dt.Columns.Add("StatusEndDate", typeof(DateTime));

            employee.EmployeeStatus?.ForEach(s =>
                dt.Rows.Add(s.StatusId, s.EmployeeCodeId, s.Status, s.StatusStartDate ?? (object)DBNull.Value, s.StatusEndDate ?? (object)DBNull.Value));

            return dt;
        }

        public static DataTable ToPayrollDataTable(this EmployeeBasicDetails employee)
        {
            DataTable dt = new();
            dt.Columns.Add("Id", typeof(int));
            dt.Columns.Add("EmployeeCodeId", typeof(int));
            dt.Columns.Add("payPeriodHourlyAmount", typeof(decimal));
            dt.Columns.Add("payPeriodSalaryAmount", typeof(decimal));
            dt.Columns.Add("payPeriodTotalHours", typeof(decimal));
            dt.Columns.Add("payPeriodAdditional", typeof(decimal));
            dt.Columns.Add("payPeriodStartDate", typeof(DateTime));
            dt.Columns.Add("payPeriodEndDate", typeof(DateTime));

            employee.EmployeePayrollDetails?.ForEach(p =>
                dt.Rows.Add(
                    p.Id,
                    p.EmployeeCodeId,
                    p.payPeriodHourlyAmount == null ? (object)DBNull.Value : p.payPeriodHourlyAmount,
                    p.payPeriodSalaryAmount == null ? (object)DBNull.Value : p.payPeriodSalaryAmount,
                    p.payPeriodTotalHours == null ? (object)DBNull.Value : p.payPeriodTotalHours,
                    p.payPeriodAdditional == null ? (object)DBNull.Value : p.payPeriodAdditional,
                    p.payPeriodStartDate == null ? (object)DBNull.Value : p.payPeriodStartDate,
                    p.payPeriodEndDate == null ? (object)DBNull.Value : p.payPeriodEndDate
                ));

            return dt;
        }

        public static DataTable ToMedicalDataTable(this EmployeeBasicDetails employee)
        {
            DataTable dt = new();
            dt.Columns.Add("Id", typeof(int));
            dt.Columns.Add("planId", typeof(int));
            dt.Columns.Add("EmployeeCodeId", typeof(int));
            dt.Columns.Add("IsMedicalEnrolled", typeof(short));
            dt.Columns.Add("CoverageOfferDate", typeof(DateTime));
            dt.Columns.Add("Medical_CoverageStartDate", typeof(DateTime));
            dt.Columns.Add("Medical_CoverageEndDate", typeof(DateTime));

            employee.EmployeeEnrollment?.ForEach(m =>
                dt.Rows.Add(
                    m.Id,
                    m.planId == null ? (object)DBNull.Value : m.planId,
                    m.EmployeeCodeId == null ? (object)DBNull.Value : m.EmployeeCodeId,
                    m.IsMedicalEnrolled == null ? (object)DBNull.Value : m.IsMedicalEnrolled,
                    m.CoverageOfferDate == null ? (object)DBNull.Value : m.CoverageOfferDate,
                    m.Medical_CoverageStartDate == null ? (object)DBNull.Value : m.Medical_CoverageStartDate,
                    m.Medical_CoverageEndDate == null ? (object)DBNull.Value : m.Medical_CoverageEndDate
                ));

            return dt;
        }

        public static DataTable ToCobraDataTable(this EmployeeBasicDetails employee)
        {
            DataTable dt = new();
            dt.Columns.Add("Id", typeof(int));
            dt.Columns.Add("planId", typeof(int));
            dt.Columns.Add("EmployeeCodeId", typeof(int));
            dt.Columns.Add("IsCOBRAEnrolled", typeof(short));
            dt.Columns.Add("CoverageOfferDate", typeof(DateTime));
            dt.Columns.Add("COBRA_StartDate", typeof(DateTime));
            dt.Columns.Add("COBRA_EndDate", typeof(DateTime));

            employee.EmployeeEnrollment?.ForEach(c =>
                dt.Rows.Add(
                    c.Id,
                    c.planId == null ? (object)DBNull.Value : c.planId,
                    c.EmployeeCodeId == null ? (object)DBNull.Value : c.EmployeeCodeId,
                    c.IsCOBRAEnrolled == null ? (object)DBNull.Value : c.IsCOBRAEnrolled,
                    c.CoverageOfferDate == null ? (object)DBNull.Value : c.CoverageOfferDate,
                    c.COBRA_StartDate == null ? (object)DBNull.Value : c.COBRA_StartDate,
                    c.COBRA_EndDate == null ? (object)DBNull.Value : c.COBRA_EndDate
                ));

            return dt;
        }

        public static DataTable ToUnionDataTable(this EmployeeBasicDetails employee)
        {
            DataTable dt = new();
            dt.Columns.Add("Id", typeof(int));
            dt.Columns.Add("planId", typeof(int));
            dt.Columns.Add("EmployeeCodeId", typeof(int));
            dt.Columns.Add("IsUnionMember", typeof(short));
            dt.Columns.Add("CoverageOfferDate", typeof(DateTime));
            dt.Columns.Add("Union_ContributionStartDate", typeof(DateTime));
            dt.Columns.Add("Union_ContributionEndDate", typeof(DateTime));

            employee.EmployeeEnrollment?.ForEach(u =>
                dt.Rows.Add(
                    u.Id,
                    u.planId == null ? (object)DBNull.Value : u.planId,
                    u.EmployeeCodeId == null ? (object)DBNull.Value : u.EmployeeCodeId,
                    u.IsUnionMember == null ? (object)DBNull.Value : u.IsUnionMember,
                    u.CoverageOfferDate == null ? (object)DBNull.Value : u.CoverageOfferDate,
                    u.Union_ContributionStartDate == null ? (object)DBNull.Value : u.Union_ContributionStartDate,
                    u.Union_ContributionEndDate == null ? (object)DBNull.Value : u.Union_ContributionEndDate
                ));

            return dt;
        }

        public static DataTable ToRetireeDataTable(this EmployeeBasicDetails employee)
        {
            DataTable dt = new();
            dt.Columns.Add("Id", typeof(int));
            dt.Columns.Add("planId", typeof(int));
            dt.Columns.Add("EmployeeCodeId", typeof(int));
            dt.Columns.Add("IsRetireeEnrolled", typeof(short));
            dt.Columns.Add("CoverageOfferDate", typeof(DateTime));
            dt.Columns.Add("Retiree_StartDate", typeof(DateTime));
            dt.Columns.Add("Retiree_EndDate", typeof(DateTime));

            employee.EmployeeEnrollment?.ForEach(r =>
                dt.Rows.Add(
                    r.Id,
                    r.planId == null ? (object)DBNull.Value : r.planId,
                    r.EmployeeCodeId == null ? (object)DBNull.Value : r.EmployeeCodeId,
                    r.IsRetireeEnrolled == null ? (object)DBNull.Value : r.IsRetireeEnrolled,
                    r.CoverageOfferDate == null ? (object)DBNull.Value : r.CoverageOfferDate,
                    r.Retiree_StartDate == null ? (object)DBNull.Value : r.Retiree_StartDate,
                    r.Retiree_EndDate == null ? (object)DBNull.Value : r.Retiree_EndDate
                ));

            return dt;
        }

        public static DataTable ToDependentDataTable(this EmployeeBasicDetails employee)
        {
            DataTable dt = new();
            dt.Columns.Add("Id", typeof(int));
            dt.Columns.Add("EmployeeCodeId", typeof(int));
            dt.Columns.Add("FirstName", typeof(string));
            dt.Columns.Add("MiddleName", typeof(string));
            dt.Columns.Add("LastName", typeof(string));
            dt.Columns.Add("SSN", typeof(string));
            dt.Columns.Add("Suffix", typeof(string));
            dt.Columns.Add("Birthday", typeof(DateTime));
            dt.Columns.Add("CoverageStartDate", typeof(DateTime));
            dt.Columns.Add("CoverageEndDate", typeof(DateTime));

            employee.CoveredIndividuals?.ForEach(d =>
                dt.Rows.Add(
                    d.Id,
                    d.EmployeeCodeId == null ? (object)DBNull.Value : d.EmployeeCodeId,
                    d.FirstName == null ? (object)DBNull.Value : d.FirstName,
                    d.MiddleName == null ? (object)DBNull.Value : d.MiddleName,
                    d.LastName == null ? (object)DBNull.Value : d.LastName,
                    d.SSN == null ? (object)DBNull.Value : d.SSN,
                    d.Suffix == null ? (object)DBNull.Value : d.Suffix,
                    d.Birthday == null ? (object)DBNull.Value : d.Birthday,
                    d.CoverageStartDate == null ? (object)DBNull.Value : d.CoverageStartDate,
                    d.CoverageEndDate == null ? (object)DBNull.Value : d.CoverageEndDate
                ));

            return dt;
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;

namespace ACA360.Core.Models
{
    public sealed record ImportAnalysisColumn(string Key, string Title);
    public sealed record ImportAnalysisSheet(string Entity, string Label, IReadOnlyList<ImportAnalysisColumn> Columns);

    // Display order and headings follow ACA_Data_File - 2.xlsx. SQL independently validates sort keys.
    public static class ImportAnalysisCatalog
    {
        public static IReadOnlyList<ImportAnalysisSheet> Sheets { get; } = new ImportAnalysisSheet[]
        {
            new("Employer", "Employer", new ImportAnalysisColumn[]
            {
                new("PrimaryEIN", "Primary EIN"),
                new("AffiliatedEIN", "Affiliated EIN"),
                new("EmployerName", "Employer Name"),
                new("ForeignAddressInd", "Foreign Address Ind"),
                new("Address", "Address"),
                new("Address2", "Address 2"),
                new("City", "City"),
                new("StateOrProvince", "State/Province"),
                new("Zip", "Zip"),
                new("Country", "Country"),
                new("Phone", "Phone"),
                new("Contact", "Contact"),
                new("Title", "Title"),
                new("OriginCode", "Origin Code"),
                new("SHOPIdentifier", "SHOP Identifier"),
                new("Notes", "Notes"),
            }),
            new("Plan", "Plan", new ImportAnalysisColumn[]
            {
                new("PrimaryEIN", "Primary EIN"),
                new("PlanName", "Plan Name"),
                new("PlanType", "Plan Type"),
                new("OfferedToSpouse", "Offered to Spouse"),
                new("OfferedToDependents", "Offered to Dependents"),
                new("WaitingPeriodNumberOfDays", "Waiting period (number of days)"),
                new("EligibleFirstOfTheMonth", "Eligible First of the Month"),
                new("FundingType", "Funding Type"),
                new("PlanRenewalMonth", "Plan Renewal Month"),
                new("PlanTerminatesOnDateOfTermination", "Plan Terminates on Date of Termination"),
                new("MinimumValue", "Minimum Value"),
                new("BandingType", "Banding Type"),
                new("PremiumCap", "Premium Cap"),
            }),
            new("Premium", "Premium", new ImportAnalysisColumn[]
            {
                new("PrimaryEIN", "Primary EIN"),
                new("PlanName", "Plan Name"),
                new("BandingType", "Banding Type"),
                new("Start", "Start"),
                new("End", "End"),
                new("StartDate", "Start Date"),
                new("EndDate", "End Date"),
                new("EEMonthlyContribution", "EE Monthly Contribution"),
                new("Notes", "Notes"),
            }),
            new("Employee", "Employee", new ImportAnalysisColumn[]
            {
                new("PrimaryEIN", "Primary EIN"),
                new("EINAssociatedWithEE", "EIN Associated with EE (May be an Affiliate EIN)"),
                new("EmployeeLegalFirstName", "Employee Legal First Name"),
                new("EmployeeMiddleInitial", "Employee Middle Initial"),
                new("EmployeeLegalLastName", "Employee Legal Last Name"),
                new("EmployeeSuffix", "Employee Suffix"),
                new("EmployeeSSN", "Employee SSN"),
                new("EmployeeBirthdate", "Employee Birthdate"),
                new("Status", "Status"),
                new("Expatriate", "Expatriate"),
                new("StatusStartDate", "Status Start Date"),
                new("StatusEndDate", "Status End Date"),
                new("HireDate", "Hire Date"),
                new("TerminationDate", "Termination Date"),
                new("W2IncomeOrAnnualSalary", "W-2 Income/ Annual Salary (if applicable)"),
                new("AdditionalIncome", "Additional Income"),
                new("HourlyRate", "Hourly rate (if applicable)"),
                new("EmailAddress", "Email Address"),
                new("ForeignAddress", "Foreign Address"),
                new("Address1", "Address 1"),
                new("Address2", "Address 2"),
                new("City", "City"),
                new("StateOrProvince", "State/ Province"),
                new("Zip", "Zip"),
                new("Country", "Country"),
                new("LowestCostPlanOffered", "Lowest Cost Plan Offered (Plan Name)"),
                new("DateEligibleForCoverage", "Date Eligible for Coverage"),
                new("CoverageElected", "Coverage Elected?"),
                new("CoverageStartDate", "Coverage Start Date"),
                new("CoverageEndDate", "Coverage End Date"),
                new("UnionEmployee", "Union Employee? (Union offers benefits)"),
                new("DateEmployerFirstPaysToUnionBenefits", "Date Employer First Pays to Union Benefits"),
                new("DateEmployerLastPaysToUnionBenefits", "Date Employer Last Pays to Union Benefits"),
                new("COBRABenefitsElected", "COBRA Benefits elected?"),
                new("COBRACoverageEffectiveDate", "COBRA Coverage Effective Date"),
                new("COBRACoverageEndDate", "COBRA Coverage End Date"),
                new("RetireeBenefitsElected", "Retiree Benefits elected?"),
                new("RetireePlanStartDate", "Retiree Plan Start Date"),
                new("RetireePlanEndDate", "Retiree Plan End Date"),
            }),
            new("Dependent", "Dependents", new ImportAnalysisColumn[]
            {
                new("PrimaryEIN", "Primary  EIN"),
                new("EINAssociatedWithEE", "EIN Associated with EE (May be an Affiliate EIN)"),
                new("EmployeeSSN", "Employee SSN"),
                new("DependentLegalFirstName", "Dependent Legal      First Name"),
                new("DependentMiddleInitial", "Dependent Middle Initial"),
                new("DependentLegalLastName", "Dependent Legal      Last Name"),
                new("DependentSuffix", "Dependent Suffix"),
                new("DependentSSN", "Dependent SSN"),
                new("DependentBirthdate", "Dependent Birthdate"),
                new("DependentCoverageStartDate", "Dependent Coverage Start Date"),
                new("DependentCoverageEndDate", "Dependent Coverage End Date"),
                new("Relationship", "Relationship"),
            }),
        };

        public static ImportAnalysisSheet? Find(string? entity) =>
            Sheets.FirstOrDefault(s => string.Equals(s.Entity, entity, StringComparison.OrdinalIgnoreCase));

        public static bool IsMode(string? mode) => mode is "MERGE" or "OVERWRITE" or "Merge" or "Overwrite";
    }
}

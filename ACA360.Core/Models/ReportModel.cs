using ACA360.Core.Models;
using System;
using System.Collections.Generic;
using System.ComponentModel.Design;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;


namespace ACA360.Core.Models
{
   
    public class Employer_ReportModel
    {

       // public string? Id { get; set; }
        public string? FilingYear { get; set; }
        public string? CompanyId{ get; set; }
        public string? Taxid { get; set; }
        public string? Name { get; set; }
        public string? IsForeign { get; set; }
        public string? Address { get; set; }
        public string? Address2 { get; set; }
        public string? City { get; set; }
        public string? State { get; set; }
        public string? StateId { get; set; }
        public string? Zip { get; set; }
        public string? CountryId { get; set; }  
        public string? IsCorrected { get; set; }
        public string? ContactName { get; set; }
        public string? PhoneNumber { get; set; }
        public string? OriginCode { get; set; }
        public string? IsAuthoritative { get; set; }
        public string? TotalNumberForms { get; set; }
        public string? IsAggregatedAle { get; set; }
        public string? QualifyingOfferMethod { get; set; }
        public string? Sec4980HRelief { get; set; }
        public string? Offer98Method { get; set; }
        public string? SignTitle { get; set; }
        public string? SignDate { get; set; }
        public string? Minimum_0 { get; set; }
        public string? Minimum_1 { get; set; }
        public string? Minimum_2 { get; set; }
        public string? Minimum_3 { get; set; }
        public string? Minimum_4 { get; set; }
        public string? Minimum_5 { get; set; }
        public string? Minimum_6 { get; set; }
        public string? Minimum_7 { get; set; }
        public string? Minimum_8 { get; set; }
        public string? Minimum_9 { get; set; }
        public string? Minimum_10 { get; set; }
        public string? Minimum_11 { get; set; }
        public string? Minimum_12 { get; set; }
        public string? FullTime_0 { get; set; }
        public string? FullTime_1 { get; set; }
        public string? FullTime_2 { get; set; }
        public string? FullTime_3 { get; set; }
        public string? FullTime_4 { get; set; }
        public string? FullTime_5 { get; set; }
        public string? FullTime_6 { get; set; }
        public string? FullTime_7 { get; set; }
        public string? FullTime_8 { get; set; }
        public string? FullTime_9 { get; set; }
        public string? FullTime_10 { get; set; }
        public string? FullTime_11 { get; set; }
        public string? FullTime_12 { get; set; }
        public string? Total_0 { get; set; }
        public string? Total_1 { get; set; }
        public string? Total_2 { get; set; }
        public string? Total_3 { get; set; }
        public string? Total_4 { get; set; }
        public string? Total_5 { get; set; }
        public string? Total_6 { get; set; }
        public string? Total_7 { get; set; }
        public string? Total_8 { get; set; }
        public string? Total_9 { get; set; }
        public string? Total_10 { get; set; }
        public string? Total_11 { get; set; }
        public string? Total_12 { get; set; }
        public string? Group_0 { get; set; }
        public string? Group_1 { get; set; }
        public string? Group_2     { get; set; }
        public string? Group_3 { get; set; }
        public string? Group_4 { get; set; }
        public string? Group_5 { get; set; }
        public string? Group_6 { get; set; }
        public string? Group_7 { get; set; }
        public string? Group_8 { get; set; }
        public string? Group_9 { get; set; }
        public string? Group_10 { get; set; }
        public string? Group_11   { get; set; }
        public string? Group_12 { get; set; }
        public string? S4980H_0 { get; set; }
        public string? S4980H_1 { get; set; }
        public string? S4980H_2 { get; set; }
        public string? S4980H_3 { get; set; }
        public string? S4980H_4 { get; set; }
        public string? S4980H_5    { get; set; }
        public string? S4980H_6 { get; set; }
        public string? S4980H_7 { get; set; }
        public string? S4980H_8 { get; set; }
        public string? S4980H_9 { get; set; }
        public string? S4980H_10 { get; set; }
        public string? S4980H_11   { get; set; }
        public string? S4980H_12 { get; set; }
        public string? DisableChanges { get; set; }
        public string? FormType { get; set; }
        public string? MailMemo { get; set; }
        public string? EnableCallCenter { get; set; }

    }

    public class AcaReportViewModel
    {
        // Holds the inputs for the search form
        public string? SearchTaxId { get; set; }
        public string? SearchSsn { get; set; }
        public string? SearchYear { get; set; }

        // Holds the results
        public Employer1094Data? Employer { get; set; }
        public List<Employee1095Data>? Employees { get; set; } = new List<Employee1095Data>();

        // Error message if something goes wrong
        public string? ErrorMessage { get; set; }
    }
        
    public class Employer1094Data
    {
        public string? EmployerEIN { get; set; }
        public string? BusinessNameLine1Txt { get; set; }
        public string? BusinessNameLine2Txt { get; set; }
        public string? PersonFirstNm { get; set; }
        public string? PersonMiddleNm { get; set; }
        public string? PersonLastNm { get; set; }
        public string? SuffixNm { get; set; }
        public string? ContactPhoneNum { get; set; }
        public string? ForeignAddressInd { get; set; }
        public string? AddressLine1Txt { get; set; }
        public string? AddressLine2Txt { get; set; }
        public string? CityNm { get; set; }
        public string? USStateCd { get; set; }
        public string? USZIPCd { get; set; }
        public string? USZIPExtensionCd { get; set; }
        public string? ForeignAddressLine1Txt { get; set; }
        public string? ForeignAddressLine2Txt { get; set; }
        public string? ForeignCityNm { get; set; }
        public string? ForeignProvinceNm { get; set; }
        public string? ForeignPostalCd { get; set; }
        public string? ForeignCountryCd { get; set; }
        public string? CorrectedInd { get; set; }

        // GovEntity Columns
        public string? GovEntity_BusinessNameLine1Txt { get; set; }
        public string? GovEntity_BusinessNameLine2Txt { get; set; }
        public string? GovEntity_EmployerEIN { get; set; }
        public string? GovEntity_ForeignAddressInd { get; set; }
        public string? GovEntity_AddressLine1Txt { get; set; }
        public string? GovEntity_AddressLine2Txt { get; set; }
        public string? GovEntity_CityNm { get; set; }
        public string? GovEntity_USStateCd { get; set; }
        public string? GovEntity_USZIPCd { get; set; }
        public string? GovEntity_USZIPExtensionCd { get; set; }
        public string? GovEntity_ForeignAddressLine1Txt { get; set; }
        public string? GovEntity_ForeignAddressLine2Txt { get; set; }
        public string? GovEntity_ForeignCityNm { get; set; }
        public string? GovEntity_ForeignProvinceNm { get; set; }
        public string? GovEntity_ForeignPostalCd { get; set; }
        public string? GovEntity_ForeignCountryCd { get; set; }
        public string? GovEntity_PersonFirstNm { get; set; }
        public string? GovEntity_PersonMiddleNm { get; set; }
        public string? GovEntity_PersonLastNm { get; set; }
        public string? GovEntity_SuffixNm { get; set; }
        public string? GovEntity_ContactPhoneNum { get; set; }

        public string? AuthoritativeTransmittalInd { get; set; }
        public string? TotalForm1095CALEMemberCnt { get; set; }
        public string? AggregatedGroupMemberInd { get; set; }
        public string? QualifyingOfferMethodInd { get; set; }
        public string? Section4980HReliefInd { get; set; }
        public string? NinetyEightPctOfferMethodInd { get; set; }
        public string? PersonTitleTxt { get; set; }
        public string? SignatureDt { get; set; }

        // Monthly Data (Yearly + Jan-Dec)
        public string? Yearly_MinEssentialCvrOffrInd { get; set; }
        public string? Yearly_ALEMemberFTECnt { get; set; }
        public string? Yearly_TotalEmployeeCnt { get; set; }
        public string? Yearly_AggregatedGroupInd { get; set; }
        public string? Yearly_ALESect4980HTrnstReliefCd { get; set; }

        public string? Jan_MinEssentialCvrOffrInd { get; set; }
        public string? Jan_ALEMemberFTECnt { get; set; }
        public string? Jan_TotalEmployeeCnt { get; set; }
        public string? Jan_AggregatedGroupInd { get; set; }
        public string? Jan_ALESect4980HTrnstReliefCd { get; set; }

        public string? Feb_MinEssentialCvrOffrInd { get; set; }
        public string? Feb_ALEMemberFTECnt { get; set; }
        public string? Feb_TotalEmployeeCnt { get; set; }
        public string? Feb_AggregatedGroupInd { get; set; }
        public string? Feb_ALESect4980HTrnstReliefCd { get; set; }

        public string? Mar_MinEssentialCvrOffrInd { get; set; }
        public string? Mar_ALEMemberFTECnt { get; set; }
        public string? Mar_TotalEmployeeCnt { get; set; }
        public string? Mar_AggregatedGroupInd { get; set; }
        public string? Mar_ALESect4980HTrnstReliefCd { get; set; }

        public string? Apr_MinEssentialCvrOffrInd { get; set; }
        public string? Apr_ALEMemberFTECnt { get; set; }
        public string? Apr_TotalEmployeeCnt { get; set; }
        public string? Apr_AggregatedGroupInd { get; set; }
        public string? Apr_ALESect4980HTrnstReliefCd { get; set; }

        public string? May_MinEssentialCvrOffrInd { get; set; }
        public string? May_ALEMemberFTECnt { get; set; }
        public string? May_TotalEmployeeCnt { get; set; }
        public string? May_AggregatedGroupInd { get; set; }
        public string? May_ALESect4980HTrnstReliefCd { get; set; }

        public string? Jun_MinEssentialCvrOffrInd { get; set; }
        public string? Jun_ALEMemberFTECnt { get; set; }
        public string? Jun_TotalEmployeeCnt { get; set; }
        public string? Jun_AggregatedGroupInd { get; set; }
        public string? Jun_ALESect4980HTrnstReliefCd { get; set; }

        public string? Jul_MinEssentialCvrOffrInd { get; set; }
        public string? Jul_ALEMemberFTECnt { get; set; }
        public string? Jul_TotalEmployeeCnt { get; set; }
        public string? Jul_AggregatedGroupInd { get; set; }
        public string? Jul_ALESect4980HTrnstReliefCd { get; set; }

        public string? Aug_MinEssentialCvrOffrInd { get; set; }
        public string? Aug_ALEMemberFTECnt { get; set; }
        public string? Aug_TotalEmployeeCnt { get; set; }
        public string? Aug_AggregatedGroupInd { get; set; }
        public string? Aug_ALESect4980HTrnstReliefCd { get; set; }

        public string? Sep_MinEssentialCvrOffrInd { get; set; }
        public string? Sep_ALEMemberFTECnt { get; set; }
        public string? Sep_TotalEmployeeCnt { get; set; }
        public string? Sep_AggregatedGroupInd { get; set; }
        public string? Sep_ALESect4980HTrnstReliefCd { get; set; }

        public string? Oct_MinEssentialCvrOffrInd { get; set; }
        public string? Oct_ALEMemberFTECnt { get; set; }
        public string? Oct_TotalEmployeeCnt { get; set; }
        public string? Oct_AggregatedGroupInd { get; set; }
        public string? Oct_ALESect4980HTrnstReliefCd { get; set; }

        public string? Nov_MinEssentialCvrOffrInd { get; set; }
        public string? Nov_ALEMemberFTECnt { get; set; }
        public string? Nov_TotalEmployeeCnt { get; set; }
        public string? Nov_AggregatedGroupInd { get; set; }
        public string? Nov_ALESect4980HTrnstReliefCd { get; set; }

        public string? Dec_MinEssentialCvrOffrInd { get; set; }
        public string? Dec_ALEMemberFTECnt { get; set; }
        public string? Dec_TotalEmployeeCnt { get; set; }
        public string? Dec_AggregatedGroupInd { get; set; }
        public string? Dec_ALESect4980HTrnstReliefCd { get; set; }

        // Other ALE Members (1-30)       
        // Member 01
        public string? BusinessNameLine1Txt_01 { get; set; }
        public string? BusinessNameLine2Txt_01 { get; set; }
        public string? EIN_01 { get; set; }

        // Member 02
        public string? BusinessNameLine1Txt_02 { get; set; }
        public string? BusinessNameLine2Txt_02 { get; set; }
        public string? EIN_02 { get; set; }

        // Member 03
        public string? BusinessNameLine1Txt_03 { get; set; }
        public string? BusinessNameLine2Txt_03 { get; set; }
        public string? EIN_03 { get; set; }

        // Member 04
        public string? BusinessNameLine1Txt_04 { get; set; }
        public string? BusinessNameLine2Txt_04 { get; set; }
        public string? EIN_04 { get; set; }

        // Member 05
        public string? BusinessNameLine1Txt_05 { get; set; }
        public string? BusinessNameLine2Txt_05 { get; set; }
        public string? EIN_05 { get; set; }

        // Member 06
        public string? BusinessNameLine1Txt_06 { get; set; }
        public string? BusinessNameLine2Txt_06 { get; set; }
        public string? EIN_06 { get; set; }

        // Member 07
        public string? BusinessNameLine1Txt_07 { get; set; }
        public string? BusinessNameLine2Txt_07 { get; set; }
        public string? EIN_07 { get; set; }

        // Member 08
        public string? BusinessNameLine1Txt_08 { get; set; }
        public string? BusinessNameLine2Txt_08 { get; set; }
        public string? EIN_08 { get; set; }

        // Member 09
        public string? BusinessNameLine1Txt_09 { get; set; }
        public string? BusinessNameLine2Txt_09 { get; set; }
        public string? EIN_09 { get; set; }

        // Member 10
        public string? BusinessNameLine1Txt_10 { get; set; }
        public string? BusinessNameLine2Txt_10 { get; set; }
        public string? EIN_10 { get; set; }

        // Member 11
        public string? BusinessNameLine1Txt_11 { get; set; }
        public string? BusinessNameLine2Txt_11 { get; set; }
        public string? EIN_11 { get; set; }

        // Member 12
        public string? BusinessNameLine1Txt_12 { get; set; }
        public string? BusinessNameLine2Txt_12 { get; set; }
        public string? EIN_12 { get; set; }

        // Member 13
        public string? BusinessNameLine1Txt_13 { get; set; }
        public string? BusinessNameLine2Txt_13 { get; set; }
        public string? EIN_13 { get; set; }

        // Member 14
        public string? BusinessNameLine1Txt_14 { get; set; }
        public string? BusinessNameLine2Txt_14 { get; set; }
        public string? EIN_14 { get; set; }

        // Member 15
        public string? BusinessNameLine1Txt_15 { get; set; }
        public string? BusinessNameLine2Txt_15 { get; set; }
        public string? EIN_15 { get; set; }

        // Member 16
        public string? BusinessNameLine1Txt_16 { get; set; }
        public string? BusinessNameLine2Txt_16 { get; set; }
        public string? EIN_16 { get; set; }

        // Member 17
        public string? BusinessNameLine1Txt_17 { get; set; }
        public string? BusinessNameLine2Txt_17 { get; set; }
        public string? EIN_17 { get; set; }

        // Member 18
        public string? BusinessNameLine1Txt_18 { get; set; }
        public string? BusinessNameLine2Txt_18 { get; set; }
        public string? EIN_18 { get; set; }

        // Member 19
        public string? BusinessNameLine1Txt_19 { get; set; }
        public string? BusinessNameLine2Txt_19 { get; set; }
        public string? EIN_19 { get; set; }

        // Member 20
        public string? BusinessNameLine1Txt_20 { get; set; }
        public string? BusinessNameLine2Txt_20 { get; set; }
        public string? EIN_20 { get; set; }

        // Member 21
        public string? BusinessNameLine1Txt_21 { get; set; }
        public string? BusinessNameLine2Txt_21 { get; set; }
        public string? EIN_21 { get; set; }

        // Member 22
        public string? BusinessNameLine1Txt_22 { get; set; }
        public string? BusinessNameLine2Txt_22 { get; set; }
        public string? EIN_22 { get; set; }

        // Member 23
        public string? BusinessNameLine1Txt_23 { get; set; }
        public string? BusinessNameLine2Txt_23 { get; set; }
        public string? EIN_23 { get; set; }

        // Member 24
        public string? BusinessNameLine1Txt_24 { get; set; }
        public string? BusinessNameLine2Txt_24 { get; set; }
        public string? EIN_24 { get; set; }

        // Member 25
        public string? BusinessNameLine1Txt_25 { get; set; }
        public string? BusinessNameLine2Txt_25 { get; set; }
        public string? EIN_25 { get; set; }

        // Member 26
        public string? BusinessNameLine1Txt_26 { get; set; }
        public string? BusinessNameLine2Txt_26 { get; set; }
        public string? EIN_26 { get; set; }

        // Member 27
        public string? BusinessNameLine1Txt_27 { get; set; }
        public string? BusinessNameLine2Txt_27 { get; set; }
        public string? EIN_27 { get; set; }

        // Member 28
        public string? BusinessNameLine1Txt_28 { get; set; }
        public string? BusinessNameLine2Txt_28 { get; set; }
        public string? EIN_28 { get; set; }

        // Member 29
        public string? BusinessNameLine1Txt_29 { get; set; }
        public string? BusinessNameLine2Txt_29 { get; set; }
        public string? EIN_29 { get; set; }

        // Member 30
        public string? BusinessNameLine1Txt_30 { get; set; }
        public string? BusinessNameLine2Txt_30 { get; set; }
        public string? EIN_30 { get; set; }
        
    }

    // Result Set 2: Employee / 1095-C Data
    public class Employee1095Data
    {
        public string? Employer_EIN { get; set; }
        public string? Employee_SSN { get; set; }
        public string? Member_SSN { get; set; }
        public string? First_Name { get; set; }
        public string? Middle_Name { get; set; }
        public string? Last_Name { get; set; }
        public string? Suffix { get; set; }
        public string? CorrectedInd { get; set; }

        public string? Employee_ForeignAddressInd { get; set; }
        public string? Employee_AddressLine1Txt { get; set; }
        public string? Employee_AddressLine2Txt { get; set; }
        public string? Employee_CityNm { get; set; }
        public string? Employee_USStateCd { get; set; }
        public string? Employee_USZIPCd { get; set; }
        public string? Employee_USZIPExtensionCd { get; set; }
        public string? Employee_ForeignAddressLine1Txt { get; set; }
        public string? Employee_ForeignAddressLine2Txt { get; set; }
        public string? Employee_ForeignCityNm { get; set; }
        public string? Employee_ForeignProvinceNm { get; set; }
        public string? Employee_ForeignPostalCd { get; set; }
        public string? Employee_ForeignCountryCd { get; set; }

        public string? PlanStartMonth { get; set; }

        // Annual
        public string? AnnualOfferOfCoverageCd { get; set; }
        public string? AnnualShrLowestCostMthlyPremAmt { get; set; }
        public string? AnnualSafeHarborCd { get; set; }

        // Monthly Offer/Amt/SafeHarbor
        public string? JanOfferCd { get; set; }
        public string? JanuaryAmt { get; set; }
        public string? JanSafeHarborCd { get; set; }

        public string? FebOfferCd { get; set; }
        public string? FebruaryAmt { get; set; }
        public string? FebSafeHarborCd { get; set; }

        public string? MarOfferCd { get; set; }
        public string? MarchAmt { get; set; }
        public string? MarSafeHarborCd { get; set; }

        public string? AprOfferCd { get; set; }
        public string? AprilAmt { get; set; }
        public string? AprSafeHarborCd { get; set; }

        public string? MayOfferCd { get; set; }
        public string? MayAmt { get; set; }
        public string? MaySafeHarborCd { get; set; }

        public string? JunOfferCd { get; set; }
        public string? JuneAmt { get; set; }
        public string? JuneSafeHarborCd { get; set; }

        public string? JulOfferCd { get; set; }
        public string? JulyAmt { get; set; }
        public string? JulSafeHarborCd { get; set; }

        public string? AugOfferCd { get; set; }
        public string? AugustAmt { get; set; }
        public string? AugSafeHarborCd { get; set; }

        public string? SepOfferCd { get; set; }
        public string? SeptemberAmt { get; set; }
        public string? SepSafeHarborCd { get; set; }

        public string? OctOfferCd { get; set; }
        public string? OctoberAmt { get; set; }
        public string? OctSafeHarborCd { get; set; }

        public string? NovOfferCd { get; set; }
        public string? NovemberAmt { get; set; }
        public string? NovSafeHarborCd { get; set; }

        public string? DecOfferCd { get; set; }
        public string? DecemberAmt { get; set; }
        public string? DecSafeHarborCd { get; set; }

        public string? CoveredIndividualInd { get; set; }
        public string? Dependent_BirthDt { get; set; }
        public string? Dependent_CoveredIndividualAnnualInd { get; set; }

        public string? Dependent_JanuaryInd { get; set; }
        public string? Dependent_FebruaryInd { get; set; }
        public string? Dependent_MarchInd { get; set; }
        public string? Dependent_AprilInd { get; set; }
        public string? Dependent_MayInd { get; set; }
        public string? Dependent_JuneInd { get; set; }
        public string? Dependent_JulyInd { get; set; }
        public string? Dependent_AugustInd { get; set; }
        public string? Dependent_SeptemberInd { get; set; }
        public string? Dependent_OctoberInd { get; set; }
        public string? Dependent_NovemberInd { get; set; }
        public string? Dependent_DecemberInd { get; set; }

        public string? Parent_First_Name { get; set; }
        public string? Parent_Middle_Name { get; set; }
        public string? Parent_Last_Name { get; set; }
        public string? Parent_Suffix { get; set; }
        public string? Parent_Birth_Date { get; set; }
    }


    // Container ViewModel
    public class ExportMailFulfillmentViewModel
    {
        public List<MailFulfillment_EmployerModel>? Employers { get; set; }
        public List<MailFulfillment_EmployeeModel>? Employees { get; set; }
    }

    // Result Set 1: Employer (1094)
    public class MailFulfillment_EmployerModel
    {
        public string? EmployerEIN { get; set; }
        public string? BusinessNameLine1Txt { get; set; }
        public string? BusinessNameLine2Txt { get; set; }
        public string? PersonFirstNm { get; set; }
        public string? PersonMiddleNm { get; set; }
        public string? PersonLastNm { get; set; }
        public string? SuffixNm { get; set; }
        public string? ContactPhoneNum { get; set; }
        public string? ForeignAddressInd { get; set; }
        public string? AddressLine1Txt { get; set; }
        public string? AddressLine2Txt { get; set; }
        public string? CityNm { get; set; }
        public string? USStateCd { get; set; }
        public string? USZIPCd { get; set; }
        public string? USZIPExtensionCd { get; set; }

        public string? CorrectedInd { get; set; }
        public string? AuthoritativeTransmittalInd { get; set; }
        public string? TotalForm1095CALEMemberCnt { get; set; }
        public string? AggregatedGroupMemberInd { get; set; }
        public string? QualifyingOfferMethodInd { get; set; }
        public string? Section4980HReliefInd { get; set; }
        public string? NinetyEightPctOfferMethodInd { get; set; }
        public string? PersonTitleTxt { get; set; }
        public string? SignatureDt { get; set; }
        public string? FormType { get; set; }

        // Monthly Breakdown (Yearly + Jan-Dec)
        public string? Yearly_MinEssentialCvrOffrInd { get; set; }
        public string? Yearly_ALEMemberFTECnt { get; set; }
        public string? Yearly_TotalEmployeeCnt { get; set; }
        public string? Yearly_AggregatedGroupInd { get; set; }
        public string? Yearly_ALESect4980HTrnstReliefCd { get; set; }

        // January
        public string? Jan_MinEssentialCvrOffrInd { get; set; }
        public string? Jan_ALEMemberFTECnt { get; set; }
        public string? Jan_TotalEmployeeCnt { get; set; }
        public string? Jan_AggregatedGroupInd { get; set; }
        public string? Jan_ALESect4980HTrnstReliefCd { get; set; }

        // February
        public string? Feb_MinEssentialCvrOffrInd { get; set; }
        public string? Feb_ALEMemberFTECnt { get; set; }
        public string? Feb_TotalEmployeeCnt { get; set; }
        public string? Feb_AggregatedGroupInd { get; set; }
        public string? Feb_ALESect4980HTrnstReliefCd { get; set; }

        // March
        public string? Mar_MinEssentialCvrOffrInd { get; set; }
        public string? Mar_ALEMemberFTECnt { get; set; }
        public string? Mar_TotalEmployeeCnt { get; set; }
        public string? Mar_AggregatedGroupInd { get; set; }
        public string? Mar_ALESect4980HTrnstReliefCd { get; set; }

        // April
        public string? Apr_MinEssentialCvrOffrInd { get; set; }
        public string? Apr_ALEMemberFTECnt { get; set; }
        public string? Apr_TotalEmployeeCnt { get; set; }
        public string? Apr_AggregatedGroupInd { get; set; }
        public string? Apr_ALESect4980HTrnstReliefCd { get; set; }

        // May
        public string? May_MinEssentialCvrOffrInd { get; set; }
        public string? May_ALEMemberFTECnt { get; set; }
        public string? May_TotalEmployeeCnt { get; set; }
        public string? May_AggregatedGroupInd { get; set; }
        public string? May_ALESect4980HTrnstReliefCd { get; set; }

        // June
        public string? Jun_MinEssentialCvrOffrInd { get; set; }
        public string? Jun_ALEMemberFTECnt { get; set; }
        public string? Jun_TotalEmployeeCnt { get; set; }
        public string? Jun_AggregatedGroupInd { get; set; }
        public string? Jun_ALESect4980HTrnstReliefCd { get; set; }

        // July
        public string? Jul_MinEssentialCvrOffrInd { get; set; }
        public string? Jul_ALEMemberFTECnt { get; set; }
        public string? Jul_TotalEmployeeCnt { get; set; }
        public string? Jul_AggregatedGroupInd { get; set; }
        public string? Jul_ALESect4980HTrnstReliefCd { get; set; }

        // August
        public string? Aug_MinEssentialCvrOffrInd { get; set; }
        public string? Aug_ALEMemberFTECnt { get; set; }
        public string? Aug_TotalEmployeeCnt { get; set; }
        public string? Aug_AggregatedGroupInd { get; set; }
        public string? Aug_ALESect4980HTrnstReliefCd { get; set; }

        // September
        public string? Sep_MinEssentialCvrOffrInd { get; set; }
        public string? Sep_ALEMemberFTECnt { get; set; }
        public string? Sep_TotalEmployeeCnt { get; set; }
        public string? Sep_AggregatedGroupInd { get; set; }
        public string? Sep_ALESect4980HTrnstReliefCd { get; set; }

        // October
        public string? Oct_MinEssentialCvrOffrInd { get; set; }
        public string? Oct_ALEMemberFTECnt { get; set; }
        public string? Oct_TotalEmployeeCnt { get; set; }
        public string? Oct_AggregatedGroupInd { get; set; }
        public string? Oct_ALESect4980HTrnstReliefCd { get; set; }

        // November
        public string? Nov_MinEssentialCvrOffrInd { get; set; }
        public string? Nov_ALEMemberFTECnt { get; set; }
        public string? Nov_TotalEmployeeCnt { get; set; }
        public string? Nov_AggregatedGroupInd { get; set; }
        public string? Nov_ALESect4980HTrnstReliefCd { get; set; }

        // December
        public string? Dec_MinEssentialCvrOffrInd { get; set; }
        public string? Dec_ALEMemberFTECnt { get; set; }
        public string? Dec_TotalEmployeeCnt { get; set; }
        public string? Dec_AggregatedGroupInd { get; set; }
        public string? Dec_ALESect4980HTrnstReliefCd { get; set; }
    }

    // Result Set 2: Employee (1095)
    public class MailFulfillment_EmployeeModel
    {
        public string? Employer_EIN { get; set; }
        public string? Employee_SSN { get; set; }
        public string? Member_SSN { get; set; }
        public string? First_Name { get; set; }
        public string? Middle_Name { get; set; }
        public string? Last_Name { get; set; }
        public string? Suffix { get; set; }
        public string? Mail_Memo { get; set; } // Specific to this report

        public string? AddressLine1Txt { get; set; }
        public string? AddressLine2Txt { get; set; }
        public string? CityNm { get; set; }
        public string? USStateCd { get; set; }
        public string? USZIPCd { get; set; }

        public string? PlanStartMonth { get; set; }
        public string? CorrectedInd { get; set; }
        public string? SelfInsuredInd { get; set; }
        public string? FormType { get; set; }

        // Offer Codes
        public string? AnnualOfferOfCoverageCd { get; set; }
        public string? JanOfferCd { get; set; }
        public string? FebOfferCd { get; set; }
        public string? MarOfferCd { get; set; }
        public string? AprOfferCd { get; set; }
        public string? MayOfferCd { get; set; }
        public string? JunOfferCd { get; set; }
        public string? JulOfferCd { get; set; }
        public string? AugOfferCd { get; set; }
        public string? SepOfferCd { get; set; }
        public string? OctOfferCd { get; set; }
        public string? NovOfferCd { get; set; }
        public string? DecOfferCd { get; set; }

        // Amounts
        public string? AnnualShrLowestCostMthlyPremAmt { get; set; }
        public string? JanuaryAmt { get; set; }
        public string? FebruaryAmt { get; set; }
        public string? MarchAmt { get; set; }
        public string? AprilAmt { get; set; }
        public string? MayAmt { get; set; }
        public string? JuneAmt { get; set; }
        public string? JulyAmt { get; set; }
        public string? AugustAmt { get; set; }
        public string? SeptemberAmt { get; set; }
        public string? OctoberAmt { get; set; }
        public string? NovemberAmt { get; set; }
        public string? DecemberAmt { get; set; }

        // Safe Harbor
        public string? AnnualSafeHarborCd { get; set; }
        public string? JanSafeHarborCd { get; set; }
        public string? FebSafeHarborCd { get; set; }
        public string? MarSafeHarborCd { get; set; }
        public string? AprSafeHarborCd { get; set; }
        public string? MaySafeHarborCd { get; set; }
        public string? JuneSafeHarborCd { get; set; }
        public string? JulSafeHarborCd { get; set; }
        public string? AugSafeHarborCd { get; set; }
        public string? SepSafeHarborCd { get; set; }
        public string? OctSafeHarborCd { get; set; }
        public string? NovSafeHarborCd { get; set; }
        public string? DecSafeHarborCd { get; set; }

        // Covered Individuals
        public string? CoveredIndividualInd { get; set; }
        public string? Dependent_BirthDt { get; set; }
        public string? Dependent_CoveredIndividualAnnualInd { get; set; }
        public string? Dependent_JanuaryInd { get; set; }
        public string? Dependent_FebruaryInd { get; set; }
        public string? Dependent_MarchInd { get; set; }
        public string? Dependent_AprilInd { get; set; }
        public string? Dependent_MayInd { get; set; }
        public string? Dependent_JuneInd { get; set; }
        public string? Dependent_JulyInd { get; set; }
        public string? Dependent_AugustInd { get; set; }
        public string? Dependent_SeptemberInd { get; set; }
        public string? Dependent_OctoberInd { get; set; }
        public string? Dependent_NovemberInd { get; set; }
        public string? Dependent_DecemberInd { get; set; }
    }

}


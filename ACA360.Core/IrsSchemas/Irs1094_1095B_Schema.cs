using System;
using System.Collections.Generic;
using System.Xml.Serialization;

namespace ACA360.Core.IrsSchemas
{
    // --- ROOT ELEMENT (1094-B) ---
    [XmlRoot("Form1094BUpstreamDetail", Namespace = "urn:us:gov:treasury:irs:ext:aca:air:7.0")]
    public class Form1094BUpstreamDetail
    {
        [XmlElement("SubmissionId")]
        public string? SubmissionId { get; set; }

        [XmlElement("TaxYr")]
        public string TaxYr { get; set; }

        [XmlElement("CorrectedInd")]
        public string? CorrectedInd { get; set; } // 0 or 1

        // 1094-B Filer (Employer) Info
        [XmlElement("BusinessName")]
        public BusinessNameType? BusinessName { get; set; }

        [XmlElement("BusinessEIN")]
        public string? BusinessEIN { get; set; }

        [XmlElement("BusinessAddressGroup")]
        public AddressType? BusinessAddressGroup { get; set; }

        [XmlElement("ContactName")]
        public PersonNameType? ContactName { get; set; }

        [XmlElement("ContactPhoneNum")]
        public string? ContactPhoneNum { get; set; }

        [XmlElement("TotalForm1095BSubmittedCnt")]
        public int TotalForm1095BSubmittedCnt { get; set; }

        [XmlElement("JuratSignaturePIN")]
        public string? JuratSignaturePIN { get; set; }

        // LIST OF 1095-Bs
        [XmlElement("Form1095BUpstreamDetail")]
        public List<Form1095BUpstreamDetail> Form1095Bs { get; set; } = new List<Form1095BUpstreamDetail>();
    }

    // --- 1095-B CHILD ELEMENT ---
    public class Form1095BUpstreamDetail
    {
        [XmlElement("RecordId")]
        public string? RecordId { get; set; }

        [XmlElement("CorrectedInd")]
        public string? CorrectedInd { get; set; }

        // Part I: Responsible Individual
        [XmlElement("ResponsibleIndividual")]
        public ResponsibleIndividualType? ResponsibleIndividual { get; set; }

        // Part II: Employer Sponsored Coverage
        [XmlElement("EmployerSponsoredCoverage")]
        public EmployerSponsoredCoverageType EmployerSponsoredCoverage { get; set; }

        // Part III: Issuer (Optional/Skipped if same as Filer)

        // Part IV: Covered Individuals
        [XmlElement("CoveredIndividual")]
        public List<CoveredIndividualType>? CoveredIndividuals { get; set; }
    }

    // --- HELPER TYPES FOR B-FORMS ---

    public class ResponsibleIndividualType
    {
        [XmlElement("PersonName")]
        public PersonNameType PersonName { get; set; }

        [XmlElement("TIN")]
        public string TIN { get; set; } // SSN

        [XmlElement("BirthDt")]
        public string BirthDt { get; set; }

        [XmlElement("Address")]
        public AddressType Address { get; set; }

        [XmlElement("OriginOfHealthCoverageCd")]
        public string OriginOfHealthCoverageCd { get; set; } // B = Employer Sponsored
    }

    public class EmployerSponsoredCoverageType
    {
        // For self-insured employers, this repeats the employer info
        [XmlElement("EmployerName")]
        public BusinessNameType EmployerName { get; set; }

        [XmlElement("EmployerEIN")]
        public string EmployerEIN { get; set; }

        [XmlElement("EmployerAddress")]
        public AddressType EmployerAddress { get; set; }
    }
}
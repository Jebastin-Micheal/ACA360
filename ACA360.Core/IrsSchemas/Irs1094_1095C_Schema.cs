using System;
using System.Collections.Generic;
using System.Xml.Serialization;

namespace ACA360.Core.IrsSchemas
{
    // --- ROOT ELEMENT ---
    [XmlRoot("Form1094CUpstreamDetail", Namespace = "urn:us:gov:treasury:irs:ext:aca:air:7.0")]
    public class Form1094CUpstreamDetail
    {
        [XmlElement("SubmissionId")]
        public string? SubmissionId { get; set; } // Unique ID for this 1094 group

        [XmlElement("TaxYr")]
        public string? TaxYr { get; set; }

        [XmlElement("CorrectedInd")]
        public string? CorrectedInd { get; set; } // 0 or 1

        // 1094-C Data
        [XmlElement("EmployerName")]
        public BusinessNameType? EmployerName { get; set; }

        [XmlElement("EmployerEIN")]
        public string? EmployerEIN { get; set; }

        [XmlElement("EmployerAddress")]
        public AddressType? EmployerAddress { get; set; }

        [XmlElement("ContactName")]
        public PersonNameType? ContactName { get; set; }

        [XmlElement("ContactPhoneNum")]
        public string? ContactPhoneNum { get; set; }

        [XmlElement("JuratSignaturePIN")]
        public string? JuratSignaturePIN { get; set; } // Optional for UI channel, required for A2A

        [XmlElement("AleMemberInformation")]
        public AleMemberInformationType? AleMemberInformation { get; set; }

        [XmlElement("AleMemberInformationMonthly")]
        public AleMemberInformationMonthlyType? AleMemberInformationMonthly { get; set; }

        // LIST OF 1095-Cs attached to this 1094
        [XmlElement("Form1095CUpstreamDetail")]
        public List<Form1095CUpstreamDetail> Form1095Cs { get; set; } = new List<Form1095CUpstreamDetail>();
    }

    // --- 1095-C CHILD ELEMENT ---
    public class Form1095CUpstreamDetail
    {
        [XmlElement("RecordId")]
        public string? RecordId { get; set; } // Unique ID for this employee record

        [XmlElement("CorrectedInd")]
        public string? CorrectedInd { get; set; } // 0 or 1

        [XmlElement("EmployeeName")]
        public PersonNameType? EmployeeName { get; set; }

        [XmlElement("EmployeeSSN")]
        public string? EmployeeSSN { get; set; }

        [XmlElement("EmployeeAddress")]
        public AddressType? EmployeeAddress { get; set; }

        // Part II: Offer & Coverage
        [XmlElement("CoveredIndividual")]
        public List<CoveredIndividualType>? CoveredIndividuals { get; set; } // Part III

        [XmlElement("EmployeeOfferAndCoverage")]
        public EmployeeOfferAndCoverageType? EmployeeOfferAndCoverage { get; set; }
    }

    // --- HELPER TYPES ---

    public class BusinessNameType
    {
        [XmlElement("BusinessNameLine1")]
        public string? BusinessNameLine1 { get; set; }
    }

    public class PersonNameType
    {
        [XmlElement("FirstNm")]
        public string? FirstNm { get; set; }
        [XmlElement("LastNm")]
        public string? LastNm { get; set; }
        [XmlElement("MiddleNm")]
        public string? MiddleNm { get; set; }
    }

    public class AddressType
    {
        [XmlElement("AddressLine1Txt")]
        public string? AddressLine1Txt { get; set; }
        [XmlElement("CityNm")]
        public string? CityNm { get; set; }
        [XmlElement("USStateCd")]
        public string? USStateCd { get; set; }
        [XmlElement("USZIPCd")]
        public string? USZIPCd { get; set; }
    }

    public class AleMemberInformationType
    {
        [XmlElement("Total1095CAttachedCnt")]
        public int Total1095CAttachedCnt { get; set; }

        [XmlElement("AuthoritativeTransmittalInd")]
        public string? AuthoritativeTransmittalInd { get; set; } // 1=Yes

        [XmlElement("Total1095CFiledCnt")]
        public int Total1095CFiledCnt { get; set; }

        [XmlElement("AleMemberGroupInd")]
        public string? AleMemberGroupInd { get; set; } // 0 or 1

        [XmlElement("QualifyingOfferMethodInd")]
        public string? QualifyingOfferMethodInd { get; set; }

        [XmlElement("Section4980HReliefInd")]
        public string? Section4980HReliefInd { get; set; }

        [XmlElement("NinetyEightPercentOfferMethodInd")]
        public string? NinetyEightPercentOfferMethodInd { get; set; }
    }

    public class AleMemberInformationMonthlyType
    {
        [XmlElement("AleMemberMonthlyGrp")]
        public List<AleMemberMonthlyGrp>? MonthlyGroups { get; set; }
    }

    public class AleMemberMonthlyGrp
    {
        [XmlElement("MonthId")]
        public string? MonthId { get; set; } // "01" ... "12"

        [XmlElement("MinEssentialCvrgOffrInd")]
        public string? MinEssentialCvrgOffrInd { get; set; } // 1 or 0

        [XmlElement("FullTimeEmployeeCnt")]
        public int FullTimeEmployeeCnt { get; set; }

        [XmlElement("TotalEmployeeCnt")]
        public int TotalEmployeeCnt { get; set; }

        [XmlElement("AggregatedGroupInd")]
        public string? AggregatedGroupInd { get; set; } // 1 or 0
    }

    public class EmployeeOfferAndCoverageType
    {
        [XmlElement("EmployeeOfferMethod")]
        public List<MonthlyOfferCoverageGroup>? MonthlyOfferCoverage { get; set; }
    }

    public class MonthlyOfferCoverageGroup
    {
        [XmlElement("MonthId")]
        public string? MonthId { get; set; } // "01" ... "12" or "00" for All

        [XmlElement("OfferOfCoverageCd")]
        public string? OfferOfCoverageCd { get; set; } // 1A ... 1U (1L-1U = ICHRA)

        [XmlElement("EmployeeRequiredContriAmt")]
        public string? EmployeeRequiredContriAmt { get; set; } // 0.00
       
        [XmlElement("SafeHarborCd")]
        public string? SafeHarborCd { get; set; } // 2A ... 2F
        // ICHRA (codes 1L-1U): employee age used for affordability, and the
       // Line 17 ZIP code (residence or work-site). Emitted only for ICHRA rows.
       
        [XmlElement("AgeNum")]
        public string? AgeNum { get; set; }

        [XmlElement("ZipCd")]
        public string? ZipCd { get; set; }
    }

    public class CoveredIndividualType
    {
        [XmlElement("PersonName")]
        public PersonNameType? PersonName { get; set; }

        [XmlElement("SSN")]
        public string? SSN { get; set; }

        [XmlElement("BirthDt")]
        public string? BirthDt { get; set; }

        [XmlElement("CoveredIndicator")]
        public string? CoveredIndicator { get; set; } // 1 if covered all 12 months

        // If not all 12 months, list specific months
        [XmlElement("JanInd")] public string? JanInd { get; set; }
        [XmlElement("FebInd")] public string? FebInd { get; set; }
        [XmlElement("MarInd")] public string? MarInd { get; set; }
        [XmlElement("AprInd")] public string? AprInd { get; set; }
        [XmlElement("MayInd")] public string? MayInd { get; set; }
        [XmlElement("JunInd")] public string? JunInd { get; set; }
        [XmlElement("JulInd")] public string? JulInd { get; set; }
        [XmlElement("AugInd")] public string? AugInd { get; set; }
        [XmlElement("SepInd")] public string? SepInd { get; set; }
        [XmlElement("OctInd")] public string? OctInd { get; set; }
        [XmlElement("NovInd")] public string? NovInd { get; set; }
        [XmlElement("DecInd")] public string? DecInd { get; set; }
    }
}
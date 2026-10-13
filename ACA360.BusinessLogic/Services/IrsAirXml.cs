using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using System.Xml.Schema;

namespace ACA360.BusinessLogic.Services
{
    // ---- What goes into the IRS files (filled from the database by IrsXmlService) ----

    public sealed record IrsAddress(string? Line1, string? Line2, string? City, string? State, string? Zip);

    public sealed record IrsEmployer(string? Name, string? Ein, IrsAddress Address, string? ContactName, string? Phone);

    public sealed record IrsCoveredPerson(
        string? FirstName, string? MiddleName, string? LastName, string? Suffix,
        string? Ssn, DateTime? BirthDate, bool AllYear, bool[] Months);

    /// <summary>
    /// The 1095 a correction corrects (IRS Publication 5165, 7.1.3): its identifier as the IRS
    /// knows it (ReceiptId|SubmissionId|RecordId) and the recipient's name and TIN as filed on it.
    /// </summary>
    public sealed record IrsCorrectedRecord(string UniqueRecordId, string? FirstName, string? LastName, string? Tin);

    /// <summary>
    /// The 1094-C authoritative transmittal a correction corrects (7.1.2): its identifier
    /// (ReceiptId|SubmissionId) and the employer name and EIN as filed on it.
    /// </summary>
    public sealed record IrsCorrectedSubmission(string UniqueSubmissionId, string? PayerName, string? PayerEin);

    /// <summary>
    /// One 1095-C. Month arrays are January..December (12 entries). RecordId numbers the forms
    /// 1, 2, 3... within the file (IRS Publication 5165, 3.2); EmployeeId is only for messages.
    /// BirthDate gives the age on January 1 that ICHRA codes 1L-1S need. Corrects is set on
    /// corrections.
    /// </summary>
    public sealed record Irs1095C(
        int RecordId, string? FirstName, string? LastName, string? Ssn, IrsAddress Address, int? PlanStartMonth,
        string? AnnualOffer, string?[] MonthlyOffer,
        decimal? AnnualContribution, decimal?[] MonthlyContribution,
        string? AnnualSafeHarbor, string?[] MonthlySafeHarbor,
        string? AnnualIchraZip, string?[] MonthlyIchraZip,
        IReadOnlyList<IrsCoveredPerson> Covered,
        int EmployeeId = 0, DateTime? BirthDate = null, IrsCorrectedRecord? Corrects = null);

    /// <summary>The 1094-C. Month arrays are January..December (12 entries).</summary>
    public sealed record Irs1094C(
        int TaxYear, IrsEmployer Employer, bool Authoritative, bool IsCorrection,
        bool AggregatedGroup, bool QualifyingOfferMethod, bool NinetyEightPercentOfferMethod,
        bool[] MinimumEssentialCoverageOffered, int[] FullTimeCount, int[] TotalCount, bool[] AggregatedGroupMonths,
        IReadOnlyList<(string? Name, string? Ein)> OtherMembers,
        IReadOnlyList<Irs1095C> Forms,
        IrsCorrectedSubmission? Corrects = null);

    /// <summary>One 1095-B; RecordId, EmployeeId and Corrects as for <see cref="Irs1095C"/>.</summary>
    public sealed record Irs1095B(
        int RecordId, string? FirstName, string? LastName, string? Ssn, IrsAddress Address,
        IReadOnlyList<IrsCoveredPerson> Covered,
        int EmployeeId = 0, IrsCorrectedRecord? Corrects = null);

    public sealed record Irs1094B(int TaxYear, IrsEmployer Employer, bool IsCorrection, IReadOnlyList<Irs1095B> Forms);

    public sealed record IrsManifest(
        int TaxYear, bool PriorYearData, bool IsCorrection, bool TestFile, bool BForms,
        string? TransmitterEin, string? TransmitterName, IrsAddress TransmitterAddress,
        string? ContactName, string? ContactPhone, string? SoftwareId, string? Tcc,
        Guid TransmissionId, int PayeeCount, string Checksum, long ByteSize, string FileName, DateTime TimestampUtc,
        string? OriginalReceiptId = null);

    /// <summary>The data cannot be filed as it is; <see cref="Exception.Message"/> lists what to fix.</summary>
    public sealed class IrsDataException : InvalidOperationException
    {
        public IReadOnlyList<string> Problems { get; }

        public IrsDataException(string what, IReadOnlyList<string> problems)
            : base($"{what}: {problems.Count} problem(s) to fix before the IRS files can be generated.{Environment.NewLine}"
                   + string.Join(Environment.NewLine, problems.Take(25).Select(p => " - " + p))
                   + (problems.Count > 25 ? $"{Environment.NewLine} - ... and {problems.Count - 25} more." : ""))
            => Problems = problems;
    }

    /// <summary>
    /// Builds the IRS AIR (UI channel) files - the 1094-C/1095-C or 1094-B/1095-B form data file
    /// and its manifest - in the IRS format of the forms' tax year (IrsSchemas/TY{year}, from
    /// irs.gov), and checks each one against that year's schemas. Element names, order and
    /// namespaces follow the schemas and the IRS sample files; text is cut down to the characters
    /// the IRS accepts (<see cref="IrsText"/>). Record and transmission identifiers follow IRS
    /// Publication 5165, section 3.2.
    /// </summary>
    public static class IrsAirXml
    {
        public static readonly XNamespace Irs = "urn:us:gov:treasury:irs:common";
        public static readonly XNamespace MsgC = "urn:us:gov:treasury:irs:msg:form1094-1095Ctransmitterupstreammessage";
        public static readonly XNamespace MsgB = "urn:us:gov:treasury:irs:msg:form1094-1095Btransmitterupstreammessage";
        public static readonly XNamespace UiHeader = "urn:us:gov:treasury:irs:msg:acauibusinessheader";
        public static readonly XNamespace BusinessHeader = "urn:us:gov:treasury:irs:msg:acabusinessheader";

        /// <summary>
        /// The form elements are built in this stand-in namespace and moved into the tax year's own
        /// (urn:us:gov:treasury:irs:ext:aca:air:ty25 for 2025, ty24 for 2024, ...) once built.
        /// </summary>
        private static readonly XNamespace Air = "urn:aca360:irs:air:tax-year";

        /// <summary>Where the schema folders (TY2023, TY2024, ...) are deployed.</summary>
        public static string SchemaRoot => Path.Combine(AppContext.BaseDirectory, "IrsSchemas");

        /// <summary>One tax year's IRS schemas: its folder, its AIR namespace and the compiled schema set.</summary>
        private sealed record YearSchemas(int TaxYear, string Folder, XNamespace Namespace, Lazy<XmlSchemaSet> Set);

        private static readonly ConcurrentDictionary<int, YearSchemas?> Years = new();

        private static YearSchemas? FindYear(int taxYear) => Years.GetOrAdd(taxYear, year =>
        {
            var folder = Path.Combine(SchemaRoot, $"TY{year}");
            var ext = Directory.Exists(Path.Combine(folder, "EXT")) ? Directory.GetFiles(Path.Combine(folder, "EXT"), "*.xsd").FirstOrDefault() : null;
            if (ext == null) return null;
            // The year's AIR namespace is the target namespace of its EXT schema.
            var ns = XDocument.Load(ext).Root?.Attribute("targetNamespace")?.Value;
            if (string.IsNullOrEmpty(ns)) return null;
            return new YearSchemas(year, folder, ns, new Lazy<XmlSchemaSet>(() => LoadSchemas(folder)));
        });

        private static YearSchemas Year(int taxYear) =>
            FindYear(taxYear) ?? throw new IrsDataException($"IRS files for tax year {taxYear}", new[]
            {
                $"The IRS schemas for tax year {taxYear} are not in the app (IrsSchemas/TY{taxYear}). The IRS publishes them for each tax year; add them as described in IrsSchemas/README.md."
            });

        /// <summary>The AIR namespace of a tax year's IRS files, e.g. ...:air:ty25 for 2025.</summary>
        public static XNamespace AirNamespace(int taxYear) => Year(taxYear).Namespace;

        /// <summary>The tax years whose IRS schemas are in the app.</summary>
        public static IReadOnlyList<int> AvailableTaxYears() =>
            Directory.Exists(SchemaRoot)
                ? Directory.GetDirectories(SchemaRoot, "TY*")
                    .Select(d => int.TryParse(Path.GetFileName(d)[2..], out var y) ? y : 0)
                    .Where(y => y > 0 && FindYear(y) != null).OrderBy(y => y).ToList()
                : Array.Empty<int>();

        /// <summary>Moves the built elements into the tax year's AIR namespace.</summary>
        private static XDocument ForYear(XDocument doc, int taxYear)
        {
            var ns = Year(taxYear).Namespace;
            foreach (var e in doc.Descendants().Where(e => e.Name.Namespace == Air).ToList())
                e.Name = ns + e.Name.LocalName;
            var prefix = "air" + ns.NamespaceName.Split(':').Last();   // airty25, as in the IRS samples
            doc.Root!.Attributes().Where(a => a.IsNamespaceDeclaration && a.Value == Air.NamespaceName).Remove();
            doc.Root.Add(new XAttribute(XNamespace.Xmlns + prefix, ns.NamespaceName));
            return doc;
        }

        private static readonly string[] MonthAbbr = { "Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec" };
        private static readonly string[] MonthFull = CultureInfo.InvariantCulture.DateTimeFormat.MonthNames.Take(12).ToArray();
        // The 1094-C monthly groups spell September "Sept".
        private static readonly string[] AleMonth = { "Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sept", "Oct", "Nov", "Dec" };

        // ---- 1094-C / 1095-C ----

        public static XDocument Build1094C(Irs1094C data)
        {
            Year(data.TaxYear);   // stops first when the year has no schemas
            var problems = new List<string>();
            var detail = new XElement(Air + "Form1094CUpstreamDetail",
                new XAttribute("recordType", ""), new XAttribute("lineNum", "0"),
                A("SubmissionId", "1"),   // one 1094-C per file
                A("TaxYr", data.TaxYear),
                A("CorrectedInd", data.Corrects != null ? "1" : "0"),
                CorrectedSubmission(data.Corrects),
                EmployerInformation(data.Employer, problems),
                A("Form1095CAttachedCnt", data.Forms.Count),
                A("AuthoritativeTransmittalInd", data.Authoritative ? "1" : "0"));

            // Parts II-IV belong to the authoritative transmittal only.
            if (data.Authoritative)
            {
                if (data.Forms.Count > 0) detail.Add(A("TotalForm1095CALEMemberCnt", data.Forms.Count));
                detail.Add(A("AggregatedGroupMemberCd", data.AggregatedGroup ? "1" : "2"));   // 1 = Yes, 2 = No
                if (data.QualifyingOfferMethod) detail.Add(A("QualifyingOfferMethodInd", "1"));
                if (data.NinetyEightPercentOfferMethod) detail.Add(A("NinetyEightPctOfferMethodInd", "1"));

                var months = new XElement(Air + "ALEMemberInformationGrp");
                for (int m = 0; m < 12; m++)
                    months.Add(new XElement(Air + (AleMonth[m] + "ALEMonthlyInfoGrp"),
                        A("MinEssentialCvrOffrCd", data.MinimumEssentialCoverageOffered[m] ? "1" : "2"),
                        A("ALEMemberFTECnt", Math.Max(0, data.FullTimeCount[m])),
                        A("TotalEmployeeCnt", Math.Max(0, data.TotalCount[m])),
                        A("AggregatedGroupInd", data.AggregatedGroupMonths[m] ? "1" : "0")));
                detail.Add(months);

                var ownEin = IrsText.NineDigits(data.Employer.Ein);
                foreach (var (name, ein) in data.OtherMembers
                             .Select(o => (Name: IrsText.BusinessName(o.Name), Ein: IrsText.NineDigits(o.Ein)))
                             .Where(o => o.Ein != null && o.Ein != ownEin)
                             .DistinctBy(o => o.Ein).Take(99))
                    detail.Add(new XElement(Air + "OtherALEMembersGrp",
                        name == null ? null : new XElement(Air + "BusinessName", A("BusinessNameLine1Txt", name)),
                        I("EIN", ein)));
            }

            // 7.1.2: a corrected 1094-C is the complete authoritative transmittal, sent alone.
            if (data.Corrects != null && (!data.Authoritative || data.Forms.Count > 0))
                problems.Add("A corrected Form 1094-C must be the authoritative transmittal and carry no Forms 1095-C.");
            CheckRecordIds(data.Forms.Select(f => f.RecordId), problems);
            foreach (var form in data.Forms)
                detail.Add(Build1095C(form, data.TaxYear, data.IsCorrection, data.Employer.Phone, problems));

            ThrowIfAny("1094-C / 1095-C", problems);
            return ForYear(new XDocument(new XDeclaration("1.0", "UTF-8", null),
                new XElement(MsgC + "Form109495CTransmittalUpstream", Namespaces(MsgC), detail)), data.TaxYear);
        }

        private static XElement Build1095C(Irs1095C f, int taxYear, bool isCorrection, string? employerPhone, List<string> problems)
        {
            string who = Who(f.EmployeeId, f.RecordId, f.FirstName, f.LastName, f.Ssn);
            var name = PersonName("OtherCompletePersonName", f.FirstName, null, f.LastName, null, who, problems);

            // Age on January 1, only reported with an individual coverage HRA code 1L-1S.
            int? age = null;
            if (UsesIchraAgeCode(f))
            {
                if (f.BirthDate is { } born && AgeOnJanuary1(born, taxYear) is >= 1 and <= 120 and var a) age = a;
                else problems.Add($"{who} has an ICHRA code (1L-1S), which needs the employee's age on January 1: add a valid date of birth.");
            }

            if (f.PlanStartMonth is null or < 0 or > 12)
                problems.Add($"{who} has no plan start month (required on every 1095-C since 2020; 00 when there is no plan). Set it before generating.");

            var info = new XElement(Air + "EmployeeInfoGrp", name,
                I("TINRequestTypeCd", "INDIVIDUAL_TIN"),
                IrsText.NineDigits(f.Ssn) is { } ssn ? I("SSN", ssn) : null,
                MailingAddress(f.Address));

            var phone = IrsText.Phone(employerPhone);
            return new XElement(Air + "Form1095CUpstreamDetail",
                new XAttribute("recordType", ""), new XAttribute("lineNum", "0"),
                A("RecordId", f.RecordId),
                A("CorrectedInd", isCorrection ? "1" : "0"),
                CorrectedRecord(isCorrection, f.Corrects, f.FirstName, f.LastName, f.Ssn, who, problems),
                A("TaxYr", taxYear),
                info,
                phone == null ? null : A("ALEContactPhoneNum", phone),
                A("StartMonthNumberCd", (f.PlanStartMonth ?? 0).ToString("00", CultureInfo.InvariantCulture)),
                age == null ? null : A("AgeNum", age),
                OfferAndCoverage(f, who, problems),
                f.Covered.Count > 0 ? A("CoveredIndividualInd", "1") : null,
                f.Covered.Select(c => CoveredIndividual(c, who, problems)));
        }

        private static XElement OfferAndCoverage(Irs1095C f, string who, List<string> problems)
        {
            var grp = new XElement(Air + "EmployeeOfferAndCoverageGrp");

            // Line 14 is required for all twelve months. A missing code is a data fault: defaulting
            // to 1H would file "no offer made", the pattern the IRS assesses a 4980H(a) penalty on.
            if (!string.IsNullOrWhiteSpace(f.AnnualOffer))
                grp.Add(A("AnnualOfferOfCoverageCd", f.AnnualOffer.Trim().ToUpperInvariant()));
            else
            {
                var missing = Enumerable.Range(0, 12).Where(m => string.IsNullOrWhiteSpace(f.MonthlyOffer[m])).Select(m => MonthAbbr[m]).ToList();
                if (missing.Count > 0)
                    problems.Add($"{who} has no line 14 offer-of-coverage code for {string.Join(", ", missing)}. Regenerate codes for this employer, or unlock the employee record if it is locked.");
                grp.Add(new XElement(Air + "MonthlyOfferCoverageGrp",
                    Enumerable.Range(0, 12).Where(m => !string.IsNullOrWhiteSpace(f.MonthlyOffer[m]))
                        .Select(m => A(MonthAbbr[m] + "OfferCd", f.MonthlyOffer[m]!.Trim().ToUpperInvariant()))));
            }

            // Line 15: only reported for the codes that need it, so empty months are left out.
            if (f.AnnualContribution is { } annual)
                grp.Add(A("AnnlEmployeeRequiredContriAmt", Amount(annual, who, problems)));
            else if (f.MonthlyContribution.Any(a => a.HasValue))
                grp.Add(new XElement(Air + "MonthlyEmployeeRequiredContriGrp",
                    Enumerable.Range(0, 12).Where(m => f.MonthlyContribution[m].HasValue)
                        .Select(m => A(MonthFull[m] + "Amt", Amount(f.MonthlyContribution[m]!.Value, who, problems)))));

            // Line 16
            if (!string.IsNullOrWhiteSpace(f.AnnualSafeHarbor))
                grp.Add(A("AnnualSafeHarborCd", f.AnnualSafeHarbor.Trim()));
            else if (f.MonthlySafeHarbor.Any(s => !string.IsNullOrWhiteSpace(s)))
                grp.Add(new XElement(Air + "MonthlySafeHarborGrp",
                    Enumerable.Range(0, 12).Where(m => !string.IsNullOrWhiteSpace(f.MonthlySafeHarbor[m]))
                        .Select(m => A(MonthAbbr[m] + "SafeHarborCd", f.MonthlySafeHarbor[m]!.Trim()))));

            // Line 17 (ICHRA ZIP code), only when present.
            if (IrsText.Zip5(f.AnnualIchraZip) is { } zip)
                grp.Add(A("AnnualICHRAZipCd", zip));
            else if (f.MonthlyIchraZip.Any(z => IrsText.Zip5(z) != null))
                grp.Add(new XElement(Air + "MonthlyICHRAZipCdGrp",
                    Enumerable.Range(0, 12).Where(m => IrsText.Zip5(f.MonthlyIchraZip[m]) != null)
                        .Select(m => A(MonthAbbr[m] + "ICHRAZipCd", IrsText.Zip5(f.MonthlyIchraZip[m])!))));

            return grp;
        }

        // ---- 1094-B / 1095-B ----

        public static XDocument Build1094B(Irs1094B data)
        {
            Year(data.TaxYear);
            var problems = new List<string>();
            var employer = data.Employer;
            var name = IrsText.BusinessName(employer.Name);
            var ein = IrsText.NineDigits(employer.Ein);
            if (name == null) problems.Add("The employer has no name the IRS can accept.");
            if (ein == null) problems.Add($"The employer's EIN \"{employer.Ein}\" is not 9 digits.");
            if (data.Forms.Count == 0) problems.Add("There are no employees to report (a 1094-B needs at least one 1095-B).");

            var detail = new XElement(Air + "Form1094BUpstreamDetail",
                new XAttribute("recordType", ""), new XAttribute("lineNum", "0"),
                A("SubmissionId", "1"),
                A("TaxYr", data.TaxYear),
                new XElement(Air + "BusinessName", A("BusinessNameLine1Txt", name ?? "")),
                I("TINRequestTypeCd", "BUSINESS_TIN"),
                I("EmployerEIN", ein ?? ""),
                ContactName(employer.ContactName),
                IrsText.Phone(employer.Phone) is { } phone ? A("ContactPhoneNum", phone) : null,
                MailingAddress(employer.Address),
                data.Forms.Count > 0 ? A("Form1095BAttachedCnt", data.Forms.Count) : null);

            CheckRecordIds(data.Forms.Select(f => f.RecordId), problems);
            foreach (var f in data.Forms)
            {
                string who = Who(f.EmployeeId, f.RecordId, f.FirstName, f.LastName, f.Ssn);
                var ssn = IrsText.NineDigits(f.Ssn);
                if (ssn == null) problems.Add($"{who} has no valid SSN (the responsible individual needs one, or a date of birth).");
                // A 1095-B reports who was covered: the IRS schema needs at least one covered individual.
                if (f.Covered.Count == 0) problems.Add($"{who} has no covered individuals (Part IV lists everyone covered, the employee included).");

                // The filer of a 1094-B is the coverage provider: a self-insured employer, so it is
                // both the sponsoring employer (Part II) and the issuer (Part III).
                detail.Add(new XElement(Air + "Form1095BUpstreamDetail",
                    new XAttribute("recordType", ""), new XAttribute("lineNum", "0"),
                    A("RecordId", f.RecordId),
                    A("CorrectedInd", data.IsCorrection ? "1" : "0"),
                    CorrectedRecord(data.IsCorrection, f.Corrects, f.FirstName, f.LastName, f.Ssn, who, problems),
                    A("TaxYr", data.TaxYear),
                    new XElement(Air + "ResponsibleIndividualGrp",
                        PersonName("ResponsibleIndividualName", f.FirstName, null, f.LastName, null, who, problems),
                        I("TINRequestTypeCd", "INDIVIDUAL_TIN"),
                        A("TIN", ssn ?? ""),
                        MailingAddress(f.Address),
                        A("HealthCoverageOriginCd", "B")),   // B = employer-sponsored coverage
                    new XElement(Air + "SponsoringEmployerInfoGrp",
                        new XElement(Air + "BusinessName", A("BusinessNameLine1Txt", name ?? "")),
                        I("EIN", ein ?? ""),
                        I("TINRequestTypeCd", "BUSINESS_TIN"),
                        MailingAddress(employer.Address)),
                    new XElement(Air + "IssuerInfoGrp",
                        new XElement(Air + "BusinessName", A("BusinessNameLine1Txt", name ?? "")),
                        I("TINRequestTypeCd", "BUSINESS_TIN"),
                        I("EIN", ein ?? ""),
                        IrsText.Phone(employer.Phone) is { } p ? A("ContactPhoneNum", p) : null,
                        MailingAddress(employer.Address)),
                    f.Covered.Select(c => CoveredIndividual(c, who, problems))));
            }

            ThrowIfAny("1094-B / 1095-B", problems);
            return ForYear(new XDocument(new XDeclaration("1.0", "UTF-8", null),
                new XElement(MsgB + "Form109495BTransmittalUpstream", Namespaces(MsgB), detail)), data.TaxYear);
        }

        // ---- Manifest ----

        /// <summary>An IRS Transmitter Control Code: five characters, two consonants then three consonants or digits.</summary>
        private static readonly Regex Tcc = new("^[B-DF-HJ-NP-TV-Z]{2}[B-DF-HJ-NP-TV-Z0-9]{3}$", RegexOptions.Compiled);

        /// <summary>
        /// Stops with what to fix when the transmitter settings (dbo.TransmitterConfig) cannot make
        /// a manifest the IRS accepts. Checked before any file is written.
        /// </summary>
        public static void CheckTransmitter(string? tcc, string? ein, string? companyName, IrsAddress address,
            string? contactName, string? contactPhone, string? softwareId)
        {
            var problems = new List<string>();
            if (!Tcc.IsMatch((tcc ?? "").Trim()))
                problems.Add($"The TCC \"{tcc}\" is not an IRS Transmitter Control Code: 5 characters, two consonants then three consonants or digits, e.g. BB000 (TransmitterConfig.TCC).");
            if (IrsText.NineDigits(ein) == null) problems.Add($"The transmitter EIN \"{ein}\" is not 9 digits (TransmitterConfig.TransmitterEIN).");
            if (IrsText.BusinessName(companyName) == null) problems.Add("The transmitter has no company name (TransmitterConfig.CompanyName).");
            if (MailingAddress(address) == null) problems.Add("The transmitter has no complete mailing address (TransmitterConfig.AddressLine1, City, State, ZipCode; script 048).");
            if (ContactName(contactName) == null) problems.Add("The transmitter contact needs a first and a last name (TransmitterConfig.ContactName).");
            if (IrsText.Phone(contactPhone) == null) problems.Add("The transmitter contact phone needs at least 10 digits (TransmitterConfig.ContactPhone).");
            if (string.IsNullOrWhiteSpace(softwareId) || softwareId.Trim().Length > 10 || softwareId.Contains('#'))
                problems.Add("The transmitter software ID must be 1 to 10 characters (TransmitterConfig.SoftwareId).");
            ThrowIfAny("IRS transmitter settings", problems);
        }

        public static XDocument BuildManifest(IrsManifest m)
        {
            Year(m.TaxYear);
            CheckTransmitter(m.Tcc, m.TransmitterEin, m.TransmitterName, m.TransmitterAddress, m.ContactName, m.ContactPhone, m.SoftwareId);
            var ein = IrsText.NineDigits(m.TransmitterEin);
            var name = IrsText.BusinessName(m.TransmitterName);
            var address = MailingAddress(m.TransmitterAddress);
            var contact = ContactName(m.ContactName);
            var phone = IrsText.Phone(m.ContactPhone);

            var root = new XElement(UiHeader + "ACAUIBusinessHeader",
                new XAttribute(XNamespace.Xmlns + "n1", UiHeader.NamespaceName),
                new XAttribute(XNamespace.Xmlns + "acabushdr", BusinessHeader.NamespaceName),
                new XAttribute(XNamespace.Xmlns + "airty25", Air.NamespaceName),
                new XAttribute(XNamespace.Xmlns + "irs", Irs.NamespaceName),
                new XElement(BusinessHeader + "ACABusinessHeader",
                    A("UniqueTransmissionId", UniqueTransmissionId(m.TransmissionId, m.Tcc!)),
                    I("Timestamp", m.TimestampUtc.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture))),
                new XElement(Air + "ACATransmitterManifestReqDtl",
                    A("PaymentYr", m.TaxYear),   // the tax year reported, not the year of transmission
                    A("PriorYearDataInd", m.PriorYearData ? "1" : "0"),
                    I("EIN", ein),
                    // O original, C correction, R replacement of a rejected original (Pub 5165, 7.3.1).
                    A("TransmissionTypeCd", m.IsCorrection ? "C" : m.OriginalReceiptId != null ? "R" : "O"),
                    A("TestFileCd", m.TestFile ? "T" : "P"),
                    !m.IsCorrection && m.OriginalReceiptId != null ? A("OriginalReceiptId", m.OriginalReceiptId.Trim()) : null,
                    A("TransmitterForeignEntityInd", "0"),
                    new XElement(Air + "TransmitterNameGrp", A("BusinessNameLine1Txt", name)),
                    new XElement(Air + "CompanyInformationGrp",
                        A("CompanyNm", name), address, contact, A("ContactPhoneNum", phone)),
                    new XElement(Air + "VendorInformationGrp",
                        A("VendorCd", "I"),   // I = in-house software, V = vendor software
                        ContactName(m.ContactName), A("ContactPhoneNum", phone)),
                    A("TotalPayeeRecordCnt", m.PayeeCount),
                    A("TotalPayerRecordCnt", "1"),   // one employer per file
                    A("SoftwareId", m.SoftwareId!.Trim()),
                    A("FormTypeCd", m.BForms ? "1094/1095B" : "1094/1095C"),
                    I("BinaryFormatCd", "application/xml"),
                    I("ChecksumAugmentationNum", m.Checksum),   // SHA-256 of the form data file
                    A("AttachmentByteSizeNum", m.ByteSize),
                    A("DocumentSystemFileNm", m.FileName)));
            return ForYear(new XDocument(new XDeclaration("1.0", "UTF-8", null), root), m.TaxYear);
        }

        /// <summary>
        /// The Unique Transmission ID (UTID), IRS Publication 5165 section 3.2: a UUID, the
        /// application ID SYS12, the TCC, an empty reserved field and the request type T, separated
        /// by colons (e.g. 550e8400-e29b-41d4-a716-446655440000:SYS12:BB002::T). It must be unique for
        /// every transmission, rejected ones included, so every generation uses a new UUID.
        /// </summary>
        public static string UniqueTransmissionId(Guid transmissionId, string tcc) => $"{transmissionId}:SYS12:{tcc.Trim()}::T";

        // ---- Validation and saving ----

        private static XmlSchemaSet LoadSchemas(string folder)
        {
            var set = new XmlSchemaSet { XmlResolver = new XmlUrlResolver() };
            foreach (var file in new[] { "IRS-Form1094-1095CTransmitterUpstreamMessage.xsd", "IRS-Form1094-1095BTransmitterUpstreamMessage.xsd", "IRS-ACAUserInterfaceHeaderMessage.xsd" })
            {
                var path = Path.Combine(folder, "MSG", file);
                if (!File.Exists(path)) throw new FileNotFoundException($"The IRS schema {file} is missing from {folder}.", path);
                using var reader = XmlReader.Create(path, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = new XmlUrlResolver() });
                set.Add(null, reader);
            }
            set.Compile();
            return set;
        }

        private const string AirPrefix = "urn:us:gov:treasury:irs:ext:aca:air:ty";

        /// <summary>
        /// Schema errors in <paramref name="doc"/> (empty when the IRS schema accepts it). The schemas
        /// of the tax year whose AIR namespace the document uses are the ones checked against.
        /// </summary>
        public static List<string> Validate(XDocument doc)
        {
            var airNs = doc.Descendants().Select(e => e.Name.NamespaceName).FirstOrDefault(n => n.StartsWith(AirPrefix, StringComparison.Ordinal));
            if (airNs == null || !int.TryParse(airNs[AirPrefix.Length..], out var yy))
                return new List<string> { "The document uses no IRS AIR tax-year namespace." };
            var year = Year(2000 + yy);
            if (year.Namespace.NamespaceName != airNs)
                return new List<string> { $"The document uses {airNs}, but the TY{year.TaxYear} schemas use {year.Namespace.NamespaceName}." };

            var errors = new List<string>();
            // Round-trip through text so line numbers point into the file as it is saved.
            var xml = ToText(doc);
            var settings = new XmlReaderSettings { ValidationType = ValidationType.Schema, Schemas = year.Set.Value, DtdProcessing = DtdProcessing.Prohibit };
            settings.ValidationEventHandler += (_, e) => errors.Add($"line {e.Exception?.LineNumber}: {e.Message}");
            using var reader = XmlReader.Create(new StringReader(xml), settings);
            while (reader.Read()) { }
            return errors;
        }

        /// <summary>Checks <paramref name="doc"/> against the IRS schema and saves it (UTF-8, no byte-order mark).</summary>
        public static void ValidateAndSave(XDocument doc, string path, string what)
        {
            var errors = Validate(doc);
            if (errors.Count > 0)
                throw new IrsDataException($"The {what} file does not match the IRS schema", errors);
            File.WriteAllText(path, ToText(doc), new UTF8Encoding(false));
        }

        public static string ToText(XDocument doc)
        {
            var sb = new StringBuilder();
            using (var writer = XmlWriter.Create(new StringWriter(sb), new XmlWriterSettings { Indent = true, OmitXmlDeclaration = true }))
                doc.Save(writer);
            return "<?xml version=\"1.0\" encoding=\"UTF-8\"?>" + Environment.NewLine + sb;
        }

        // ---- Pieces ----

        private static string Who(int employeeId, int recordId, string? first, string? last, string? ssn) =>
            $"Employee {(employeeId > 0 ? employeeId : recordId)} ({first} {last}, SSN ending {IrsText.LastFour(ssn)})";

        /// <summary>IRS Publication 5165, 3.2: each 1095 in a submission has a Record ID starting at 1 and going up by 1.</summary>
        private static void CheckRecordIds(IEnumerable<int> recordIds, List<string> problems)
        {
            int expected = 1;
            foreach (var id in recordIds)
            {
                if (id != expected) { problems.Add($"Record IDs must run 1, 2, 3... in the file; found {id} where {expected} was expected."); return; }
                expected++;
            }
        }

        private static readonly HashSet<string> IchraAgeCodes = new(StringComparer.OrdinalIgnoreCase) { "1L", "1M", "1N", "1O", "1P", "1Q", "1R", "1S" };

        private static bool UsesIchraAgeCode(Irs1095C f) =>
            IchraAgeCodes.Contains(f.AnnualOffer?.Trim() ?? "") || f.MonthlyOffer.Any(c => IchraAgeCodes.Contains(c?.Trim() ?? ""));

        /// <summary>Age in whole years on January 1 of <paramref name="taxYear"/>.</summary>
        public static int AgeOnJanuary1(DateTime birthDate, int taxYear)
        {
            var jan1 = new DateTime(taxYear, 1, 1);
            int age = jan1.Year - birthDate.Year;
            if (birthDate.Date > jan1.AddYears(-age)) age--;
            return age;
        }

        /// <summary>
        /// On a correction, which record it corrects (ReceiptId|SubmissionId|RecordId, from the IRS
        /// acknowledgement) and the recipient's name and TIN from that record as filed (7.1.3); the
        /// current ones only when the filed ones were not kept (files made before script 050).
        /// </summary>
        private static XElement? CorrectedRecord(bool isCorrection, IrsCorrectedRecord? corrects, string? first, string? last, string? ssn, string who, List<string> problems)
        {
            if (!isCorrection) return null;
            if (corrects == null || string.IsNullOrWhiteSpace(corrects.UniqueRecordId))
            {
                problems.Add($"{who} cannot be corrected: the record it corrects is not known (process the IRS acknowledgement of the original filing first).");
                return null;
            }
            var filedFirst = IrsText.PersonName(corrects.FirstName) ?? IrsText.PersonName(first);
            var filedLast = IrsText.PersonName(corrects.LastName) ?? IrsText.PersonName(last);
            var tin = IrsText.NineDigits(corrects.Tin) ?? IrsText.NineDigits(ssn);
            return new XElement(Air + "CorrectedRecordRecipientGrp",
                A("CorrectedUniqueRecordId", corrects.UniqueRecordId.Trim()),
                new XElement(Air + "CorrectedRecRecipientPrsnName",
                    A("PersonFirstNm", filedFirst ?? ""),
                    A("PersonLastNm", filedLast ?? "")),
                tin == null ? null : A("CorrectedRecRecipientTIN", tin));
        }

        /// <summary>On a corrected 1094-C: which 1094-C it corrects, with the employer name and EIN as filed on it (7.1.2).</summary>
        private static XElement? CorrectedSubmission(IrsCorrectedSubmission? corrects)
        {
            if (corrects == null) return null;
            var name = IrsText.BusinessName(corrects.PayerName);
            var ein = IrsText.NineDigits(corrects.PayerEin);
            return new XElement(Air + "CorrectedSubmissionInfoGrp",
                A("CorrectedUniqueSubmissionId", corrects.UniqueSubmissionId.Trim()),
                name == null ? null : new XElement(Air + "CorrectedSubmissionPayerName", A("BusinessNameLine1Txt", name)),
                ein == null ? null : A("CorrectedSubmissionPayerTIN", ein));
        }

        private static XElement A(string name, object? value) => new(Air + name, value);
        private static XElement I(string name, object? value) => new(Irs + name, value);

        private static object[] Namespaces(XNamespace message) => new object[]
        {
            new XAttribute(XNamespace.Xmlns + "n1", message.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "airty25", Air.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "irs", Irs.NamespaceName),
        };

        private static void ThrowIfAny(string what, List<string> problems)
        {
            if (problems.Count > 0) throw new IrsDataException(what, problems);
        }

        private static XElement EmployerInformation(IrsEmployer e, List<string> problems)
        {
            var name = IrsText.BusinessName(e.Name);
            var ein = IrsText.NineDigits(e.Ein);
            if (name == null) problems.Add("The employer has no name the IRS can accept.");
            if (ein == null) problems.Add($"The employer's EIN \"{e.Ein}\" is not 9 digits.");
            return new XElement(Air + "EmployerInformationGrp",
                new XElement(Air + "BusinessName", A("BusinessNameLine1Txt", name ?? "")),
                I("TINRequestTypeCd", "BUSINESS_TIN"),
                I("EmployerEIN", ein ?? ""),
                MailingAddress(e.Address),
                ContactName(e.ContactName),
                IrsText.Phone(e.Phone) is { } phone ? A("ContactPhoneNum", phone) : null);
        }

        /// <summary>The US address, or nothing when a required part is missing (the element is optional; a partial one is rejected).</summary>
        private static XElement? MailingAddress(IrsAddress a)
        {
            var line1 = IrsText.Street(a.Line1);
            var city = IrsText.City(a.City);
            var state = IrsText.State(a.State);
            var zip = IrsText.Zip5(a.Zip);
            if (line1 == null || city == null || state == null || zip == null) return null;
            var line2 = IrsText.Street(a.Line2);
            var zip4 = IrsText.Zip4(a.Zip);
            return new XElement(Air + "MailingAddressGrp",
                new XElement(Air + "USAddressGrp",
                    A("AddressLine1Txt", line1),
                    line2 == null ? null : A("AddressLine2Txt", line2),
                    I("CityNm", city),
                    A("USStateCd", state),
                    I("USZIPCd", zip),
                    zip4 == null ? null : I("USZIPExtensionCd", zip4)));
        }

        /// <summary>First and last name from a single contact field; nothing when it is not two words.</summary>
        private static XElement? ContactName(string? contact)
        {
            var words = (contact ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (words.Length < 2) return null;
            var first = IrsText.PersonName(words[0]);
            var last = IrsText.PersonName(words[^1]);
            if (first == null || last == null) return null;
            return new XElement(Air + "ContactNameGrp", A("PersonFirstNm", first), A("PersonLastNm", last));
        }

        private static XElement PersonName(string element, string? first, string? middle, string? last, string? suffix, string who, List<string> problems)
        {
            var f = IrsText.PersonName(first);
            var l = IrsText.PersonName(last);
            if (f == null || l == null) problems.Add($"{who} needs a first and a last name the IRS can accept (letters, hyphens and spaces).");
            var m = IrsText.PersonName(middle);
            var s = IrsText.PersonName(suffix);
            return new XElement(Air + element,
                A("PersonFirstNm", f ?? ""),
                m == null ? null : A("PersonMiddleNm", m),
                A("PersonLastNm", l ?? ""),
                s == null ? null : A("SuffixNm", s));
        }

        private static XElement CoveredIndividual(IrsCoveredPerson c, string employee, List<string> problems)
        {
            string who = $"Covered individual {c.FirstName} {c.LastName} of {employee}";
            var ssn = IrsText.NineDigits(c.Ssn);
            if (ssn == null && c.BirthDate == null) problems.Add($"{who} needs an SSN or a date of birth.");
            return new XElement(Air + "CoveredIndividualGrp",
                PersonName("CoveredIndividualName", c.FirstName, c.MiddleName, c.LastName, c.Suffix, who, problems),
                I("TINRequestTypeCd", "INDIVIDUAL_TIN"),
                ssn != null ? I("SSN", ssn) : A("BirthDt", (c.BirthDate ?? DateTime.MinValue).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
                c.AllYear
                    ? A("CoveredIndividualAnnualInd", "1")
                    : new XElement(Air + "CoveredIndividualMonthlyIndGrp",
                        Enumerable.Range(0, 12).Select(m => A(MonthFull[m] + "Ind", c.Months.Length > m && c.Months[m] ? "1" : "0"))));
        }

        private static string Amount(decimal value, string who, List<string> problems)
        {
            if (value < 0) problems.Add($"{who} has a negative line 15 amount ({value}).");
            // Invariant culture: a comma decimal separator would be rejected.
            return Math.Abs(value).ToString("0.00", CultureInfo.InvariantCulture);
        }
    }

    /// <summary>
    /// Cuts text down to what the IRS schema accepts. The IRS publishes these rules as
    /// patterns; anything outside them makes the whole file fail, so names like
    /// "Smith &amp; Sons, Inc." or "O'Brien" must be cleaned before they are written.
    /// </summary>
    public static class IrsText
    {
        /// <summary>Business name line: letters, digits, - ( ) &amp; ' and single spaces; 75 characters.</summary>
        public static string? BusinessName(string? value) =>
            Clean(value, allowed: c => char.IsAsciiLetterOrDigit(c) || c is '-' or '(' or ')' or '&' or '\'', drop: ".,", max: 75);

        /// <summary>Person name: letters, hyphens and single spaces; 20 characters (apostrophes and periods are dropped).</summary>
        public static string? PersonName(string? value) =>
            Clean(value, allowed: c => char.IsAsciiLetter(c) || c == '-', drop: "'.,", max: 20);

        /// <summary>Street address: starts with a letter or digit; letters, digits, - / and single spaces; 35 characters.</summary>
        public static string? Street(string? value)
        {
            var s = Clean(value, allowed: c => char.IsAsciiLetterOrDigit(c) || c is '-' or '/', drop: ".,#'", max: 35);
            if (s == null) return null;
            s = s.TrimStart('-', '/', ' ');
            return s.Length == 0 ? null : s;
        }

        /// <summary>City: letters and single spaces; 22 characters.</summary>
        public static string? City(string? value) => Clean(value, allowed: char.IsAsciiLetter, drop: ".'", max: 22);

        /// <summary>Two-letter state code, upper case.</summary>
        public static string? State(string? value)
        {
            var s = (value ?? "").Trim().ToUpperInvariant();
            return s.Length == 2 && s.All(char.IsAsciiLetterUpper) ? s : null;
        }

        public static string? Zip5(string? value)
        {
            var d = Digits(value);
            return d.Length is 5 or 9 ? d[..5] : null;
        }

        public static string? Zip4(string? value)
        {
            var d = Digits(value);
            return d.Length == 9 ? d[5..] : null;
        }

        /// <summary>Phone: digits only, 10 to 30 of them.</summary>
        public static string? Phone(string? value)
        {
            var d = Digits(value);
            return d.Length is >= 10 and <= 30 ? d : null;
        }

        /// <summary>An SSN or EIN: exactly nine digits, punctuation removed.</summary>
        public static string? NineDigits(string? value)
        {
            var d = Digits(value);
            return d.Length == 9 ? d : null;
        }

        public static string LastFour(string? value)
        {
            var d = Digits(value);
            return d.Length >= 4 ? d[^4..] : "unknown";
        }

        private static string Digits(string? value) => new((value ?? "").Where(char.IsAsciiDigit).ToArray());

        private static readonly Regex Spaces = new(" {2,}", RegexOptions.Compiled);

        private static string? Clean(string? value, Func<char, bool> allowed, string drop, int max)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;
            // Accented letters become their plain letter (é -> e); other characters the IRS does
            // not accept become a space, except the few that are simply dropped ("Inc." -> "Inc").
            var plain = new StringBuilder();
            foreach (var c in value.Normalize(NormalizationForm.FormD))
            {
                if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;
                if (drop.Contains(c)) continue;
                plain.Append(allowed(c) ? c : ' ');
            }
            var s = Spaces.Replace(plain.ToString(), " ").Trim();
            if (s.Length > max) s = s[..max].TrimEnd();
            return s.Length == 0 ? null : s;
        }
    }
}

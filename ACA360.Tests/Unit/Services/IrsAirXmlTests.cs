using System.Xml.Linq;
using ACA360.BusinessLogic.Services;

namespace ACA360.Tests.Unit.Services;

/// <summary>
/// The IRS files (1094-C/1095-C, 1094-B/1095-B, manifest) built from sample data and checked
/// against the IRS TY2025 schemas (ACA360.BusinessLogic/IrsSchemas, from irs.gov), and the
/// text clean-up that keeps names and addresses within the IRS character rules.
/// </summary>
public class IrsAirXmlTests
{
    private static readonly XNamespace Air = IrsAirXml.AirNamespace(2025);
    private static readonly XNamespace Irs = IrsAirXml.Irs;

    private static T[] Twelve<T>(T value) => Enumerable.Repeat(value, 12).ToArray();

    private static readonly IrsAddress Office = new("100 Main St., Suite #4", null, "St. Louis", "mo", "63101-1234");

    // The local test employer's real name, symbols and all: the IRS would reject it as it is.
    private static IrsEmployer Employer(string name = "Bobby's Symbol & Company (CO) -24/!,LLC._+*^%$#@<>\\\"';:]}{[") =>
        new(name, "45-2210881", Office, "Pat O'Neil", "(201) 555-0100");

    private static Irs1095C Employee(int recordId, string? annualOffer = "1E", string?[]? monthly = null, IReadOnlyList<IrsCoveredPerson>? covered = null, int employeeId = 0) => new(
        EmployeeId: employeeId, BirthDate: new DateTime(1980, 6, 15),
        RecordId: recordId, FirstName: "José", LastName: "O'Brien-Smith Jr.", Ssn: "123-45-6789", Address: Office, PlanStartMonth: 1,
        AnnualOffer: annualOffer, MonthlyOffer: monthly ?? Twelve<string?>(null),
        AnnualContribution: annualOffer == null ? null : 95.5m, MonthlyContribution: annualOffer == null ? Twelve<decimal?>(120.25m) : Twelve<decimal?>(null),
        AnnualSafeHarbor: annualOffer == null ? null : "2C", MonthlySafeHarbor: annualOffer == null ? Twelve<string?>("2F") : Twelve<string?>(null),
        AnnualIchraZip: null, MonthlyIchraZip: Twelve<string?>(null),
        Covered: covered ?? Array.Empty<IrsCoveredPerson>());

    private static Irs1094C Transmittal(params Irs1095C[] forms) => new(
        TaxYear: 2025, Employer: Employer(), Authoritative: true, IsCorrection: false,
        AggregatedGroup: true, QualifyingOfferMethod: true, NinetyEightPercentOfferMethod: false,
        MinimumEssentialCoverageOffered: Twelve(true), FullTimeCount: Twelve(55), TotalCount: Twelve(70), AggregatedGroupMonths: Twelve(true),
        OtherMembers: new (string?, string?)[] { ("Cascade Clinical Services, Inc.", "58-7734121"), ("Own row", "45-2210881") },
        Forms: forms);

    private static void AssertValid(XDocument doc)
    {
        var errors = IrsAirXml.Validate(doc);
        Assert.True(errors.Count == 0, "Not valid against the IRS schema:\n" + string.Join("\n", errors) + "\n\n" + IrsAirXml.ToText(doc));
    }

    [Fact]
    public void The1094CAndItsFormsMatchTheIrsSchema()
    {
        var covered = new IrsCoveredPerson("Ana", null, "O'Brien", null, null, new DateTime(2015, 3, 2), false,
            new[] { true, true, true, false, false, false, false, false, false, false, false, false });
        var monthly = new string?[] { "1A", "1A", "1A", "1H", "1H", "1H", "1E", "1E", "1E", "1E", "1E", "1E" };
        var doc = IrsAirXml.Build1094C(Transmittal(Employee(1, employeeId: 17), Employee(2, annualOffer: null, monthly: monthly, covered: new[] { covered }, employeeId: 18)));

        AssertValid(doc);
        Assert.Equal("Form109495CTransmittalUpstream", doc.Root!.Name.LocalName);
        Assert.Equal("Bobby's Symbol & Company (CO) -24 LLC '", doc.Descendants(Air + "BusinessNameLine1Txt").First().Value);
        Assert.Equal("452210881", doc.Descendants(Irs + "EmployerEIN").Single().Value);
        // The employer's own EIN is not listed among the other members.
        Assert.Equal("587734121", doc.Descendants(Air + "OtherALEMembersGrp").Single().Element(Irs + "EIN")!.Value);

        var forms = doc.Descendants(Air + "Form1095CUpstreamDetail").ToList();
        // Record IDs run 1, 2, 3... whatever the employee ids are (IRS Publication 5165, 3.2).
        Assert.Equal(new[] { "1", "2" }, forms.Select(f => f.Element(Air + "RecordId")!.Value));
        Assert.Empty(doc.Descendants(Air + "AgeNum"));   // no ICHRA code
        var first = forms[0];
        Assert.Equal("Jose", first.Descendants(Air + "PersonFirstNm").First().Value);
        Assert.Equal("OBrien-Smith Jr", first.Descendants(Air + "PersonLastNm").First().Value);
        Assert.Equal("123456789", first.Descendants(Irs + "SSN").Single().Value);
        Assert.Equal("1E", first.Descendants(Air + "AnnualOfferOfCoverageCd").Single().Value);
        Assert.Equal("95.50", first.Descendants(Air + "AnnlEmployeeRequiredContriAmt").Single().Value);
        Assert.Equal("01", first.Element(Air + "StartMonthNumberCd")!.Value);
        Assert.Equal("100 Main St Suite 4", first.Descendants(Air + "AddressLine1Txt").Single().Value);
        Assert.Equal("St Louis", first.Descendants(Irs + "CityNm").Single().Value);
        Assert.Equal("1234", first.Descendants(Irs + "USZIPExtensionCd").Single().Value);

        var second = forms[1];
        Assert.Equal("1H", second.Descendants(Air + "AprOfferCd").Single().Value);
        Assert.Equal(12, second.Descendants(Air + "MonthlyEmployeeRequiredContriGrp").Single().Elements().Count());
        Assert.Equal("2015-03-02", second.Descendants(Air + "BirthDt").Single().Value);
        Assert.Equal("1", second.Descendants(Air + "JanuaryInd").Single().Value);
    }

    [Fact]
    public void A1095CWithoutAnOfferCodeOrPlanStartMonthIsRefusedWithWhatToFix()
    {
        var missing = Employee(1, annualOffer: null, monthly: Twelve<string?>(null), employeeId: 21) with { PlanStartMonth = null };
        var ex = Assert.Throws<IrsDataException>(() => IrsAirXml.Build1094C(Transmittal(missing)));
        Assert.Contains(ex.Problems, p => p.Contains("Employee 21") && p.Contains("line 14") && p.Contains("Jan"));
        Assert.Contains(ex.Problems, p => p.Contains("Employee 21") && p.Contains("plan start month"));
    }

    [Fact]
    public void A1094CWithoutAValidEinIsRefused()
    {
        var data = Transmittal(Employee(1)) with { Employer = Employer() with { Ein = "12-345" } };
        var ex = Assert.Throws<IrsDataException>(() => IrsAirXml.Build1094C(data));
        Assert.Contains(ex.Problems, p => p.Contains("EIN"));
    }

    [Fact]
    public void ACorrectionIsNotTheAuthoritativeTransmittal()
    {
        var corrected = Employee(1, employeeId: 5) with { Corrects = new IrsCorrectedRecord("1095C-25-00012345|1|7", "Joseph", "OBrien", "987654321") };
        var doc = IrsAirXml.Build1094C(Transmittal(corrected) with { Authoritative = false, IsCorrection = true });
        AssertValid(doc);
        Assert.Equal("0", doc.Descendants(Air + "AuthoritativeTransmittalInd").Single().Value);
        Assert.Empty(doc.Descendants(Air + "ALEMemberInformationGrp"));
        var form = doc.Descendants(Air + "Form1095CUpstreamDetail").Single();
        Assert.Equal("1", form.Element(Air + "CorrectedInd")!.Value);
        // It names the record it corrects, as the IRS identified it.
        Assert.Equal("1095C-25-00012345|1|7", form.Descendants(Air + "CorrectedUniqueRecordId").Single().Value);
        // The recipient as filed on the record corrected, not as now (Pub 5165, 7.1.3).
        Assert.Equal("987654321", form.Descendants(Air + "CorrectedRecRecipientTIN").Single().Value);
        Assert.Equal("Joseph", form.Descendants(Air + "CorrectedRecRecipientPrsnName").Single().Element(Air + "PersonFirstNm")!.Value);
        Assert.Equal("Jose", form.Element(Air + "EmployeeInfoGrp")!.Descendants(Air + "PersonFirstNm").Single().Value);
    }

    [Fact]
    public void The1094BAndItsFormsMatchTheIrsSchema()
    {
        var covered = new IrsCoveredPerson("Lee", "A", "Park", null, "987654321", null, true, Twelve(true));
        var doc = IrsAirXml.Build1094B(new Irs1094B(2025, Employer("Acme Manufacturing, Inc."), false,
            new[] { new Irs1095B(1, "Kim", "Park", "111-22-3333", Office, new[] { covered }, EmployeeId: 9) }));

        AssertValid(doc);
        Assert.Equal("Form109495BTransmittalUpstream", doc.Root!.Name.LocalName);
        Assert.Equal("Acme Manufacturing Inc", doc.Descendants(Air + "BusinessNameLine1Txt").First().Value);
        Assert.Equal("111223333", doc.Descendants(Air + "TIN").Single().Value);
        Assert.Equal("B", doc.Descendants(Air + "HealthCoverageOriginCd").Single().Value);
    }

    [Fact]
    public void TheManifestMatchesTheIrsSchema()
    {
        var doc = IrsAirXml.BuildManifest(new IrsManifest(
            TaxYear: 2025, PriorYearData: false, IsCorrection: false, TestFile: true, BForms: false,
            TransmitterEin: "23-0000000", TransmitterName: "ACA360 Software, LLC.", TransmitterAddress: Office,
            ContactName: "Support Team", ContactPhone: "201-555-0100", SoftwareId: "25A0012345", Tcc: "BB0TC",
            TransmissionId: Guid.Parse("11111111-2222-3333-4444-555555555555"), PayeeCount: 2,
            Checksum: new string('a', 64), ByteSize: 1234, FileName: "1094C_Request_BB0TC_20260102T030405006Z.xml",
            TimestampUtc: new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc)));

        AssertValid(doc);
        Assert.Equal("ACAUIBusinessHeader", doc.Root!.Name.LocalName);
        Assert.Equal("11111111-2222-3333-4444-555555555555:SYS12:BB0TC::T", doc.Descendants(Air + "UniqueTransmissionId").Single().Value);
        Assert.Equal("T", doc.Descendants(Air + "TestFileCd").Single().Value);
        Assert.Equal("2025", doc.Descendants(Air + "PaymentYr").Single().Value);
    }

    [Fact]
    public void AManifestWithoutTheTransmitterAddressIsRefused()
    {
        var ex = Assert.Throws<IrsDataException>(() => IrsAirXml.BuildManifest(new IrsManifest(
            2025, false, false, true, false, "230000000", "ACA360", new IrsAddress(null, null, null, null, null),
            "Support Team", "2015550100", "25A0012345", "BB0TC", Guid.NewGuid(), 1, new string('a', 64), 1, "f.xml", DateTime.UtcNow)));
        Assert.Contains(ex.Problems, p => p.Contains("mailing address"));
    }

    [Theory]
    [InlineData("BB0TCC")]   // the local placeholder: six characters
    [InlineData("AB000")]    // starts with a vowel
    [InlineData("")]
    public void ATccTheIrsWouldNotRecogniseIsRefused(string tcc)
    {
        var ex = Assert.Throws<IrsDataException>(() => IrsAirXml.CheckTransmitter(
            tcc, "230000000", "ACA360", Office, "Support Team", "2015550100", "25A0012345"));
        Assert.Contains(ex.Problems, p => p.Contains("TCC"));
    }

    [Theory]
    [InlineData("Smith & Sons, Inc.", "Smith & Sons Inc")]
    [InlineData("  Café   Rouge (NY)  ", "Cafe Rouge (NY)")]
    [InlineData("A.B.C. Corp #7", "ABC Corp 7")]
    [InlineData("$%^", null)]
    public void BusinessNamesKeepOnlyWhatTheIrsAccepts(string input, string? expected) =>
        Assert.Equal(expected, IrsText.BusinessName(input));

    [Theory]
    [InlineData("O'Neil", "ONeil")]
    [InlineData("Mary Ann", "Mary Ann")]
    [InlineData("Zoë-Jane", "Zoe-Jane")]
    [InlineData("A Very Long Last Name Indeed", "A Very Long Last Nam")]
    public void PersonNamesKeepOnlyWhatTheIrsAccepts(string input, string expected) =>
        Assert.Equal(expected, IrsText.PersonName(input));

    [Theory]
    [InlineData("#12 Oak Rd., Apt 3", "12 Oak Rd Apt 3")]
    [InlineData("PO Box 77/B", "PO Box 77/B")]
    public void StreetAddressesKeepOnlyWhatTheIrsAccepts(string input, string expected) =>
        Assert.Equal(expected, IrsText.Street(input));

    // ---- Tax years: each is built and checked against its own IRS schemas

    public static TheoryData<int> TaxYearsInTheApp()
    {
        var years = new TheoryData<int>();
        foreach (var y in IrsAirXml.AvailableTaxYears()) years.Add(y);
        return years;
    }

    [Fact]
    public void TheAppHasTheSchemasFor2023To2025() =>
        Assert.Equal(new[] { 2023, 2024, 2025 }, IrsAirXml.AvailableTaxYears().Where(y => y <= 2025));

    [Theory]
    [MemberData(nameof(TaxYearsInTheApp))]
    public void EveryTaxYearsFilesMatchThatYearsSchema(int year)
    {
        var c = IrsAirXml.Build1094C(Transmittal(Employee(1), Employee(2)) with { TaxYear = year });
        var b = IrsAirXml.Build1094B(new Irs1094B(year, Employer(), false,
            new[] { new Irs1095B(1, "Kim", "Park", "111-22-3333", Office,
                new[] { new IrsCoveredPerson("Kim", null, "Park", null, "111223333", null, true, Twelve(true)) }, EmployeeId: 9) }));
        var manifest = IrsAirXml.BuildManifest(new IrsManifest(year, true, false, true, false, "230000000", "ACA360", Office,
            "Support Team", "2015550100", "25A0012345", "BB0TC", Guid.NewGuid(), 2, new string('a', 64), 10,
            $"1094C_Request_BB0TC_{year}0102T030405006Z.xml", new DateTime(year + 1, 1, 2, 3, 4, 5, DateTimeKind.Utc)));

        foreach (var doc in new[] { c, b, manifest })
        {
            AssertValid(doc);
            Assert.Contains(doc.Descendants(), e => e.Name.Namespace == IrsAirXml.AirNamespace(year));
        }
        Assert.Equal($"urn:us:gov:treasury:irs:ext:aca:air:ty{year % 100}", IrsAirXml.AirNamespace(year).NamespaceName);
    }

    [Fact]
    public void ATaxYearWithoutSchemasIsRefusedWithWhatToDo()
    {
        var ex = Assert.Throws<IrsDataException>(() => IrsAirXml.Build1094C(Transmittal(Employee(1)) with { TaxYear = 2030 }));
        Assert.Contains(ex.Problems, p => p.Contains("tax year 2030") && p.Contains("IrsSchemas/TY2030"));
    }

    // ---- Record IDs, ICHRA age, corrections

    [Fact]
    public void A1095BWithNoCoveredIndividualsIsRefused()
    {
        var ex = Assert.Throws<IrsDataException>(() => IrsAirXml.Build1094B(new Irs1094B(2025, Employer(), false,
            new[] { new Irs1095B(1, "Kim", "Park", "111-22-3333", Office, Array.Empty<IrsCoveredPerson>(), EmployeeId: 44) })));
        Assert.Contains(ex.Problems, p => p.Contains("Employee 44") && p.Contains("covered individuals"));
    }

    [Fact]
    public void RecordIdsThatDoNotRun123AreRefused()
    {
        var ex = Assert.Throws<IrsDataException>(() => IrsAirXml.Build1094C(Transmittal(Employee(1), Employee(3))));
        Assert.Contains(ex.Problems, p => p.Contains("Record IDs"));
    }

    [Fact]
    public void AnIchraCodeReportsTheAgeOnJanuaryFirst()
    {
        // Born 15 June 1980: 44 on 1 January 2025.
        var ichra = Employee(1, annualOffer: "1L") with { AnnualIchraZip = "07001", AnnualSafeHarbor = null };
        var doc = IrsAirXml.Build1094C(Transmittal(ichra));
        AssertValid(doc);
        Assert.Equal("44", doc.Descendants(Air + "AgeNum").Single().Value);
        Assert.Equal("07001", doc.Descendants(Air + "AnnualICHRAZipCd").Single().Value);
    }

    [Fact]
    public void AnIchraCodeWithoutADateOfBirthIsRefused()
    {
        var ichra = Employee(1, annualOffer: "1M", employeeId: 31) with { BirthDate = null };
        var ex = Assert.Throws<IrsDataException>(() => IrsAirXml.Build1094C(Transmittal(ichra)));
        Assert.Contains(ex.Problems, p => p.Contains("Employee 31") && p.Contains("date of birth"));
    }

    [Theory]
    [InlineData(1980, 6, 15, 2025, 44)]
    [InlineData(1980, 1, 1, 2025, 45)]    // birthday on January 1 counts
    [InlineData(1980, 1, 2, 2025, 44)]
    public void AgeOnJanuaryFirst(int y, int m, int d, int taxYear, int expected) =>
        Assert.Equal(expected, IrsAirXml.AgeOnJanuary1(new DateTime(y, m, d), taxYear));

    [Fact]
    public void ACorrectionWithoutTheRecordItCorrectsIsRefused()
    {
        var ex = Assert.Throws<IrsDataException>(() => IrsAirXml.Build1094C(Transmittal(Employee(1, employeeId: 12)) with { Authoritative = false, IsCorrection = true }));
        Assert.Contains(ex.Problems, p => p.Contains("Employee 12") && p.Contains("acknowledgement"));
    }

    // ---- Reading IRS acknowledgements (IRS Publication 5165, section 6)

    private static IrsAcknowledgement Ack(string xml) => IrsAcknowledgement.Parse(new MemoryStream(System.Text.Encoding.UTF8.GetBytes(xml)));

    [Fact]
    public void TheStatusResponseIsRead()
    {
        var ack = Ack("""
            <n1:ACABulkRequestTransmitterStatusDetailResponse xmlns:n1="urn:us:gov:treasury:irs:msg:irstransmitterstatusrequest"
                xmlns:air="urn:us:gov:treasury:irs:ext:aca:air:ty25" xmlns:irs="urn:us:gov:treasury:irs:common">
              <air:ACABulkRequestTransmitterResponse>
                <air:TransmissionStatusCd>Accepted with Errors</air:TransmissionStatusCd>
                <irs:ReceiptId>1095C-25-00012345</irs:ReceiptId>
              </air:ACABulkRequestTransmitterResponse>
              <air:ACABulkReqTrnsmtStsRespGrpDtl><irs:BulkExchangeFile>AA==</irs:BulkExchangeFile></air:ACABulkReqTrnsmtStsRespGrpDtl>
            </n1:ACABulkRequestTransmitterStatusDetailResponse>
            """);
        Assert.Equal(3, ack.StatusId);
        Assert.Equal("1095C-25-00012345", ack.ReceiptId);
        Assert.Empty(ack.Errors);
    }

    [Fact]
    public void TheErrorDataFileIsReadWithTheRecordsItNames()
    {
        var ack = Ack("""
            <FormBCTransmitterSubmissionDtl xmlns="urn:us:gov:treasury:irs:ext:aca:air:ty25" xmlns:irs="urn:us:gov:treasury:irs:common">
              <ACATransmitterSubmissionDetail>
                <TransmitterErrorDetailGrp>
                  <SubmissionLevelStatusCd>Accepted with Errors</SubmissionLevelStatusCd>
                  <UniqueSubmissionId>1095C-25-00012345|1</UniqueSubmissionId>
                  <irs:ErrorMessageDetail><irs:ErrorMessageCd>1094C-011</irs:ErrorMessageCd><irs:ErrorMessageTxt>Line 21 ...</irs:ErrorMessageTxt></irs:ErrorMessageDetail>
                </TransmitterErrorDetailGrp>
                <TransmitterErrorDetailGrp>
                  <UniqueRecordId>1095C-25-00012345|1|2</UniqueRecordId>
                  <irs:ErrorMessageDetail>
                    <irs:ErrorMessageCd>AIRTN500</irs:ErrorMessageCd>
                    <irs:ErrorMessageTxt>The SSN does not match the name.</irs:ErrorMessageTxt>
                    <irs:XpathContent>/Form1095CUpstreamDetail[2]/EmployeeInfoGrp/SSN</irs:XpathContent>
                  </irs:ErrorMessageDetail>
                </TransmitterErrorDetailGrp>
              </ACATransmitterSubmissionDetail>
            </FormBCTransmitterSubmissionDtl>
            """);
        Assert.Equal(3, ack.StatusId);
        Assert.Equal("1095C-25-00012345", ack.ReceiptId);
        Assert.Equal(2, ack.Errors.Count);
        var record = ack.Errors.Single(e => e.RecordId != null);
        Assert.Equal(2, record.RecordId);
        Assert.Equal("AIRTN500", record.Code);
        Assert.Contains("EmployeeInfoGrp/SSN", record.Xpath);
        Assert.Null(ack.Errors.Single(e => e.RecordId == null).UniqueRecordId);
    }

    [Theory]
    [InlineData("Accepted", 2)]
    [InlineData("Rejected", 4)]
    [InlineData("Partially Accepted", 3)]
    [InlineData("Processing", 1)]
    public void TheManifestResponseStatusIsRead(string status, int statusId)
    {
        var ack = Ack($"""
            <n1:TransmitterManifestResponse xmlns="urn:us:gov:treasury:irs:common" xmlns:n1="urn:us:gov:treasury:irs:msg:form1094-1095BCtransmitterrespmessage">
              <ReceiptId>1095C-25-00099999</ReceiptId><SubmissionStatusCd>{status}</SubmissionStatusCd>
            </n1:TransmitterManifestResponse>
            """);
        Assert.Equal(statusId, ack.StatusId);
        Assert.Equal("1095C-25-00099999", ack.ReceiptId);
    }

    // ---- Corrections of the 1094-C and replacements (IRS Publication 5165, section 7)

    [Fact]
    public void ACorrected1094CIsTheCompleteAuthoritativeTransmittalAlone()
    {
        var corrects = new IrsCorrectedSubmission("1094C-25-00012345|1", "Bobby's Old Name, Inc.", "45-2210881");
        var doc = IrsAirXml.Build1094C(Transmittal() with { Corrects = corrects });
        AssertValid(doc);
        var detail = doc.Descendants(Air + "Form1094CUpstreamDetail").Single();
        Assert.Equal("1", detail.Element(Air + "CorrectedInd")!.Value);
        var info = detail.Element(Air + "CorrectedSubmissionInfoGrp")!;
        Assert.Equal("1094C-25-00012345|1", info.Element(Air + "CorrectedUniqueSubmissionId")!.Value);
        Assert.Equal("Bobby's Old Name Inc", info.Descendants(Air + "BusinessNameLine1Txt").Single().Value);
        Assert.Equal("452210881", info.Element(Air + "CorrectedSubmissionPayerTIN")!.Value);
        Assert.Equal("1", detail.Element(Air + "AuthoritativeTransmittalInd")!.Value);
        Assert.NotEmpty(detail.Descendants(Air + "ALEMemberInformationGrp"));   // Parts II-IV complete
    }

    [Fact]
    public void ACorrected1094CWithFormsIsRefused()
    {
        var corrects = new IrsCorrectedSubmission("1094C-25-00012345|1", "Name", "452210881");
        var ex = Assert.Throws<IrsDataException>(() => IrsAirXml.Build1094C(Transmittal(Employee(1)) with { Corrects = corrects }));
        Assert.Contains(ex.Problems, p => p.Contains("no Forms 1095-C"));
    }

    [Fact]
    public void AReplacementManifestNamesTheRejectedReceipt()
    {
        var doc = IrsAirXml.BuildManifest(new IrsManifest(2025, false, false, true, false, "230000000", "ACA360", Office,
            "Support Team", "2015550100", "25A0012345", "BB0TC", Guid.NewGuid(), 1, new string('a', 64), 10,
            "1094C_Request_BB0TC_20260102T030405006Z.xml", DateTime.UtcNow, OriginalReceiptId: "1095C-25-00099999"));
        AssertValid(doc);
        Assert.Equal("R", doc.Descendants(Air + "TransmissionTypeCd").Single().Value);
        Assert.Equal("1095C-25-00099999", doc.Descendants(Air + "OriginalReceiptId").Single().Value);
    }

    // ---- The Error Data File attached to a status response

    private const string AttachedErrorFile = """
        <FormBCTransmitterSubmissionDtl xmlns="urn:us:gov:treasury:irs:ext:aca:air:ty25" xmlns:irs="urn:us:gov:treasury:irs:common">
          <ACATransmitterSubmissionDetail><TransmitterErrorDetailGrp>
            <UniqueRecordId>1095C-25-00012345|1|3</UniqueRecordId>
            <irs:ErrorMessageDetail><irs:ErrorMessageCd>AIRTN500</irs:ErrorMessageCd><irs:ErrorMessageTxt>TIN mismatch</irs:ErrorMessageTxt></irs:ErrorMessageDetail>
          </TransmitterErrorDetailGrp></ACATransmitterSubmissionDetail>
        </FormBCTransmitterSubmissionDtl>
        """;

    private static string StatusResponse(string bulkExchangeFile) => $"""
        <n1:ACABulkRequestTransmitterStatusDetailResponse xmlns:n1="urn:us:gov:treasury:irs:msg:irstransmitterstatusrequest"
            xmlns:air="urn:us:gov:treasury:irs:ext:aca:air:ty25" xmlns:irs="urn:us:gov:treasury:irs:common" xmlns:xop="http://www.w3.org/2004/08/xop/include">
          <air:ACABulkRequestTransmitterResponse>
            <air:TransmissionStatusCd>Accepted with Errors</air:TransmissionStatusCd>
            <irs:ReceiptId>1095C-25-00012345</irs:ReceiptId>
          </air:ACABulkRequestTransmitterResponse>
          <air:ACABulkReqTrnsmtStsRespGrpDtl><irs:BulkExchangeFile>{bulkExchangeFile}</irs:BulkExchangeFile></air:ACABulkReqTrnsmtStsRespGrpDtl>
        </n1:ACABulkRequestTransmitterStatusDetailResponse>
        """;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]   // gzip-compressed
    public void TheErrorFileAttachedToAStatusResponseIsRead(bool compressed)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(AttachedErrorFile);
        if (compressed)
        {
            var zipped = new MemoryStream();
            using (var gz = new System.IO.Compression.GZipStream(zipped, System.IO.Compression.CompressionLevel.Fastest, leaveOpen: true)) gz.Write(bytes);
            bytes = zipped.ToArray();
        }
        var ack = Ack(StatusResponse(Convert.ToBase64String(bytes)));
        Assert.Equal(3, ack.StatusId);
        Assert.False(ack.ErrorFileMissing);
        var error = Assert.Single(ack.Errors);
        Assert.Equal(3, error.RecordId);
        Assert.Equal("AIRTN500", error.Code);
    }

    [Fact]
    public void AStatusResponseWhoseAttachmentIsOnlyReferencedSaysSo()
    {
        var ack = Ack(StatusResponse("""<xop:Include href="cid:errors.xml"/>"""));
        Assert.Equal(3, ack.StatusId);
        Assert.True(ack.ErrorFileMissing);
        Assert.Empty(ack.Errors);
    }

    [Theory]
    [InlineData("not xml at all")]
    [InlineData("<html><body>Sign in</body></html>")]
    public void SomethingElseIsNotTakenForAnAcknowledgement(string content) =>
        Assert.Throws<FormatException>(() => Ack(content));
}

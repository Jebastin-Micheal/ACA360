using System.Globalization;
using System.Text;
using ACA360.Core.Models;

namespace ACA360.BusinessLogic.Services
{
    /// <summary>The rows of one import workbook, by sheet, keyed by the template's column headers.</summary>
    public sealed class DummyWorkbook
    {
        public Dictionary<string, List<Dictionary<string, object?>>> Sheets { get; } = new(StringComparer.OrdinalIgnoreCase);
        public DummyFileSummary Summary { get; set; } = null!;

        public List<Dictionary<string, object?>> Rows(string sheet) =>
            Sheets.TryGetValue(sheet, out var rows) ? rows : Sheets[sheet] = new();
    }

    /// <summary>Where a warning or error lands.</summary>
    public enum DummyIssueTarget { Employee, Dependent, Plan }

    /// <summary>
    /// Makes up an import workbook for the Dummy Data page: the employer, made-up affiliates, plans
    /// and premiums (monthly, salary or age bands), employees (part time, new hires, leavers,
    /// COBRA, retirees, union members, in the shares asked for) and dependents, filled in the way
    /// the validation rules (sp_Validation_ExecuteRules) accept as clean. A share of employees can
    /// be given one warning or one error each, taken in turn from the chosen <see cref="Issues"/>.
    /// People and companies are made up: e-mail addresses use example.com and affiliates are named
    /// "DUMMY ...".
    /// </summary>
    public static class DummyDataGenerator
    {
        public const string EmployerSheet = "Employer Tab", PlanSheet = "Plan Information", PremiumSheet = "Premium",
                            EmployeeSheet = "Employee", DependentSheet = "Dependents";

        public static readonly string[] Bandings = { "Monthly", "Salary", "Age", "Mixed" };

        /// <summary>What an issue needs to know about the employee it is put on.</summary>
        public sealed record IssueContext(Random Rnd, DateTime Hire);

        /// <summary>
        /// One way to make a row trigger a validation rule. Employee issues change the employee row;
        /// dependent issues are made while the employee's dependents are; plan issues add one extra
        /// plan (that no employee is on) to the file when warnings are asked for.
        /// </summary>
        public sealed record Issue(string Code, string Description, bool IsError, int RuleId, DummyIssueTarget Target,
            Action<Dictionary<string, object?>, IssueContext>? Apply = null, bool IsInformation = false)
        {
            /// <summary>What validation reports it as: Error, Warning, or Information (a note that still imports).</summary>
            public string Severity => IsError ? "Error" : IsInformation ? "Information" : "Warning";
        }

        private const string FirstName = "Employee Legal First Name", LastName = "Employee Legal Last Name", Ssn = "Employee SSN",
                             Status = "Status", Hire = "Hire Date", Term = "Termination Date", Plan = "Lowest Cost Plan Offered (Plan Name)",
                             State = "State/ Province", Zip = "Zip", Salary = "W-2 Income/ Annual Salary (if applicable)";

        /// <summary>
        /// The warnings and errors the page can put in a file, and the rule each one triggers.
        /// Warnings come only from rules that report rather than auto-correct (IsAutoCorrect = 0):
        /// validation quietly fixes an apostrophe in a name or a long first name, so those never show.
        /// </summary>
        public static readonly IReadOnlyList<Issue> Issues = new Issue[]
        {
            new("ZIP_STATE", "ZIP code does not match the state", false, 107, DummyIssueTarget.Employee,
                (r, _) => { r[State] = "CA"; r[Zip] = "07102"; }),
            new("FIRST_NAME_SHORT", "First name of one letter", false, 76, DummyIssueTarget.Employee,
                (r, _) => r[FirstName] = ((string)r[FirstName]!)[..1]),
            // 2,000 a year against a premium of at least 24 a month (the cheapest age band): over the
            // affordability percentage in every year, whatever the banding.
            new("NOT_AFFORDABLE", "Salary too low for the plan's premium (affordability)", false, 105, DummyIssueTarget.Employee,
                (r, _) => r[Salary] = 2000),
            new("DEPENDENT_NO_SSN", "Dependent without an SSN (birth date only)", false, 21, DummyIssueTarget.Dependent, IsInformation: true),
            new("DEPENDENT_WITHOUT_EMPLOYEE", "Dependent covered while the employee is not", false, 56, DummyIssueTarget.Dependent),
            new("PLAN_LONG_WAIT", "Extra plan with a 120-day waiting period", false, 71, DummyIssueTarget.Plan),
            new("PREMIUM_LOW", "Extra plan with a premium under 1.00", false, 43, DummyIssueTarget.Plan),

            new("SSN_000", "SSN starting with 000", true, 79, DummyIssueTarget.Employee,
                (r, c) => r[Ssn] = $"000-{c.Rnd.Next(10, 100)}-{c.Rnd.Next(1000, 10000)}"),
            new("TERM_BEFORE_HIRE", "Termination date before the hire date", true, 11, DummyIssueTarget.Employee,
                (r, c) => r[Term] = Date(c.Hire.AddDays(-30))),
            new("STATUS_INVALID", "Status that is not 0 or 1", true, 81, DummyIssueTarget.Employee, (r, _) => r[Status] = 5),
            new("PLAN_MISSING", "Plan that is not in the file", true, 135, DummyIssueTarget.Employee, (r, _) => r[Plan] = "NO SUCH PLAN"),
            new("FIRST_NAME_MISSING", "First name missing", true, 44, DummyIssueTarget.Employee, (r, _) => r[FirstName] = null),
        };

        private static readonly string[] FirstNames =
        {
            "JAMES", "MARY", "ROBERT", "PATRICIA", "JOHN", "JENNIFER", "MICHAEL", "LINDA", "DAVID", "ELIZABETH", "WILLIAM", "BARBARA",
            "RICHARD", "SUSAN", "JOSEPH", "JESSICA", "THOMAS", "SARAH", "CHARLES", "KAREN", "DANIEL", "LISA", "MATTHEW", "NANCY",
            "ANTHONY", "BETTY", "MARK", "SANDRA", "DONALD", "ASHLEY", "STEVEN", "EMILY", "ANDREW", "DONNA", "PAUL", "MICHELLE",
            "JOSHUA", "CAROL", "KENNETH", "AMANDA", "KEVIN", "MELISSA", "BRIAN", "DEBORAH", "GEORGE", "STEPHANIE", "TIMOTHY", "REBECCA",
            "RONALD", "LAURA", "JASON", "HELEN", "EDWARD", "SHARON", "JEFFREY", "CYNTHIA", "RYAN", "KATHLEEN", "JACOB", "AMY",
        };

        private static readonly string[] LastNames =
        {
            "SMITH", "JOHNSON", "WILLIAMS", "BROWN", "JONES", "GARCIA", "MILLER", "DAVIS", "RODRIGUEZ", "MARTINEZ", "HERNANDEZ", "LOPEZ",
            "GONZALEZ", "WILSON", "ANDERSON", "THOMAS", "TAYLOR", "MOORE", "JACKSON", "MARTIN", "LEE", "PEREZ", "THOMPSON", "WHITE",
            "HARRIS", "SANCHEZ", "CLARK", "RAMIREZ", "LEWIS", "ROBINSON", "WALKER", "YOUNG", "ALLEN", "KING", "WRIGHT", "SCOTT",
            "TORRES", "NGUYEN", "HILL", "FLORES", "GREEN", "ADAMS", "NELSON", "BAKER", "HALL", "RIVERA", "CAMPBELL", "MITCHELL",
        };

        private static readonly string[] Streets =
        {
            "MAPLE AVE", "OAK ST", "PINE RD", "CEDAR LN", "ELM ST", "WASHINGTON BLVD", "LAKE DR", "HILLSIDE AVE", "PARK PL", "RIVER RD",
            "SUNSET BLVD", "MAIN ST", "CHURCH ST", "MILL RD", "SPRING ST", "HIGHLAND AVE", "FOREST DR", "MEADOW LN",
        };

        // City, state and a ZIP code of that state (the ZIP/state rule checks NY, CA, TX, FL and IL).
        private static readonly (string City, string State, string Zip)[] Places =
        {
            ("NEW YORK", "NY", "10001"), ("ALBANY", "NY", "12207"), ("LOS ANGELES", "CA", "90012"), ("SAN DIEGO", "CA", "92101"),
            ("AUSTIN", "TX", "73301"), ("DALLAS", "TX", "75201"), ("MIAMI", "FL", "33101"), ("ORLANDO", "FL", "32801"),
            ("CHICAGO", "IL", "60601"), ("SPRINGFIELD", "IL", "62701"), ("NEWARK", "NJ", "07102"), ("DENVER", "CO", "80202"),
            ("BOSTON", "MA", "02108"), ("SEATTLE", "WA", "98101"), ("ATLANTA", "GA", "30303"), ("PHOENIX", "AZ", "85004"),
        };

        private static readonly string[] CompanyWords =
        {
            "SUMMIT", "HARBOR", "PIONEER", "LIBERTY", "CRESCENT", "GRANITE", "MERIDIAN", "BLUE RIDGE", "EVERGREEN", "KEYSTONE",
            "NORTHSTAR", "RIVERBEND", "SILVERLINE", "TRIDENT", "WESTFIELD", "ASPEN", "BEACON", "CARDINAL", "HORIZON", "LANDMARK",
        };

        private static readonly string[] CompanyKinds = { "LOGISTICS", "HEALTH", "MANUFACTURING", "SERVICES", "RETAIL", "FOODS", "SYSTEMS", "BUILDERS" };
        private static readonly string[] PlanTiers = { "GOLD", "SILVER", "BRONZE", "PLATINUM", "BASIC", "PLUS" };
        private static readonly string[] PlanKinds = { "PPO", "HMO", "HDHP", "EPO" };

        // Salary bands (the affordability rule matches an employee to the band holding their salary) and age bands.
        private static readonly (decimal From, decimal To, decimal Factor)[] SalaryBands = { (0, 39999.99m, 1m), (40000, 79999.99m, 1.25m), (80000, 9999999m, 1.5m) };
        // Age bands are checked for affordability too (script 055): the dearest band costs the base premium, at most 150.
        private static readonly (int From, int To, decimal Factor)[] AgeBands = { (0, 29, 0.6m), (30, 39, 0.7m), (40, 49, 0.8m), (50, 64, 0.9m), (65, 120, 1m) };

        // EIN prefixes the IRS assigns.
        private static readonly int[] EinPrefixes = { 10, 12, 20, 23, 26, 27, 30, 33, 35, 41, 45, 46, 52, 54, 56, 61, 75, 82, 84, 86, 91, 93 };

        /// <summary>New employer names from the page's text box: one per line, cleaned, duplicates dropped.</summary>
        public static List<string> NewNames(string? text) =>
            (text ?? "").Split('\n').Select(CleanBusinessName).Where(n => n.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        /// <summary>The issue kinds a request uses: the chosen codes, or all of them.</summary>
        public static List<Issue> ChosenIssues(DummyDataRequest request) => Issues
            .Where(i => request.IssueCodes.Count == 0 || request.IssueCodes.Contains(i.Code, StringComparer.OrdinalIgnoreCase))
            .ToList();

        /// <summary>Checks a request before any work is done; returns the problems in plain words.</summary>
        public static List<string> Check(DummyDataRequest request)
        {
            var problems = new List<string>();
            int employers = request.EmployerIds.Distinct().Count() + NewNames(request.NewEmployerNames).Count;
            if (employers == 0) problems.Add("Choose an employer or type a new employer name.");
            if (employers > 20) problems.Add("At most 20 employers at a time.");
            if (request.DependentsMin > request.DependentsMax) problems.Add("The smallest number of dependents is larger than the largest.");
            if (request.WarningPercent + request.ErrorPercent > 100) problems.Add("Warnings and errors together cannot be more than 100% of the employees.");
            if (request.CobraPercent + request.RetireePercent > 100) problems.Add("COBRA and retiree together cannot be more than 100% of the leavers.");
            if (request.Employees < request.Affiliates + 1) problems.Add("Each affiliate needs at least one employee: ask for more employees than affiliates.");
            if ((long)request.Employees * Math.Max(1, employers) > 100_000) problems.Add("At most 100,000 employees in one download (employees x employers).");
            if (!Bandings.Contains(request.Banding, StringComparer.OrdinalIgnoreCase)) problems.Add("Choose a premium banding: Monthly, Salary, Age or Mixed.");
            var unknown = request.IssueCodes.Where(c => !Issues.Any(i => string.Equals(i.Code, c, StringComparison.OrdinalIgnoreCase))).ToList();
            if (unknown.Count > 0) problems.Add("Unknown warning or error kinds: " + string.Join(", ", unknown) + ".");
            var chosen = ChosenIssues(request);
            if (request.WarningPercent > 0 && !chosen.Any(i => !i.IsError))
                problems.Add("Tick at least one warning kind, or set warnings to 0%.");
            if (request.ErrorPercent > 0 && !chosen.Any(i => i.IsError))
                problems.Add("Tick at least one error kind, or set errors to 0%.");
            return problems;
        }

        /// <summary>A made-up EIN that is not in <paramref name="taken"/> (digits only), which it is added to.</summary>
        public static string NewEin(Random rnd, ISet<string> taken)
        {
            while (true)
            {
                var digits = $"{EinPrefixes[rnd.Next(EinPrefixes.Length)]}{rnd.Next(0, 10_000_000):D7}";
                if (taken.Add(digits)) return $"{digits[..2]}-{digits[2..]}";
            }
        }

        private sealed class Person
        {
            public string AssocEin = "", First = "", Last = "", PlanName = "", Ssn = "";
            public DateTime Birth, Hire, Eligible;
            public DateTime? TermDate, CoverageEnd;
            public bool PartTime, NewHire, Covered, Union, Cobra, Retiree;
            public Issue? Issue;
            public (string City, string State, string Zip) Place;
        }

        /// <summary>
        /// One workbook for <paramref name="employer"/>. <paramref name="takenEins"/> (digits only) holds
        /// every EIN already in ACA360 or already used, so made-up affiliates never land on a real
        /// employer; it is added to. <paramref name="usedSsns"/> keeps SSNs unique across workbooks.
        /// </summary>
        public static DummyWorkbook Generate(DummyDataRequest request, DummyEmployer employer, Random rnd,
            ISet<string> takenEins, ISet<string> usedSsns)
        {
            int year = request.FilingYear;
            var book = new DummyWorkbook();
            string mainEin = FormatEin(employer.Ein);
            string mainName = CleanBusinessName(employer.Name);
            var chosen = ChosenIssues(request);

            // Employers: the main employer names itself as its own affiliate (rows without an
            // Affiliated EIN are reported as an affiliate without employees).
            var eins = new List<string> { mainEin };
            book.Rows(EmployerSheet).Add(EmployerRow(mainEin, mainEin, mainName, rnd));
            for (int a = 0; a < request.Affiliates; a++)
            {
                string ein = NewEin(rnd, takenEins);
                eins.Add(ein);
                book.Rows(EmployerSheet).Add(EmployerRow(mainEin, ein, $"DUMMY {Pick(rnd, CompanyWords)} {Pick(rnd, CompanyKinds)} {a + 1}", rnd));
            }

            // Plans and premiums.
            var plans = new List<(string Name, int WaitingDays, string Banding)>();
            string banding = Bandings.First(b => string.Equals(b, request.Banding, StringComparison.OrdinalIgnoreCase));
            for (int p = 0; p < request.Plans; p++)
            {
                string name = $"{PlanTiers[p % PlanTiers.Length]} {PlanKinds[p / PlanTiers.Length % PlanKinds.Length]}"
                              + (p >= PlanTiers.Length * PlanKinds.Length ? $" {p + 1}" : "");
                string planBanding = banding == "Mixed" ? Bandings[p % 3] : banding;
                int waiting = rnd.Next(2) == 0 ? 0 : 30;   // with 60 or more, a mid-month hire can be eligible after more than 90 days
                plans.Add((name, waiting, planBanding));
                AddPlan(book, mainEin, name, waiting, planBanding, rnd.Next(4000, 15001) / 100m, year, rnd);
            }
            var issueCount = new List<Issue>();

            // Plan warnings: one extra plan each, which no employee is on.
            if (request.WarningPercent > 0)
                foreach (var issue in chosen.Where(i => i.Target == DummyIssueTarget.Plan))
                {
                    if (issue.Code == "PLAN_LONG_WAIT") AddPlan(book, mainEin, "LONG WAIT PLAN", 120, "Monthly", 75m, year, rnd);
                    else AddPlan(book, mainEin, "LOW PREMIUM PLAN", 0, "Monthly", 0.50m, year, rnd);
                    issueCount.Add(issue);
                }

            // Who gets a warning or an error, and which one (dependent issues need coverage arranged first).
            var people = new List<Person>(request.Employees);
            int warnings = Share(request.Employees, request.WarningPercent);
            int errors = Math.Min(request.Employees - warnings, Share(request.Employees, request.ErrorPercent));
            var order = Shuffled(request.Employees, rnd);
            var warningKinds = chosen.Where(i => !i.IsError && i.Target != DummyIssueTarget.Plan).ToList();
            var errorKinds = chosen.Where(i => i.IsError).ToList();
            var issueOf = new Dictionary<int, Issue>();
            int wStart = rnd.Next(Math.Max(1, warningKinds.Count)), eStart = rnd.Next(Math.Max(1, errorKinds.Count));
            if (warningKinds.Count > 0) for (int i = 0; i < warnings; i++) issueOf[order[i]] = warningKinds[(wStart + i) % warningKinds.Count];
            if (errorKinds.Count > 0) for (int i = 0; i < errors; i++) issueOf[order[warnings + i]] = errorKinds[(eStart + i) % errorKinds.Count];

            var partTime = Pick(request.Employees, request.PartTimePercent, rnd);
            var newHires = Pick(request.Employees, request.NewHirePercent, rnd);
            var union = Pick(request.Employees, request.UnionPercent, rnd);
            var covered = Pick(request.Employees, request.CoveragePercent, rnd);

            for (int e = 0; e < request.Employees; e++)
            {
                var p = new Person
                {
                    // Every affiliate gets an employee first, then employees go anywhere.
                    AssocEin = e < eins.Count ? eins[e] : eins[rnd.Next(eins.Count)],
                    First = Pick(rnd, FirstNames), Last = Pick(rnd, LastNames), Place = Places[rnd.Next(Places.Length)],
                    Birth = new DateTime(year - rnd.Next(22, 64), rnd.Next(1, 13), rnd.Next(1, 29)),
                    PartTime = partTime.Contains(e), NewHire = newHires.Contains(e), Union = union.Contains(e),
                    Issue = issueOf.GetValueOrDefault(e),
                };
                var plan = plans[rnd.Next(plans.Count)];
                p.PlanName = plan.Name;
                p.Covered = !p.Union && covered.Contains(e);
                if (p.Issue?.Code == "DEPENDENT_NO_SSN") { p.Union = false; p.Covered = true; }
                if (p.Issue?.Code == "DEPENDENT_WITHOUT_EMPLOYEE") p.Covered = false;

                // Employees hired before the filing year are eligible on 1 January; new hires on the
                // first of the month after their waiting period.
                if (p.NewHire)
                {
                    p.Hire = new DateTime(year, rnd.Next(1, 10), rnd.Next(1, 29));
                    var due = p.Hire.AddDays(plan.WaitingDays);
                    p.Eligible = due.Day == 1 ? due : new DateTime(due.Year, due.Month, 1).AddMonths(1);
                }
                else
                {
                    p.Hire = new DateTime(year - rnd.Next(1, 11), rnd.Next(1, 13), rnd.Next(1, 29));
                    p.Eligible = new DateTime(year, 1, 1);
                }
                people.Add(p);
            }

            // Leavers: among employees without an issue who are eligible by October, so a month of coverage fits.
            var canLeave = Enumerable.Range(0, people.Count).Where(i => people[i].Issue == null && people[i].Eligible <= new DateTime(year, 10, 31)).ToList();
            var leavers = canLeave.OrderBy(_ => rnd.Next()).Take(Share(request.Employees, request.TerminatedPercent)).ToList();
            foreach (var i in leavers)
            {
                var p = people[i];
                var from = (p.Eligible > p.Hire ? p.Eligible : p.Hire).AddDays(31);
                var to = new DateTime(year, 12, 15);
                if (from < new DateTime(year, 1, 1)) from = new DateTime(year, 1, 31);
                p.TermDate = from.AddDays(rnd.Next(0, Math.Max(1, (to - from).Days)));
                if (p.Covered) p.CoverageEnd = new DateTime(p.TermDate.Value.Year, p.TermDate.Value.Month, 1).AddMonths(1).AddDays(-1);
            }
            var coveredLeavers = leavers.Where(i => people[i].Covered).OrderBy(_ => rnd.Next()).ToList();
            int cobra = Share(coveredLeavers.Count, request.CobraPercent), retiree = Share(coveredLeavers.Count, request.RetireePercent);
            foreach (var i in coveredLeavers.Take(cobra)) people[i].Cobra = true;
            foreach (var i in coveredLeavers.Skip(cobra).Take(retiree)) people[i].Retiree = true;

            int dependents = 0;
            foreach (var (p, e) in people.Select((p, e) => (p, e)))
            {
                p.Ssn = NewSsn(rnd, usedSsns);
                var row = EmployeeRow(p, mainEin, e, year, rnd);
                if (p.Issue?.Apply != null) p.Issue.Apply(row, new IssueContext(rnd, p.Hire));
                if (p.Issue != null) issueCount.Add(p.Issue);
                book.Rows(EmployeeSheet).Add(row);

                int count = p.Covered ? rnd.Next(request.DependentsMin, request.DependentsMax + 1) : 0;
                if (p.Issue?.Code == "DEPENDENT_NO_SSN") count = Math.Max(count, 1);
                if (p.Issue?.Code == "DEPENDENT_WITHOUT_EMPLOYEE") count = 1;
                for (int d = 0; d < count; d++)
                {
                    // The first dependent is a spouse, the rest children born before the coverage starts.
                    var birth = d == 0
                        ? p.Birth.AddYears(rnd.Next(-4, 5)).AddDays(rnd.Next(-150, 150))
                        : new DateTime(Math.Max(year - 24, p.Birth.Year + 18) + rnd.Next(0, 6), rnd.Next(1, 13), rnd.Next(1, 29));
                    if (birth >= p.Eligible) birth = p.Eligible.AddDays(-rnd.Next(30, 400));
                    book.Rows(DependentSheet).Add(new()
                    {
                        ["Primary EIN"] = mainEin, ["EIN Associated with EE (May be an Affiliate EIN)"] = p.AssocEin,
                        ["Employee SSN"] = row[Ssn], ["Dependent Legal First Name"] = Pick(rnd, FirstNames),
                        ["Dependent Middle Initial"] = null, ["Dependent Legal Last Name"] = p.Last, ["Dependent Suffix"] = null,
                        ["Dependent SSN"] = d == 0 && p.Issue?.Code == "DEPENDENT_NO_SSN" ? null : NewSsn(rnd, usedSsns),
                        ["Dependent Birthdate"] = Date(birth),
                        ["Dependent Coverage Start Date"] = Date(p.Eligible), ["Dependent Coverage End Date"] = Date(p.CoverageEnd),
                    });
                    dependents++;
                }
            }

            var mix = new Dictionary<string, int>
            {
                ["Part time"] = people.Count(p => p.PartTime), ["New hires"] = people.Count(p => p.NewHire),
                ["Elected coverage"] = people.Count(p => p.Covered), ["Left in the year"] = people.Count(p => p.TermDate != null),
                ["COBRA"] = people.Count(p => p.Cobra), ["Retiree"] = people.Count(p => p.Retiree), ["Union"] = people.Count(p => p.Union),
            };
            book.Summary = new DummyFileSummary(
                FileName(employer, mainName, year), mainName, mainEin, employer.IsNew, eins.Count, book.Rows(PlanSheet).Count,
                book.Rows(PremiumSheet).Count, request.Employees, dependents, people.Count(p => p.Issue == null), mix,
                issueCount.Where(i => !i.IsError).GroupBy(i => i.Description).ToDictionary(g => g.Key, g => g.Count()),
                issueCount.Where(i => i.IsError).GroupBy(i => i.Description).ToDictionary(g => g.Key, g => g.Count()));
            return book;
        }

        private static Dictionary<string, object?> EmployeeRow(Person p, string mainEin, int index, int year, Random rnd) => new()
        {
            ["Primary EIN"] = mainEin, ["EIN Associated with EE (May be an Affiliate EIN)"] = p.AssocEin,
            [FirstName] = p.First, ["Employee Middle Initial"] = rnd.Next(3) == 0 ? null : ((char)('A' + rnd.Next(26))).ToString(),
            [LastName] = p.Last, ["Employee Suffix"] = null, [Ssn] = p.Ssn, ["Employee Birthdate"] = Date(p.Birth),
            [Status] = p.PartTime ? 0 : 1, ["Expatriate"] = 0, ["Status Start Date"] = Date(p.Hire), ["Status End Date"] = Date(p.TermDate),
            [Hire] = Date(p.Hire), [Term] = Date(p.TermDate),
            // Part time 22,000-31,000, full time 32,000-150,000: affordable against every premium the file has.
            [Salary] = p.PartTime ? rnd.Next(22, 32) * 1000 : rnd.Next(32, 151) * 1000, ["Additional Income"] = null,
            ["Hourly rate (if applicable)"] = null,
            ["Email Address"] = $"{p.First.ToLowerInvariant()}.{p.Last.ToLowerInvariant()}.{index + 1}@example.com",
            ["Foreign Address"] = 0, ["Address 1"] = $"{rnd.Next(10, 9999)} {Pick(rnd, Streets)}", ["Address 2"] = null,
            ["City"] = p.Place.City, [State] = p.Place.State, [Zip] = p.Place.Zip, ["Country"] = "US", [Plan] = p.PlanName,
            ["Date Eligible for Coverage"] = Date(p.Eligible), ["Coverage Elected?"] = p.Covered ? 1 : 0,
            ["Coverage Start Date"] = p.Covered ? Date(p.Eligible) : null, ["Coverage End Date"] = Date(p.CoverageEnd),
            ["Union Employee? (Union offers benefits)"] = p.Union ? 1 : 0,
            ["Date Employer First Pays to Union Benefits"] = p.Union ? Date(p.Eligible) : null,
            ["Date Employer Last Pays to Union Benefits"] = p.Union ? Date(p.TermDate) : null,
            ["COBRA Benefits elected?"] = p.Cobra ? 1 : 0,
            ["COBRA Coverage Effective Date"] = p.Cobra ? Date(p.CoverageEnd!.Value.AddDays(1)) : null,
            ["COBRA Coverage End Date"] = p.Cobra ? Date(new DateTime(year, 12, 31)) : null,
            ["Retiree Benefits elected?"] = p.Retiree ? 1 : 0,
            ["Retiree Plan Start Date"] = p.Retiree ? Date(p.CoverageEnd!.Value.AddDays(1)) : null, ["Retiree Plan End Date"] = null,
        };

        private static void AddPlan(DummyWorkbook book, string mainEin, string name, int waiting, string banding, decimal contribution, int year, Random rnd)
        {
            string bandingText = banding == "Monthly" ? "Monthly" : banding.ToUpperInvariant();
            book.Rows(PlanSheet).Add(new()
            {
                ["Primary EIN"] = mainEin, ["Plan Name"] = name, ["Plan Type"] = "Medical", ["Offered to Spouse"] = 1,
                ["Offered to Dependents"] = 1, ["Waiting period (number of days)"] = waiting, ["Eligible First of the Month"] = 1,
                // Fully insured: a self-funded plan (2) is reported when no enrolled employee on it has dependents.
                ["Funding Type"] = 1, ["Plan Renewal Month"] = 1, ["Plan Terminates on Date of Termination"] = 0,
                ["Minimum Value"] = 1, ["Banding Type"] = bandingText, ["Premium Cap"] = null,
            });
            void Premium(string start, string end, decimal amount) => book.Rows(PremiumSheet).Add(new()
            {
                ["Primary EIN"] = mainEin, ["Plan Name"] = name, ["Banding Type"] = bandingText, ["Start"] = start, ["End"] = end,
                ["Start Date"] = Date(new DateTime(year, 1, 1)), ["End Date"] = Date(new DateTime(year, 12, 31)),
                ["EE Monthly Contribution"] = Math.Round(amount, 2).ToString("0.00", CultureInfo.InvariantCulture),
            });
            // At most 150 a month for salaries from 22,000 (bands rise with salary): affordable in every year from 2017.
            if (banding == "Salary")
                foreach (var b in SalaryBands) Premium(b.From.ToString(CultureInfo.InvariantCulture), b.To.ToString(CultureInfo.InvariantCulture), contribution * b.Factor);
            else if (banding == "Age")
                foreach (var b in AgeBands) Premium(b.From.ToString(CultureInfo.InvariantCulture), b.To.ToString(CultureInfo.InvariantCulture), contribution * b.Factor);
            else
                Premium("January", "December", contribution);
        }

        private static Dictionary<string, object?> EmployerRow(string primaryEin, string affiliatedEin, string name, Random rnd)
        {
            var place = Places[rnd.Next(Places.Length)];
            return new()
            {
                ["Primary EIN"] = primaryEin, ["Affiliated EIN"] = affiliatedEin, ["Employer Name"] = name, ["Foreign Address Ind"] = 0,
                ["Address"] = $"{rnd.Next(100, 9999)} {Pick(rnd, Streets)}", ["Address 2"] = null, ["City"] = place.City,
                ["State/Province"] = place.State, ["Zip"] = place.Zip, ["Country"] = "US",
                ["Phone"] = $"{rnd.Next(201, 990)}555{rnd.Next(1000, 10000)}",
                ["Contact"] = $"{Pick(rnd, FirstNames)} {Pick(rnd, LastNames)}", ["Title"] = "HR MANAGER",
                ["Origin Code"] = null, ["SHOP Identifier"] = null, ["Notes"] = null,
            };
        }

        public static string Date(DateTime d) => d.ToString("M/d/yyyy", CultureInfo.InvariantCulture);
        private static string? Date(DateTime? d) => d.HasValue ? Date(d.Value) : null;

        private static string Pick(Random rnd, string[] list) => list[rnd.Next(list.Length)];
        private static int Share(int count, int percent) => (int)Math.Round(count * percent / 100.0, MidpointRounding.AwayFromZero);
        private static List<int> Shuffled(int count, Random rnd) => Enumerable.Range(0, count).OrderBy(_ => rnd.Next()).ToList();
        private static HashSet<int> Pick(int count, int percent, Random rnd) => Shuffled(count, rnd).Take(Share(count, percent)).ToHashSet();

        /// <summary>NN-NNNNNNN from whatever the employer row holds.</summary>
        public static string FormatEin(string ein)
        {
            var digits = new string((ein ?? "").Where(char.IsDigit).ToArray());
            return digits.Length == 9 ? $"{digits[..2]}-{digits[2..]}" : ein ?? "";
        }

        /// <summary>The employer-name rule allows letters, digits, spaces and ( ) &amp; ' -, up to 75 characters.</summary>
        public static string CleanBusinessName(string name)
        {
            var sb = new StringBuilder();
            foreach (var c in (name ?? "").ToUpperInvariant())
                if (char.IsAsciiLetterOrDigit(c) || " ()&'-".Contains(c)) sb.Append(c);
            var clean = string.Join(' ', sb.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
            return clean.Length > 75 ? clean[..75].TrimEnd() : clean;
        }

        /// <summary>A well-formed SSN the import accepts: area 001-665 (not 000, 666 or 9xx), group 01-99, serial 0001-9999.</summary>
        private static string NewSsn(Random rnd, ISet<string> used)
        {
            while (true)
            {
                var ssn = $"{rnd.Next(1, 666):D3}-{rnd.Next(1, 100):D2}-{rnd.Next(1, 10000):D4}";
                if (used.Add(ssn)) return ssn;
            }
        }

        private static string FileName(DummyEmployer employer, string name, int year)
        {
            var safe = new string(name.Select(c => char.IsAsciiLetterOrDigit(c) ? c : '_').ToArray()).Trim('_');
            while (safe.Contains("__")) safe = safe.Replace("__", "_");
            if (safe.Length > 40) safe = safe[..40].TrimEnd('_');
            return $"DummyData_{safe}_{new string(employer.Ein.Where(char.IsDigit).ToArray())}_{year}.xlsx";
        }
    }
}

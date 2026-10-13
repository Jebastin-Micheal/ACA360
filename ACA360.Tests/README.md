# ACA360.Tests

```
dotnet test ACA360.Tests
```

Stop the running web app first: it locks `ACA360.Web/bin`, so the build fails while it runs.

## Unit tests (`Unit/`)

No database or network; they run anywhere.

| Folder | Covers |
|---|---|
| `Conventions` | Rules every controller action must follow, found by reflection: who can open it without signing in, antiforgery on every POST, no data changes by GET, `RequireEmployerScope` parameter names, permission names.; only the layouts load jQuery. |
| `Security` | `AuthorizePermission`, `HasPermission`, `PiiAccess`, `CustomRequirementHandler`, `UserRoles`, AES encryption. |
| `Filters` | Portal-area filter, employer-scope filter, View As read-only filter, Hangfire dashboard filter, maintenance-mode middleware, slow-request log. |
| `Controllers` | `TourController`, and the employer / employee scope helpers in `BaseController`. |
| `Services` | Plan-year scan, Excel structure check, file upload access rules; the IRS files (1094-C/1095-C, 1094-B/1095-B, manifest) built from sample data and checked against each tax year's IRS schemas (2023-2025), Record IDs 1, 2, 3..., ICHRA ages, corrections, reading IRS acknowledgements (status response, Error Data File), the IRS text clean-up, the Dummy Data generator (row counts, unique SSNs and EINs, shares of warnings and errors), and "Why these codes?" (the reasons for each month's Line 14/15/16, the safe-harbor sums, codes changed by hand). |
| `Helpers` | SSN / EIN masking, user-agent labels, password policy, the address each menu entry opens and the one the sidebar highlights. |

Where today's code breaks a convention, the offenders are listed with a reason in the test
("known" lists). Those lists may only shrink: a new offender fails the test, and so does a
listed one that has been fixed (take it off the list).

## Integration tests (`Integration/`)

They start the real app in memory (`WebApplicationFactory`, Hangfire job server off) against
**your local `db_ACA360`**, read from the web project's user secrets. They run only when that
database is on this machine and reachable; otherwise they are skipped, so they cannot touch a
shared, BETA or production database.

- Each test signs in as a throw-away login (`aca360.tests.*@example.com`) with a random
  password, created in the database and removed afterwards (also at the start of the next run,
  in case a run crashed). No password is stored in the repository.
- `AllPagesTests`: every page, one test each: anonymous visitors are sent to sign-in, and a
  SuperAdmin gets no server error.
- `SignInTests` / `StartupTests`: sign-in, wrong password, switched-off and deleted accounts,
  temporary passwords, lockout, sign-out and revoked sessions; every controller can be built.
- `EmployerScopeTests`: Account Managers and Data Analysts see only their employers; the
  supervisors, StaffAC and StaffAU see every employer, in the Select Employer list and at
  sign-in too (script 051).
- `SecurityTests`, `TourTests`: antiforgery, security headers, Hangfire and FAQ access, tours.
- `AntiforgeryFlowTests`: the Calendar, grid view-mode and Plan requests, built as their pages
  build them: they work with the token header and are refused without it.
- `FileAccessTests`: a file can be opened exactly when the File Upload list shows it
  (`dbo.fn_FileVisibleToUser`, script 041, agrees with `sp_SearchFileLogsPaginated`).
- `DatabaseTests`: every fix script applied (via `Database Script/deploy`), the Tracker list
  procedure, the login check, scripts 040 and 041, permissions the code checks can be granted,
  menus point at pages.
- `EmployerLoginTests`, `PortalSignInTests`: Employer logins come only from Tracker; Employer
  and Broker sign-in, and each sees only their own employers.
- `EmployeePortalTests`: the e-mailed form link (`/MyForms`: last four SSN digits, five wrong
  tries lock it, expiry, sign-out) and the Employee Portal (activate, set password, wrong
  password, consent, download only after consent, staff pages refused).
- `FormOutputTests`: the 1095-C (also with the SSN masked), 1095-B and 1094-C PDFs, read back
  as text; the batch preview; the batch e-mail (link, secure link, log, then opening it); the
  IRS submission for a throw-away employer: both files pass the IRS schema, the log row comes
  back with its id, the manifest matches the form file (count, size, SHA-256); data the IRS
  would reject stops it with what to fix and leaves no file or row behind; an IRS Error Data
  File is traced to the employees through the record list; errors on the 1094-C and on 1095-Cs
  give two correction transmissions, naming the 1094-C / records as filed (name, TIN, EIN), and a
  record already corrected is corrected through its accepted correction; files not yet sent are
  regenerated, sent or accepted ones are not, and a rejected one is re-sent as a replacement
  (IRS Publication 5165, section 7). The active
  transmitter row gets a test TCC and address for the length of the test, then its own back.
- `ImportPipelineTests`: a workbook filled in from the template the app hands out, through
  structure check → assign → validate → import, then checked in the live tables (the employer
  row has no Affiliated EIN, script 048); a missing sheet and a bad SSN stop it at the right step; an
  employee with only a warning is still imported (script 053); the Dummy Data page's files come out
  clean with every premium banding and the employee mix (and import whole, affiliates too), with
  only the chosen warnings, or with the chosen errors. The rules script 055 fixed: an employee under an EIN
  missing from the Employer tab is an error (74), a termination date with the status still open
  warns (19), ZIP codes are checked against every state (107), age-banded premiums are checked
  for affordability by age (105), a missing plan is one error (135, not also 12), and an
  auto-corrected value is kept (before and after) and shown as an Information note. The Error Triage
  Correction Editor (script 056): someone who cannot see the file cannot open or change it, only data
  columns can be changed, each change is kept with who made it, and re-validating keeps skipped warnings
  skipped and earlier auto-corrections on record; an imported file is read-only. "What changed" (057):
  a second file names the new employee, the leaver, the changed city and the employee missing from the
  file. "Why these codes?" (058): generated codes come with their reasons, for permitted users only.
  A warning is acknowledged only with a reason, and the acknowledgement carries over to the next file of
  the employer for the same employee (061).
- `DummyDataTests`: Administration > Dummy Data is SuperAdmin only; one employer downloads a
  workbook, several a zip, described by the summary header; a new name gets a made-up EIN no
  employer has; nothing is written to the database.
- `SearchTests`: the Ctrl+K search (script 059) finds pages from the user's menu, permitted employers by
  name or EIN, and the selected employer's employees by name or last four SSN digits (never the full SSN);
  nothing outside the user's scope.
- `SystemHealthTests`: Administration > System Health for SuperAdmin and Admin only; slow pages count
  together whatever the id in the address; information log lines are saved as Info (062).
- `FilingDeadlineTests`: an administrator adds and removes a deadline, every role sees it on the dashboard
  and in the Calendar but cannot edit it, and the Employer dashboard counts down to the IRS dates (063).
- `YearOverYearTests`: Penalty Risk compares the year with the last one, and the dashboard lists the
  employers whose estimated penalty grew (064).
- `BugFixTests`: errors System Health showed recurring: reading the penalty risk changes nothing (it
  generated codes, and Risk Analysis asked for employer 0), the employer details tab needs an employer the
  user may see (it crashed without one and answered any), View As is audited (its entry was refused).
- `ImportPipelineTests` (third batch, `ImportPipelineTests.Batch3.cs`): an SSN another employer already has
  is a warning (EMP-160) and on Reports > Duplicate SSNs by its last four digits (066); trying a rule
  counts what it would flag and changes nothing (067); a second file's change is on the employee's
  history, field by field, and on the employer's Timeline, for staff only (068, 069); a file whose
  column has the employer's own name is mapped once and then read (073); the uploader hears when a
  file is ready, nobody else can mark it read, and a switched-off event stays quiet (070).
- `BatchThreeTests`: safe harbor advice finds offers no safe harbor covers and changes nothing (071);
  State Filings counts residents, covered and self-insured, and exports only employers the user may
  see (072); deadline reminders go once at 14 and 7 days, the health digest to admins only (070, in a
  rolled-back transaction); codes locked and unlocked in bulk only for reachable employees (074);
  bulk assignment adds to an account manager's employers and keeps the ones they had; grid state per user.
- `DuoSignInTests`: two-step sign-in with Duo, through a fake Duo (`FakeDuo`; nothing leaves the
  machine): someone who turned it on signs in only after Duo approves, a stale or denied answer is
  refused, Duo asks only those who turned it on (administrators included), and when Duo is down admins
  wait and others get in; every user turns it on and off on Account Settings, even before Duo is set
  up. Without Duo keys sign-in is unchanged.
- `SettingsPageTests`: Account Settings lists where the user is signed in and "Sign out everywhere
  else" ends the other sessions only (075); the notification choices are there; the API key and
  Connections placeholders and self-deactivation are gone.
- `SmallFixTests`, `UiFixTests`: issues found by earlier reviews, kept fixed (among them: the
  Employee list without a search lists everyone; menu links carry no `?menuId=`, and Firm and Broker
  Management each open and highlight their own entry).

Test people and employers are named so they cannot be mistaken for real ones (last name
`ACA360TESTS`, SSNs starting with 9, employers `ACA360TESTS IMPORT …` with EIN 45-9…) and are
removed like the logins. Nothing leaves the machine: e-mail goes to a fake mail service that
only records it, and files are written to the temp folder.

Maintenance mode must be off locally, or pages answer with the maintenance page.

## Browser tests (`Integration/BrowserTests.cs`)

The app on a real HTTPS port, driven by **your installed Google Chrome** (headless, through
Playwright; nothing to download). Skipped when Chrome or the local database is missing.

- No script errors on the main pages.
- The guided tour: starts by itself once, dims the page, steps forward, is remembered as seen,
  and can be replayed from the profile menu; it fits a phone screen.
- The AJAX grid (User Accounts): search, paging, page size, sorting.
- The Employee split view: "1 of N" counts the whole filtered list, Next moves on, and an
  empty list leaves the page's jQuery plugins working.
- The pages that used to load jQuery a second time (reports, User Accounts, System settings):
  no script errors, select2 and DataTables set up, settings buttons wired.
- Dummy Data: choose an employer, generate, and the workbook downloads with its summary.
- The Error Triage Corrections tab, the Import Analysis "What changed" tab, "Why these codes?" on Code
  Review, and Ctrl+K search, with screenshots. System Health, Filing Deadlines and its dialog, the
  dashboard countdown, deadlines in the Calendar (read-only), and the reason asked for when a warning is
  acknowledged. The third batch: the bell and notification choices, trying a rule, Duplicate SSNs, State
  Filings, safe harbor advice, the Code Review, employer list and Files bulk bars, the Columns menu
  (remembered after a reload), the employer Timeline and employee History tabs, and Map columns.
- No page scrolls sideways on a phone (390 px) or a tablet (768 px): the main pages, every staff
  page AllPagesTests opens, the employee, employer and Tracker employer detail pages, and the
  employee-facing pages (sign-in, the e-mailed form link, the Employee Portal). A failure names
  the elements that stick out. Content cut off inside a box is not detected: look at the screenshots.

Screenshots for a person to look at are saved in `ACA360.Tests/bin/<configuration>/net8.0/screenshots`.

## On GitHub (`.github/workflows/tests.yml`)

Every push and pull request builds the solution and runs these tests on a Windows runner. It
has no database or Chrome, so only the unit tests run there and the rest show as skipped. Run
the whole suite here before releasing.

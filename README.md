# 🏥 ACA360 – ACA Compliance & Reporting Platform

ACA360 is an enterprise-grade Affordable Care Act (ACA) Compliance Platform designed to manage, validate, and report employer health insurance coverage data. It simplifies the IRS reporting process (Forms **1094-C** and **1095-C**) by transforming messy client payroll data into clean, compliant filings through a smart validation and correction workflow.

---

## 📌 Table of Contents

1. [Project Overview](#project-overview)  
2. [Technologies Used](#technologies-used)  
3. [User Roles & Workflows](#user-roles--workflows)  
4. [Data Processing Pipeline](#data-processing-pipeline)  
5. [Architecture](#architecture)  
6. [Key Advantages](#key-advantages)  
7. [Known Challenges](#known-challenges)  
8. [Future Enhancements](#future-enhancements)

---

## 🎯 Project Overview

The primary goal of ACA360 is to **streamline ACA reporting** and **ensure regulatory compliance**.  
The system:

✔ Validates data structure & business rules  
✔ Prevents invalid data from entering production  
✔ Generates IRS-compliant outputs  
✔ Supports employers, brokers & internal ACA teams  

---

## 🧰 Technologies Used

| Category | Technology | Purpose |
|---------|------------|---------|
| Framework | **ASP.NET Core MVC (C#)** | Server-side web application |
| Database | **SQL Server** | Data storage & Stored Procedures |
| ORM/Data Access | **Dapper** | High-performance data mapping |
| Background Processing | **Hangfire** | Long-running jobs (file parsing, emails) |
| File Processing | **NPOI** | Excel parsing without Office dependency |
| UI | Razor Views, Bootstrap 5, jQuery/AJAX | Responsive + interactive frontend |
| Security | Role & Permission-based authorization | Fine-grained access control |

---

## 👥 User Roles & Workflows

### Internal Users
- SuperAdmin  
- Admin  
- ACA Director  
- Account Managers  
- Data Analysts  

### External Users
- Employers (view-only for own records)  
- Brokers (managed clients only)  

### Smart Login Flow
- If user has **one assigned employer** ➝ Redirect to dashboard  
- If multiple ➝ Employer selection screen

---

## 🔄 Data Processing Pipeline

### **Stage A — Ingestion & Parsing**
1. Upload Excel file (.xlsx) — direct under 10 MB, browser-to-Azure via SAS above it
2. SHA-256 deduplication against prior uploads
3. Structure validation reads **only the header row** (OpenXML SAX), so memory stays flat regardless of file size
4. Row extraction starts once a Data Analyst owns the file, streaming into staging in batches of 1,000 via table-valued parameters

### **Stage B — Staging & Error Triage**
- Bulk insert into staging tables
- Execute **115 validation rules** via stored procedures, driven by the `ValidationRules` table
- Errors fixed using **real-time Triage Dashboard**
- AJAX update & auto re-validation

### **Stage C — Import**
- Merges staging into the live tables, keyed on SSN + employer for employees and
  on plan name for plans and premiums
- Child rows (enrolment, payroll, dependents, hire spans, status) are inserted
  where absent rather than replaced, so a partial file adds to what is already there
- `Overwrite` mode clears the employee's child rows for that filing year first
- Import is followed by per-employer recalculation: 1095-C codes → flag engine →
  penalties → 1094-C monthly counts

> **Note on date-range scoping.** `UploadedFileLog` carries `PeriodStartDate` and
> `PeriodEndDate`, and earlier revisions of this document described the import as
> restricted to that window. It is not — the columns are recorded but the import
> does not read them, and the only consumer anywhere is the employer coverage map
> on the 360 dashboard. Treat a partial-year file as updating the whole year for
> the employees it contains.

---

## 🧱 Architecture

The solution uses a **Service-Repository Pattern**:

- **Controllers** → Endpoint handling + routing
- **Services** → Business logic (e.g., EmployeeService)
- **Repositories** → Dapper-based SQL calls
- **Dynamic Template System** → Column mapping configurable in DB

Example of secure actions:

```csharp
[AuthorizePermission(Permissions.Employer.Edit)]
public IActionResult Edit(int id) { ... }

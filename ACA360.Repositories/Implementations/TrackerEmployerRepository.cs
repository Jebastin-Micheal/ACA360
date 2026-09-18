using ACA360.Core.Models.Tracker;
using Dapper;
using Microsoft.Extensions.Configuration;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using ACA360.Repositories.Interfaces;
namespace ACA360.Repositories.Implementations
{
    public class TrackerEmployerRepository : ITrackerEmployerRepository
    {
        private readonly string _connStr;
        public TrackerEmployerRepository(IConfiguration config)
        {
            _connStr = config.GetConnectionString("DefaultConnection") ?? throw new InvalidOperationException(
                    "ConnectionStrings:DefaultConnection is missing from configuration."); ;
        }
        private IDbConnection Db => new SqlConnection(_connStr);
        // Helper function for date formatting
        private string FormatDate(object dateObj)
        {
            if (dateObj == null || dateObj == DBNull.Value) return null;
            if (DateTime.TryParse(dateObj.ToString(), out DateTime dt))
            {
                return dt.ToString("yyyy-MM-dd");
            }
            return null;
        }

        private string ParseMonthFromDate(object val)
        {
            if (val == null || val == DBNull.Value) return null;
            string s = val.ToString();
            // If it's a full date (e.g. 2023-01-15), extract the month
            if (DateTime.TryParse(s, out DateTime dt))
            {
                return dt.Month.ToString();
            }
            // Otherwise return as-is (might be "1" or "January")
            return s;
        }
        // 1. GET LIST (JOIN Core + Extension + Firm)
        // ============================================================
        // PASTE THIS METHOD into TrackerEmployerRepository.cs
        // replacing the existing GetListAsync implementation.
        // All other methods in the file remain unchanged.
        // ============================================================

        public async Task<(IEnumerable<TrackerEmployerItem>, int)> GetListAsync(EmployerFilterModel filter)
        {
            using (var db = Db)
            {
                // Cap page size to prevent accidental data dumps
                filter.PageSize = Math.Min(filter.PageSize > 0 ? filter.PageSize : 15, 100);

                var p = new DynamicParameters();

                // ── Search ───────────────────────────────────────────
                p.Add("@SearchText", filter.SearchText);
                // ── Employer (Main → Affiliate cascade) ──────────────────
                p.Add("@MainEmployerId", filter.MainEmployerId);
                p.Add("@AffiliateEmployerId", filter.AffiliateEmployerId);
                // ── Organization ─────────────────────────────────────
                p.Add("@FirmId", filter.FirmId);
                p.Add("@BrokerId", filter.BrokerId);
                p.Add("@State", filter.State);
                p.Add("@FilingYear", filter.FilingYear);

                // ── Team ─────────────────────────────────────────────
                p.Add("@AcctManagerId", filter.AcctManagerId);
                p.Add("@DataAnalystId", filter.DataAnalystId);
                p.Add("@SalesRepId", filter.SalesRepId);

                // ── Configuration ─────────────────────────────────────
                p.Add("@FundingType", filter.FundingType);
                p.Add("@FormType", filter.FormType);
                p.Add("@LevelOfComplexity", filter.LevelOfComplexity);
                p.Add("@IndustryId", filter.IndustryId);

                // ── Flags ─────────────────────────────────────────────
                p.Add("@IsPriority", filter.IsPriority);
                p.Add("@NoAutoRenewal", filter.NoAutoRenewal);
                p.Add("@SFTP", filter.SFTP);
                p.Add("@AnnualCOI", filter.AnnualCOI);

                // ── Pagination / sorting ──────────────────────────────
                p.Add("@SortColumn", filter.SortColumn ?? "EmployerName");
                p.Add("@SortOrder", filter.SortOrder ?? "ASC");
                p.Add("@PageNumber", filter.Page);
                p.Add("@PageSize", filter.PageSize);

                var res = await db.QueryAsync<TrackerEmployerItem>(
                    "sp_Tracker_GetEmployerList", p,
                    commandType: CommandType.StoredProcedure);

                return (res, res.FirstOrDefault()?.TotalCount ?? 0);
            }
        }

        // Main employers — companyId is null or 0
        public async Task<IEnumerable<AffiliateEmployerItem>> GetMainEmployersForDropdownAsync()
        {
            using (var db = Db)
            {
                return await db.QueryAsync<AffiliateEmployerItem>(@"
    SELECT 
        id   AS EmployerId,
        name AS AffiliateName,
        taxid AS EIN
    FROM Employer
    WHERE IsDeleted = 0
      AND (companyId IS NULL OR companyId = 0)
    ORDER BY name",
                    commandType: CommandType.Text);
            }
        }

        public async Task<IEnumerable<AffiliateEmployerItem>> GetAffiliatesForDropdownAsync(int mainEmployerId)
        {
            using (var db = Db)
            {
                return await db.QueryAsync<AffiliateEmployerItem>(@"
    SELECT 
        id        AS EmployerId,
        name      AS AffiliateName,
        taxid     AS EIN,
        companyId AS ParentEmployerId
    FROM Employer
    WHERE IsDeleted = 0
      AND companyId = @MainEmployerId
    ORDER BY name",
                    new { MainEmployerId = mainEmployerId },
                    commandType: CommandType.Text);
            }
        }

        // 2. GET BY ID (JOIN Core + Extension)
        public async Task<TrackerEmployerItem?> GetByIdAsync(int id)
        {
            try
            {
                using (var db = Db)
                {
                    return await db.QueryFirstOrDefaultAsync<TrackerEmployerItem>(
                        "sp_GetTrackerEmployerById",
                        new { Id = id },
                        commandType: CommandType.StoredProcedure);
                }
            }
            catch (Exception ex)
            {
                throw new Exception($"Error fetching employer with ID {id}: {ex.Message}", ex);
            }
        }

        // 3. SAVE (Update BOTH Tables Transactionally)
        public async Task<int> SaveAsync(TrackerEmployerItem item, int currentUserId = 0)
        {
            using (var db = Db)
            {
                var p = new DynamicParameters();

                // -- BEHAVIOR FLAG -----------------------------------------
                p.Add("@SaveMode", 1); // Employer Form = full save

                // -- CORE: Employer Table ----------------------------------
                p.Add("@EmployerId", item.EmployerId);
                p.Add("@EmployerName", item.EmployerName);
                p.Add("@EIN", item.EIN);
                p.Add("@Address", item.Address);
                p.Add("@Address2", item.Address2);
                p.Add("@City", item.City);
                p.Add("@State", item.State);
                p.Add("@Zip", item.Zip);
                p.Add("@ContactName", item.ContactName);
                p.Add("@Phone", item.Phone);
                p.Add("@Email", item.Email);
                // AFTER ? — also check if it's a valid number
                var filingYear = int.TryParse(item.Filingyear, out int fy) && fy > 0
                    ? fy.ToString()
                    : DateTime.Now.Year.ToString();
                p.Add("@FilingYear", !string.IsNullOrEmpty(item.Filingyear)
                ? item.Filingyear
                : DateTime.Now.Year.ToString());
                // -- EXTENSION: Team / Identity ----------------------------
                p.Add("@FirmId", item.FirmId);
                p.Add("@BrokerId", item.BrokerId);
                p.Add("@AccountManagerId", item.AcctManagerId);
                p.Add("@SalesRepId", item.SalesRepId);
                p.Add("@IndustryId", item.IndustryId);
                p.Add("@DataTypeId", item.DataTypeId);
                p.Add("@DataAnalyst", item.DataAnalystId?.ToString() ?? item.DataAnalyst);
                p.Add("@Vendor", item.Vendor);
                // -- EXTENSION: Contacts -----------------------------------
                p.Add("@ACAContactId", item.ACAContactId);
                p.Add("@BillingContactId", item.BillingContactId);

                // -- EXTENSION: Broker Tab ---------------------------------
                p.Add("@BrokerContactId", item.BrokerContactId);
                p.Add("@BrokerPhone", item.BrokerPhone);
                p.Add("@BrokerEmail", item.BrokerEmail);
                p.Add("@BrokerLastContactDate", item.BrokerLastContactDate);
                p.Add("@BrokerInvolvement", item.BrokerInvolvement);
                // -- EXTENSION: Data Specifications ------------------------
                p.Add("@PlanYear", item.PlanYear);
                p.Add("@FundingType", item.FundingType);
                p.Add("@FormType", item.FormType);
                p.Add("@DataFrequency", item.DataFrequency);
                p.Add("@WaitingPeriod", item.WaitingPeriod);
                p.Add("@BandingType", item.BandingType);
                // -- EXTENSION: Other Details ------------------------------
                p.Add("@LevelOfComplexity", item.LevelOfComplexity);
                p.Add("@Communication", item.Communication);
                p.Add("@PlanTermination", item.PlanTermination);
                p.Add("@DataLoads", item.DataLoads);
                p.Add("@ConnectUser", item.ConnectUser);

                // -- EXTENSION: ACA Contact --------------------------------
                p.Add("@ACARole", item.ACARole);
                p.Add("@ACAContactPhone", item.ACAContactPhone);
                p.Add("@ACAContactEmail", item.ACAContactEmail);
                p.Add("@ACAContactName", item.ACAContactName);

                // -- EXTENSION: Pricing & Counts ---------------------------
                p.Add("@FormProposed", item.FormsProposed);
                //p.Add("@Form1095Process", item.Form1095Process);

                p.Add("@NumberOf1095Cs", item.NumberOf1095Cs);
                p.Add("@LateTerminationFee", item.LateTerminationFee);
                p.Add("@OverProposedFormCount", item.OverProposedFormCount);
                p.Add("@OverProposedFormCountPrice", item.OverProposedFormPrice);
                p.Add("@OverProposedTrackedCount", item.OverProposedTrackedCount);
                p.Add("@OverProposedTrackedPrice", item.OverProposedTrackedPrice);

                // -- EXTENSION: Checkboxes ---------------------------------
                p.Add("@IsPriority", item.IsPriority);
                p.Add("@ConditionalOffer", item.ConditionalOffer);
                p.Add("@SFTP", item.SFTP);
                p.Add("@AnnualPO", item.AnnualPO);
                p.Add("@AnnualCOI", item.AnnualCOI);
                p.Add("@NoAutoRenewal", item.NoAutoRenewal);
                p.Add("@HasSpecialMailing", item.HasSpecialMailing);
                p.Add("@SpecialPaymentDates", item.SpecialPaymentDates);
                p.Add("@CannotRenewal", item.CannotRenewal);

                try
                {
                    var result = await db.QueryFirstOrDefaultAsync<dynamic>(
                        "sp_AddEmployerInfoTracker_new",
                        p,
                        commandType: CommandType.StoredProcedure);

                    int savedEmployerId = (int)(result?.EmployerId ?? item.EmployerId);

                    if (savedEmployerId > 0)
                    {
                        await db.ExecuteAsync(
                            "sp_Sync_Tracker_To_Assignments",
                            new
                            {
                                EmployerId = savedEmployerId,
                                CurrentUserId = currentUserId
                            },
                            commandType: CommandType.StoredProcedure);
                    }

                    return savedEmployerId;
                }
                catch (SqlException ex) when (ex.Message.Contains("DUPLICATE_EIN"))
                {
                    throw new InvalidOperationException(ex.Message, ex);
                }
            }
        }
        //public async Task SaveAsync(TrackerEmployerItem item)
        //{
        //    using (var db = Db)
        //    {
        //        db.Open();
        //        using (var trans = db.BeginTransaction())
        //        {
        //            try
        //            {
        //                int empId = item.EmployerId;

        //                // ---------------------------------------------------------
        //                // A. Insert/Update CORE Table (Employer)
        //                // ---------------------------------------------------------
        //                if (empId == 0)
        //                {
        //                    var sqlCore = @"INSERT INTO Employer (name, taxid, address, city, state, zip, contactName, phoneNumber, CreatedDate, IsDeleted) 
        //                            VALUES (@Name, @EIN, @Addr, @City, @State, @Zip, @Contact, @Phone, GETUTCDATE(), 0);
        //                            SELECT CAST(SCOPE_IDENTITY() as int);";
        //                    empId = await db.ExecuteScalarAsync<int>(sqlCore, new
        //                    {
        //                        Name = item.EmployerName,
        //                        EIN = item.EIN,
        //                        Addr = item.Address,
        //                        City = item.City,
        //                        State = item.State,
        //                        Zip = item.Zip,
        //                        Contact = item.ContactName,
        //                        Phone = item.Phone
        //                    }, transaction: trans);
        //                }
        //                else
        //                {
        //                    var sqlCore = @"UPDATE Employer SET name=@Name, taxid=@EIN, address=@Addr, city=@City, state=@State, zip=@Zip, contactName=@Contact, phoneNumber=@Phone 
        //                            WHERE id=@Id";
        //                    await db.ExecuteAsync(sqlCore, new
        //                    {
        //                        Id = empId,
        //                        Name = item.EmployerName,
        //                        EIN = item.EIN,
        //                        Addr = item.Address,
        //                        City = item.City,
        //                        State = item.State,
        //                        Zip = item.Zip,
        //                        Contact = item.ContactName,
        //                        Phone = item.Phone
        //                    }, transaction: trans);
        //                }

        //                // ---------------------------------------------------------
        //                // B. Insert/Update EXTENSION Table (Tracker_EmployerExtension)
        //                // ---------------------------------------------------------

        //                // 1. Prepare Parameters (Mapping Model -> SQL Column Names)
        //                var p = new DynamicParameters();
        //                p.Add("@Id", empId);

        //                // Dropdowns (IDs)
        //                p.Add("@FirmId", item.FirmId);
        //                p.Add("@BrokerId", item.BrokerId);
        //                p.Add("@AcctMgr", item.AcctManagerId);
        //                p.Add("@SalesRep", item.SalesRepId);
        //                p.Add("@Industry", item.IndustryId);       // Maps to _IndustryId
        //                p.Add("@DataType", item.DataTypeId);       // Maps to _datatypeid

        //                // Text Fields
        //                p.Add("@PlanYear", item.PlanYear);
        //                p.Add("@Funding", item.FundingType);
        //                p.Add("@FormType", item.FormType);
        //                p.Add("@DataFreq", item.DataFrequency);
        //                p.Add("@Vendor", item.Vendor);             // Maps to _vendor
        //                p.Add("@Analyst", item.DataAnalyst);       // Maps to _dataAnalyst
        //                p.Add("@WaitPeriod", item.WaitingPeriod);  // Maps to WaitingPeriod
        //                p.Add("@Banding", item.BandingType);       // Maps to BandingType
        //                p.Add("@Complexity", item.LevelOfComplexity); // Maps to _LevelofComplexity
        //                p.Add("@DataLoads", item.DataLoads);       // Maps to _DataLoads
        //                p.Add("@Comm", item.Communication);        // Maps to _Communication
        //                p.Add("@BrokerInv", item.BrokerInvolvement); // Maps to _Broker_Involvement
        //                p.Add("@PlanTerm", item.PlanTermination);  // Maps to _PlanTermination

        //                // Pricing & Counts
        //                p.Add("@OverCount", item.OverProposedFormCount);           // _OverProposedFormCount
        //                p.Add("@OverPrice", item.OverProposedFormPrice);           // _OverProposedFormCountPrice
        //                p.Add("@OverTrackCount", item.OverProposedTrackedCount);   // _OverProposedTrackedEmployeeCount
        //                p.Add("@OverTrackPrice", item.OverProposedTrackedPrice);   // _OverProposedTrackedEmployeeCountPrice

        //                // Checkboxes (Bit Fields)
        //                p.Add("@Priority", item.IsPriority);
        //                p.Add("@SpecialMailing", item.HasSpecialMailing);
        //                p.Add("@NoRenewal", item.NoAutoRenewal);
        //                p.Add("@SFTP", item.SFTP);
        //                p.Add("@AnnualCOI", item.AnnualCOI);       // Maps to _AnnualCOI
        //                p.Add("@AnnualPO", item.AnnualPO);         // Maps to _AnnualPO
        //                p.Add("@CondOffer", item.ConditionalOffer); // Maps to _ConditionalOffer
        //                p.Add("@SpecDates", item.SpecialPaymentDates); // Maps to _SpecialPaymentDates


        //                // 2. Check if row exists
        //                var extExists = await db.ExecuteScalarAsync<bool>("SELECT COUNT(1) FROM Tracker_EmployerExtension WHERE EmployerId = @Id", new { Id = empId }, transaction: trans);

        //                if (!extExists)
        //                {
        //                    // INSERT (All Columns)
        //                    var sqlExt = @"INSERT INTO Tracker_EmployerExtension 
        //                           (EmployerId, firmid, brokerid, AcctManagerid, srepid, _IndustryId, _datatypeid,
        //                            _planyear, _fundingtype, _FormType, _DataFrequency, _vendor, _dataAnalyst,
        //                            WaitingPeriod, BandingType, _LevelofComplexity, _DataLoads, _Communication, _Broker_Involvement, _PlanTermination,
        //                            _OverProposedFormCount, _OverProposedFormCountPrice, _OverProposedTrackedEmployeeCount, _OverProposedTrackedEmployeeCountPrice,
        //                            Priority, _SpecialMailing, _noAutoRenewal, SFTP, _AnnualCOI, _AnnualPO, _ConditionalOffer, _SpecialPaymentDates) 
        //                           VALUES 
        //                           (@Id, @FirmId, @BrokerId, @AcctMgr, @SalesRep, @Industry, @DataType,
        //                            @PlanYear, @Funding, @FormType, @DataFreq, @Vendor, @Analyst,
        //                            @WaitPeriod, @Banding, @Complexity, @DataLoads, @Comm, @BrokerInv, @PlanTerm,
        //                            @OverCount, @OverPrice, @OverTrackCount, @OverTrackPrice,
        //                            @Priority, @SpecialMailing, @NoRenewal, @SFTP, @AnnualCOI, @AnnualPO, @CondOffer, @SpecDates)";

        //                    await db.ExecuteAsync(sqlExt, p, transaction: trans);
        //                }
        //                else
        //                {
        //                    // UPDATE (All Columns)
        //                    var sqlExt = @"UPDATE Tracker_EmployerExtension SET 
        //                           firmid=@FirmId, brokerid=@BrokerId, AcctManagerid=@AcctMgr, srepid=@SalesRep, _IndustryId=@Industry, _datatypeid=@DataType,
        //                           _planyear=@PlanYear, _fundingtype=@Funding, _FormType=@FormType, _DataFrequency=@DataFreq, _vendor=@Vendor, _dataAnalyst=@Analyst,
        //                           WaitingPeriod=@WaitPeriod, BandingType=@Banding, _LevelofComplexity=@Complexity, _DataLoads=@DataLoads, 
        //                           _Communication=@Comm, _Broker_Involvement=@BrokerInv, _PlanTermination=@PlanTerm,
        //                           _OverProposedFormCount=@OverCount, _OverProposedFormCountPrice=@OverPrice, 
        //                           _OverProposedTrackedEmployeeCount=@OverTrackCount, _OverProposedTrackedEmployeeCountPrice=@OverTrackPrice,
        //                           Priority=@Priority, _SpecialMailing=@SpecialMailing, _noAutoRenewal=@NoRenewal, SFTP=@SFTP, 
        //                           _AnnualCOI=@AnnualCOI, _AnnualPO=@AnnualPO, _ConditionalOffer=@CondOffer, _SpecialPaymentDates=@SpecDates
        //                           WHERE EmployerId=@Id";

        //                    await db.ExecuteAsync(sqlExt, p, transaction: trans);
        //                }

        //                trans.Commit();
        //            }
        //            catch
        //            {
        //                trans.Rollback();
        //                throw;
        //            }
        //        }
        //    }
        //}

        // 4. DELETE (Soft Delete Core Table)
        public async Task DeleteAsync(int id)
        {
            using (var db = Db)
            {
                await db.ExecuteAsync("UPDATE Employer SET IsDeleted=1 WHERE id=@Id", new { Id = id });
            }
        }

        // -- PORTAL LOGIN ------------------------------------------------
        // Employer portal credentials live in tbl_User with Role_ID = 13 (Employer)
        // and Ref_ID = the employer id. We reuse the same usp_User_Action SP and
        // BCrypt hashing used by UserService so the login validates identically
        // (AccountService verifies the BCrypt hash held in User_Password).
        private const int EmployerRoleId = 13;

        public async Task<(long UserId, string UserName)?> GetPortalUserByEmployerAsync(int employerId)
        {
            if (employerId <= 0) return null;
            using (var db = Db)
            {
                var row = await db.QueryFirstOrDefaultAsync<dynamic>(
                    "SELECT TOP 1 User_ID, User_Name FROM dbo.tbl_User WHERE Ref_ID = @Ref AND Role_ID = @Role ORDER BY User_ID DESC",
                    new { Ref = (long)employerId, Role = EmployerRoleId });

                if (row == null) return null;
                return ((long)row.User_ID, (string)row.User_Name);
            }
        }

        public async Task SavePortalLoginAsync(int employerId, string userName, string? plainPassword, long existingUserId)
        {
            using (var db = Db)
            {
                // Mirror UserService: hash a new password, otherwise keep the stored one.
                string? passwordToSave = null;
                if (!string.IsNullOrWhiteSpace(plainPassword))
                {
                    passwordToSave = BCrypt.Net.BCrypt.HashPassword(plainPassword);
                }
                else if (existingUserId > 0)
                {
                    passwordToSave = await db.QueryFirstOrDefaultAsync<string>(
                        "SELECT User_Password FROM dbo.tbl_User WHERE User_ID = @Id",
                        new { Id = existingUserId });
                }

                var p = new
                {
                    User_ID = existingUserId > 0 ? existingUserId.ToString() : "0",
                    User_Name = userName,
                    User_Password = passwordToSave,
                    Temp_Password = (string)null,
                    Role_ID = EmployerRoleId,
                    Flag = 1,                       // active login
                    Ref_ID = (long?)employerId,
                    Profile_Picture = (string)null,
                    isMFA = 0,
                    User_LandingPage = (int?)null
                };

                await db.ExecuteAsync("usp_User_Action", p, commandType: CommandType.StoredProcedure);
            }
        }
        // -- DROPDOWN DATA (15 result sets from SP) ------------------
        public async Task<EmployerDropdownDataModel> GetDropdownDataAsync(int employerId, int? brokerId = null, int? firmId = null)
        {
            var model = new EmployerDropdownDataModel();

            using (var db = Db)
            {
                var p = new DynamicParameters();
                p.Add("@Action", "GetDropdownData");
                p.Add("@employerid", employerId.ToString());
                p.Add("@isBilling", null);
                p.Add("@brokerid", brokerId);
                p.Add("@firmid", firmId);

                using (var multi = await db.QueryMultipleAsync(
                    "sp_GetDropdownDataEmployer_new1_tracker", p,
                    commandType: CommandType.StoredProcedure))
                {
                    // RS 1: Industry (_IndustryId, _industryName)
                    model.Industries = (await multi.ReadAsync<dynamic>())
                        .Select(r => new DropdownItem { Id = Convert.ToInt32(r._IndustryId), Name = (string)r._industryName })
                        .ToList();

                    // RS 2: DataType (_datatypeID, _datatypeName)
                    model.DataTypes = (await multi.ReadAsync<dynamic>())
                        .Select(r => new DropdownItem { Id = Convert.ToInt32(r._datatypeID), Name = (string)r._datatypeName })
                        .ToList();

                    // RS 3: Account Managers (Id, _name)
                    model.AccountManagers = (await multi.ReadAsync<dynamic>())
                        .Select(r => new DropdownItem { Id = Convert.ToInt32(r.Id), Name = (string)r._name })
                        .ToList();

                    // RS 4: Data Analysts (Id, _name)
                    model.DataAnalysts = (await multi.ReadAsync<dynamic>())
                        .Select(r => new DropdownItem { Id = Convert.ToInt32(r.Id), Name = (string)r._name })
                        .ToList();

                    // RS 5: Sales Reps (srepid, srepname)
                    model.SalesReps = (await multi.ReadAsync<dynamic>())
                        .Select(r => new DropdownItem { Id = Convert.ToInt32(r.srepid), Name = (string)r.srepname })
                        .ToList();

                    // RS 6: Firms (firmid, FirmName)
                    model.Firms = (await multi.ReadAsync<dynamic>())
                        .Select(r => new DropdownItem { Id = Convert.ToInt32(r.firmid), Name = (string)r.FirmName })
                        .ToList();

                    // RS 7: Brokers (brokerid, BrokerName)
                    model.Brokers = (await multi.ReadAsync<dynamic>())
                        .Select(r => new DropdownItem { Id = Convert.ToInt32(r.brokerid), Name = (string)r.BrokerName })
                        .ToList();

                    // RS 8: States (StateId, StateName)
                    model.States = (await multi.ReadAsync<dynamic>())
                        .Select(r => new DropdownItem { Id = Convert.ToInt32(r.StateId), Name = (string)r.StateName })
                        .ToList();

                    // RS 9: Affiliates — SKIP (read and discard)
                    await multi.ReadAsync<dynamic>();

                    // RS 10: General Contacts (tbl_Medtracker_Contactid, name, phone, email)
                    model.GeneralContacts = (await multi.ReadAsync<dynamic>())
                        .Select(r => new ContactDropdownItem
                        {
                            Id = Convert.ToInt32(r.tbl_Medtracker_Contactid),
                            Name = (string)(r.name ?? ""),
                            Phone = (string)(r.phone ?? ""),
                            Email = (string)(r.email ?? ""),
                            Connect_User = (string)(r._acausername ?? "")
                        }).ToList();

                    // RS 11: Billing Contacts (tbl_Medtracker_Contactid, name, phone, email)
                    model.BillingContacts = (await multi.ReadAsync<dynamic>())
                        .Select(r => new ContactDropdownItem
                        {
                            Id = Convert.ToInt32(r.tbl_Medtracker_Contactid),
                            Name = (string)(r.name ?? ""),
                            Phone = (string)(r.phone ?? ""),
                            Email = (string)(r.email ?? "")
                        }).ToList();

                    // RS 12: Broker Contacts (Broker_Contactid, name, phone, email)
                    model.BrokerContacts = (await multi.ReadAsync<dynamic>())
                        .Select(r => new ContactDropdownItem
                        {
                            Id = Convert.ToInt32(r.Broker_Contactid),
                            Name = (string)(r.name ?? ""),
                            Phone = (string)(r.phone ?? ""),
                            Email = (string)(r.email ?? "")
                        }).ToList();

                    // RS 13: States ACA (id, state, code)
                    model.StatesACA = (await multi.ReadAsync<dynamic>())
                        .Select(r => new StateACAItem
                        {
                            Id = Convert.ToInt32(r.id),
                            State = (string)r.state,
                            Code = (string)r.code
                        }).ToList();

                    // RS 14: Country ACA (id, country, code)
                    model.CountriesACA = (await multi.ReadAsync<dynamic>())
                        .Select(r => new CountryACAItem
                        {
                            Id = Convert.ToInt32(r.id),
                            Country = (string)r.country,
                            Code = (string)r.code
                        }).ToList();

                    // RS 15: Broker Entity Details (Phone, Email)
                    var brokerDetail = await multi.ReadFirstOrDefaultAsync<dynamic>();
                    if (brokerDetail != null)
                    {
                        model.BrokerPhone = (string)(brokerDetail.Phone ?? "");
                        model.BrokerEmail = (string)(brokerDetail.Email ?? "");
                    }
                }
            }

            return model;
        }
        // 5. SERVICES (Right Pane - Same as before)
        public async Task<IEnumerable<EmployerServiceItem>> GetServicesAsync(int empId, string planYear)
        {
            try
            {
                using (var db = Db)
                {
                    var p = new DynamicParameters();
                    p.Add("@Action", "List");
                    p.Add("@EmployerId", empId);
                    p.Add("@PlanYear", string.IsNullOrWhiteSpace(planYear) ? null : planYear);

                    var result = await db.QueryAsync<dynamic>(
                        "sp_ServiceMainDisplayList_tracker",
                        p,
                        commandType: System.Data.CommandType.StoredProcedure);

                    return result.Select(r => new EmployerServiceItem
                    {
                        EmployerServiceId = (int)r.serviceid,
                        EmployerId = empId,
                        ServiceName = r.servicename == null || Convert.IsDBNull(r.servicename) ? null : Convert.ToString(r.servicename),
                        Status = r._status == null || Convert.IsDBNull(r._status) ? null : Convert.ToString(r._status),
                        Pricing = r._pricing == null || Convert.IsDBNull(r._pricing) ? (decimal?)null : Convert.ToDecimal(r._pricing),
                        ProcessStep1095 = r._implementationprocess == null || Convert.IsDBNull(r._implementationprocess) ? null : Convert.ToString(r._implementationprocess),
                        FollowUpDate = r.FollowUpDate == null || Convert.IsDBNull(r.FollowUpDate) ? null : Convert.ToString(r.FollowUpDate),

                        PlanYear = r._planyear == null || Convert.IsDBNull(r._planyear) ? 0 : Convert.ToInt32(r._planyear),
                        MedcomCOBRA = r._offerCobra != null && !Convert.IsDBNull(r._offerCobra) && Convert.ToBoolean(r._offerCobra)
                    }).ToList();
                }
            }
            catch (Exception ex)
            {
                throw new Exception($"Error fetching services for Employer {empId}: {ex.Message}", ex);
            }
        }


        //public async Task<IEnumerable<EmployerServiceItem>> GetServicesAsync(int empId, string planYear)
        //{
        //    using (var db = Db)
        //    {
        //        var p = new DynamicParameters();
        //        p.Add("@Action", "List");
        //        p.Add("@EmployerId", empId);
        //        p.Add("@PlanYear", planYear);

        //        var result = await db.QueryAsync<dynamic>(
        //            "sp_ServiceMainDisplayList_tracker",
        //            p,
        //            commandType: CommandType.StoredProcedure);

        //        return result.Select(r => new EmployerServiceItem
        //        {
        //            EmployerServiceId = (int)r.serviceid,
        //            ServiceName = (string)r.servicename,
        //            Status = (string)r._status
        //        }).ToList();
        //    }
        //}
        // 5b. SERVICES GRID (Dashboard - full detail rows via 'GridList' action)
        public async Task<IEnumerable<EmployerServiceItem>> GetServicesGridAsync(int empId, string planYear)
        {
            using (var db = Db)
            {
                var p = new DynamicParameters();
                p.Add("@Action", "GridList");
                p.Add("@EmployerId", empId);
                p.Add("@PlanYear", planYear);

                var result = await db.QueryAsync<dynamic>(
                    "sp_ServiceMainDisplayList_tracker",
                    p,
                    commandType: CommandType.StoredProcedure);

                return result.Select(r => new EmployerServiceItem
                {
                    EmployerServiceId = (int)r.serviceid,
                    ServiceName = (string)r.servicename,
                    ServiceId = (int)r.servicelistid,
                    Status = (string)r._status,
                    PlanYear = r._planyear != null ? Convert.ToInt32(r._planyear) : 0,
                    PotentialPenalty = (string)r._potentialpenalty,
                    SafeHarbor = (string)r._safeharbor,
                    ProposalSentDate = FormatDate(r._proposalsent),
                    ProposalSignedDate = FormatDate(r._proposalreturned),
                    ContractSentDate = FormatDate(r._contractsent),
                    ContractSignedDate = FormatDate(r._contractsigned),
                    ProcessStep1095 = (string)r._implementationprocess,
                    LastUpdated1095 = FormatDate(r._implementationproclastupdated),
                    NextFollowUp1095 = FormatDate(r.FollowUpDate),
                    FTETrackingStep = (string)r._1094processstep,
                    LastUpdatedFTE = FormatDate(r.FTETrackingStep_lastupdated),
                    ActualEmployeesTracked = r._actualNumberOfEmployeesTracked != null ? Convert.ToInt32(r._actualNumberOfEmployeesTracked) : (int?)null,
                    StateFilingStep = (string)r._stateFilingprocessstep,
                    LastUpdatedState = FormatDate(r.StateFilingProcessStepUpdated),
                    MostRecentFollowUpdate = FormatDate(r._followupdate),
                    Pricing = r._pricing != null ? Convert.ToDecimal(r._pricing) : (decimal?)null,
                    InitialBill = r._initialbill != null ? Convert.ToDecimal(r._initialbill) : (decimal?)null,
                    FinalBill = r._finalbill != null ? Convert.ToDecimal(r._finalbill) : (decimal?)null,
                    NextFollowUpFTE = FormatDate(r.FTETrackingStep_FollowUpDate),
                }).ToList();
            }
        }
        public async Task<int> SaveServiceAsync(EmployerServiceItem item, string planyear)
        {
            try
            {
                using (var db = Db)
                {
                    bool isNew = item.EmployerServiceId == 0;
                    var oldRecord = isNew ? null : await GetServiceByIdAsync(item.EmployerServiceId, planyear);
                    int effectivePlanYear = int.TryParse(planyear, out int parsedPlanYear)? parsedPlanYear: item.PlanYear;
                    var p = new DynamicParameters();
                    p.Add("@Action", isNew ? "AddService" : "UpdateServiceInfo");
                    p.Add("@EmployerId", item.EmployerId);
                    p.Add("@PlanYear", effectivePlanYear);
                    p.Add("@ServiceListId", item.ServiceId);
                    p.Add("@ServiceId", item.EmployerServiceId);
                    p.Add("@Status", item.Status);
                    p.Add("@OfferCobra", item.MedcomCOBRA);
                    p.Add("@ACAOverageBill", (int?)item.ACAOverageBill);
                    p.Add("@FTEOverageBill", (int?)item.FTEOverageBill);
                    p.Add("@PotentialPenalty", item.PotentialPenalty);
                    p.Add("@SafeHarbor", item.SafeHarbor);
                    p.Add("@ActualNumberOfEmployeesTracked", item.ActualEmployeesTracked?.ToString());
                    p.Add("@EmployeesTracked", item.EmployeesTracked?.ToString());
                    p.Add("@AdditionalInvoiceAmount", item.AdditionalInvoiceAmount);
                    p.Add("@Required1312017", item.Required131);
                    p.Add("@ProposalSent", item.ProposalSentDate);
                    p.Add("@ProposalReturned", item.ProposalSignedDate);
                    p.Add("@ContractSent", item.ContractSentDate);
                    p.Add("@ContractSigned", item.ContractSignedDate);
                    p.Add("@FollowUpDate", item.FollowUpDate);
                    p.Add("@ImplementationProcess", item.ProcessStep1095);
                    p.Add("@ProcessStep1094", item.FTETrackingStep);
                    p.Add("@StateFilingProcessStep", item.StateFilingStep);
                    p.Add("@ImplementationProcessLastUpdated",
                        string.IsNullOrEmpty(item.LastUpdated1095) ? null : (object)item.LastUpdated1095); 
                    p.Add("@FTEProcessStepLastUpdated",
                        string.IsNullOrEmpty(item.LastUpdatedFTE) ? null : (object)item.LastUpdatedFTE);
                    p.Add("@StateFilingProcessStepLastUpdate",
                        string.IsNullOrEmpty(item.LastUpdatedState) ? null : (object)item.LastUpdatedState); 
                    p.Add("@NextFollowUp1095", item.NextFollowUp1095);
                    p.Add("@NextFollowUpFTE", item.NextFollowUpFTE);
                    p.Add("@NextFollowUpState", item.NextFollowUpState);
                    p.Add("@MonthInitialData", item.MonthInitialData);
                    p.Add("@RenewalDate", item.RenewalDate);
                    p.Add("@Payer", item.Payer);
                    p.Add("@Pricing", (double?)item.Pricing);
                    p.Add("@InitialBill", item.InitialBill);
                    p.Add("@FinalBill", item.FinalBill);
                    p.Add("@XNotes", item.Notes);
                    p.Add("@First50InvoiceDate", item.FirstInvoiceDate);
                    p.Add("@Final50InvoiceDate", item.FinalInvoiceDate);
                    p.Add("@SOSAmount", item.SOSAmount);
                    p.Add("@ExtensionFiled", item.ExtensionFiled);
                    p.Add("@AuditBy", item.AuditBy);
                    p.Add("@AuditDate", item.AuditDate);
                    p.Add("@NumMailedForms", item.FormsMailed?.ToString());
                    p.Add("@WillNotSignSA", item.WillNotSignSA);
                    p.Add("@CDHPClient", item.CDHPClient);
                    p.Add("@ReceiptId", item.ReceiptID);
                    p.Add("@AffiliateId", item.AffiliateId);
                    p.Add("@OldImplementationProcess", oldRecord?.ProcessStep1095);
                    p.Add("@OldProcessStep1094", oldRecord?.FTETrackingStep);
                    p.Add("@OldStateFilingProcessStep", oldRecord?.StateFilingStep);

                    if (isNew)
                    {
                        var result = await db.QueryFirstOrDefaultAsync<int?>(
                            "sp_ServiceMainDisplayList_tracker", p,
                            commandType: CommandType.StoredProcedure);
                        return result ?? 0;
                    }
                    else
                    {
                        await db.ExecuteAsync("sp_ServiceMainDisplayList_tracker", p,
                            commandType: CommandType.StoredProcedure);
                        return item.EmployerServiceId;
                    }
                }
            }
            catch (Exception ex)
            {
                throw new Exception($"Error saving service: {ex.Message}", ex);
            }
        }

        public async Task<IEnumerable<dynamic>> GetServiceDropdownAsync()
        {
            using (var db = Db)
            {
                return await db.QueryAsync(
                    "sp_GetDropdownDataService",
                    new { Action = "GetDropdownData" },
                    commandType: CommandType.StoredProcedure
                );
            }
        }
        //public async Task<EmployerServiceItem> GetServiceByIdAsync(int id)
        //{
        //    using (var db = Db)
        //    {
        //        var p = new DynamicParameters();
        //        p.Add("@Action", "Detail");
        //        p.Add("@ServiceId", id);

        //        // Fetch basic info first to get EmployerId and PlanYear
        //        var basicInfo = await db.QueryFirstOrDefaultAsync<dynamic>(
        //            "SELECT EmployerId, ServiceId FROM Tracker_EmployerService WHERE EmployerServiceId = @Id", new { Id = id });

        //        if (basicInfo == null) return null;

        //        var catalogInfo = await db.QueryFirstOrDefaultAsync<dynamic>(
        //            "SELECT TaxYear FROM Tracker_ServiceCatalog WHERE ServiceId = @Id", new { Id = (int)basicInfo.ServiceId });

        //        p.Add("@EmployerId", (int)basicInfo.EmployerId);
        //        p.Add("@PlanYear", (int)catalogInfo.TaxYear);

        //        var r = await db.QuerySingleOrDefaultAsync<dynamic>("sp_ServiceMainDisplayList_tracker", p, commandType: CommandType.StoredProcedure);

        //        if (r == null) return null;

        //        return new EmployerServiceItem
        //        {
        //            EmployerServiceId = (int)r.serviceid,
        //            EmployerId = (int)basicInfo.EmployerId,
        //            ServiceId = (int)r.servicelistid,
        //            ServiceName = (string)r.servicename,
        //            Status = (string)r._status,
        //            PlanYear = (int)catalogInfo.TaxYear,

        //            ProposalSentDate = FormatDate(r._proposalsent),
        //            ProposalSignedDate = FormatDate(r._proposalreturned),
        //            ContractSentDate = FormatDate(r._contractsent),
        //            ContractSignedDate = FormatDate(r._contractsigned),

        //            ProcessStep1095 = (string)r._implementationprocess,
        //            LastUpdated1095 = FormatDate(r._implementationproclastupdated),
        //            NextFollowUp1095 = FormatDate(r.FollowUpDate),

        //            FTETrackingStep = (string)r._1094processstep,
        //            LastUpdatedFTE = FormatDate(r.FTETrackingStep_lastupdated),
        //            NextFollowUpFTE = FormatDate(r.FTETrackingStep_nextfollowup),

        //            StateFilingStep = (string)r._stateFilingprocessstep,
        //            LastUpdatedState = FormatDate(r.StateFilingProcessStepUpdated),
        //            NextFollowUpState = FormatDate(r.StateFiling_FollowUpDate),

        //            ActualEmployeesTracked = r._actualNumberOfEmployeesTracked != null ? Convert.ToInt32(r._actualNumberOfEmployeesTracked) : (int?)null,
        //            PotentialPenalty = (string)r._potentialpenalty,
        //            SafeHarbor = (string)r._safeharbor,
        //            MedcomCOBRA = r._offerCobra != null && Convert.ToBoolean(r._offerCobra),
        //            ACAOverageBill = r._ACAOverageBill != null ? Convert.ToDecimal(r._ACAOverageBill) : (decimal?)null,
        //            FTEOverageBill = r._FTEOverageBill != null ? Convert.ToDecimal(r._FTEOverageBill) : (decimal?)null,
        //            EmployeesTracked = r._employeesTracked != null ? Convert.ToInt32(r._employeesTracked) : (int?)null,
        //            FormsMailed = r._NumMailedForms != null ? Convert.ToInt32(r._NumMailedForms) : (int?)null,
        //            Required131 = r._required1312017 != null && Convert.ToBoolean(r._required1312017),
        //            WillNotSignSA = r._WillNotSignSA != null && Convert.ToBoolean(r._WillNotSignSA),

        //            MonthInitialData = ParseMonthFromDate(r._dsheetrecieved),
        //            DataSheetReceivedDate = FormatDate(r._dsheetrecieved),
        //            SpecialDateDeadline = FormatDate(r._specialdatedeadline),
        //            RenewalDate = FormatDate(r._renewaldate),
        //            MostRecentFollowUpdate = FormatDate(r._followupdate),
        //            AdditionalInvoiceAmount = r._additionalInvoiceAmount != null ? Convert.ToDecimal(r._additionalInvoiceAmount) : (decimal?)null,
        //            InitialBill = r._initialbill != null ? Convert.ToDecimal(r._initialbill) : (decimal?)null,
        //            FinalBill = r._finalbill != null ? Convert.ToDecimal(r._finalbill) : (decimal?)null,
        //            Payer = (string)r._payer,
        //            Pricing = r._pricing != null ? Convert.ToDecimal(r._pricing) : (decimal?)null,
        //            Notes = (string)r._xnotes,
        //            FirstInvoiceDate = FormatDate(r._First50InvoiceDate),
        //            FinalInvoiceDate = FormatDate(r._Final50InvoiceDate),
        //            SOSAmount = r._sosamount != null ? Convert.ToDecimal(r._sosamount) : (decimal?)null,
        //            ExtensionFiled = FormatDate(r._entensionFiled),
        //            AuditBy = (string)r._auditBy,
        //            AuditDate = FormatDate(r._auditDate),
        //            FollowUpDate = FormatDate(r._followUpDateMain),

        //            ReceiptID = (string)r._ReceiptID
        //        };
        //    }
        //}
        public async Task<EmployerServiceItem> GetServiceByIdAsync(int id, string planyear)
        {
            try
            {
                using (var db = Db)
                {
                    var p = new DynamicParameters();
                    p.Add("@Action", "Detail");
                    p.Add("@ServiceId", id);

                    // Fetch basic info first to get EmployerId and PlanYear
                    var basicInfo = await db.QueryFirstOrDefaultAsync<dynamic>(
                        "SELECT EmployerId, ServiceId FROM Tracker_EmployerService WHERE EmployerServiceId = @Id", new { Id = id });

                    if (basicInfo == null) return null;

                    int catalogInfo = Convert.ToInt32(planyear);

                    p.Add("@EmployerId", (int)basicInfo.EmployerId);
                    p.Add("@PlanYear", planyear);

                    var r = await db.QuerySingleOrDefaultAsync<dynamic>("sp_ServiceMainDisplayList_tracker", p, commandType: CommandType.StoredProcedure);

                    if (r == null) return null;

                    return new EmployerServiceItem
                    {
                        EmployerServiceId = (int)r.serviceid,
                        EmployerId = (int)basicInfo.EmployerId,
                        ServiceId = (int)r.servicelistid,
                        ServiceName = (string)r.servicename,
                        Status = (string)r._status,
                        PlanYear = (int)catalogInfo,

                        ProposalSentDate = FormatDate(r._proposalsent),
                        ProposalSignedDate = FormatDate(r._proposalreturned),
                        ContractSentDate = FormatDate(r._contractsent),
                        ContractSignedDate = FormatDate(r._contractsigned),

                        ProcessStep1095 = (string)r._implementationprocess,
                        LastUpdated1095 = FormatDate(r._implementationproclastupdated),
                        NextFollowUp1095 = FormatDate(r.FollowUpDate),

                        FTETrackingStep = (string)r._1094processstep,
                        LastUpdatedFTE = FormatDate(r.FTETrackingStep_lastupdated),
                        NextFollowUpFTE = FormatDate(r.FTETrackingStep_nextfollowup),

                        StateFilingStep = (string)r._stateFilingprocessstep,
                        LastUpdatedState = FormatDate(r.StateFilingProcessStepUpdated),
                        NextFollowUpState = FormatDate(r.StateFiling_FollowUpDate),

                        ActualEmployeesTracked = r._actualNumberOfEmployeesTracked != null ? Convert.ToInt32(r._actualNumberOfEmployeesTracked) : (int?)null,
                        PotentialPenalty = (string)r._potentialpenalty,
                        SafeHarbor = (string)r._safeharbor,
                        MedcomCOBRA = r._offerCobra != null && Convert.ToBoolean(r._offerCobra),
                        ACAOverageBill = r._ACAOverageBill != null ? Convert.ToDecimal(r._ACAOverageBill) : (decimal?)null,
                        FTEOverageBill = r._FTEOverageBill != null ? Convert.ToDecimal(r._FTEOverageBill) : (decimal?)null,
                        EmployeesTracked = r._employeesTracked != null ? Convert.ToInt32(r._employeesTracked) : (int?)null,
                        FormsMailed = r._NumMailedForms != null ? Convert.ToInt32(r._NumMailedForms) : (int?)null,
                        Required131 = r._required1312017 != null && Convert.ToBoolean(r._required1312017),
                        WillNotSignSA = r._WillNotSignSA != null && Convert.ToBoolean(r._WillNotSignSA),
                        CDHPClient = r.CDHPClient != null && Convert.ToBoolean(r.CDHPClient),
                        DataSheetReceivedDate = ParseMonthFromDate(r._dsheetrecieved),
                        MonthInitialData = ParseMonthFromDate(r._dataSheetReceivedDate),
                        SpecialDateDeadline = FormatDate(r._specialdatedeadline),
                        RenewalDate = FormatDate(r._renewaldate),
                        MostRecentFollowUpdate = FormatDate(r._followupdate),
                        AdditionalInvoiceAmount = r._additionalInvoiceAmount != null ? Convert.ToDecimal(r._additionalInvoiceAmount) : (decimal?)null,
                        InitialBill = r._initialbill != null ? Convert.ToDecimal(r._initialbill) : (decimal?)null,
                        FinalBill = r._finalbill != null ? Convert.ToDecimal(r._finalbill) : (decimal?)null,
                        Payer = (string)r._payer,
                        Pricing = r._pricing != null ? Convert.ToDecimal(r._pricing) : (decimal?)null,
                        Notes = (string)r._xnotes,
                        FirstInvoiceDate = FormatDate(r._First50InvoiceDate),
                        FinalInvoiceDate = FormatDate(r._Final50InvoiceDate),
                        SOSAmount = r._sosamount != null ? Convert.ToDecimal(r._sosamount) : (decimal?)null,
                        ExtensionFiled = FormatDate(r._entensionFiled),
                        AuditBy = (string)r._auditBy,
                        AuditDate = FormatDate(r._auditDate),
                        FollowUpDate = FormatDate(r._followUpDateMain),

                        ReceiptID = (string)r._ReceiptID
                    };
                }
            }
            catch (Exception ex)
            {
                // Depending on your application's logging strategy, you can either log the error here
                // or throw a wrapped exception to be handled by higher-level middleware/controllers.
                throw new Exception($"Error fetching employer service with ID {id}: {ex.Message}", ex);
            }
        }
        public async Task DeleteServiceAsync(int id)
        {
            using (var db = Db)
            {
                await db.ExecuteAsync("UPDATE Tracker_EmployerService SET IsDeleted = 1 WHERE EmployerServiceId = @Id", new { Id = id });
            }
        }

        // -- AFFILIATES ------------------------------------------------
        // Uses [Employer] table only — no Tracker_Affiliate table.
        //   companyId IS NULL or 0  ?  Primary Employer (root)
        //   companyId = X           ?  Affiliate of employer X

        // 6a. GET full family list
        // Resolves @RootId first so it works whether @EmployerId is
        // the primary or an affiliate (sibling).
        public async Task<IEnumerable<AffiliateEmployerItem>> GetAffiliatesAsync(int employerId)
        {
            const string sql = @"
                DECLARE @RootId INT;
                SELECT @RootId = CASE
                                     WHEN companyId IS NULL OR companyId = 0 THEN id
                                     ELSE companyId
                                 END
                FROM   [dbo].[Employer]
                WHERE  id = @EmployerId AND IsDeleted != 1;

                SELECT
                    emp.id                                              AS EmployerId,
                    emp.id                                              AS AffiliateId,
                    emp.companyId                                       AS ParentEmployerId,
                    emp.[name]                                          AS AffiliateName,
                    emp.taxid                                           AS EIN,
                    emp.[address]                                       AS [Address],
                    emp.city                                            AS City,
                    emp.[state]                                         AS [State],
                    emp.zip                                             AS Zip,
                    emp.phoneNumber                                     AS Phone,
                    emp.email                                           AS Email,
                    CAST(CASE
                        WHEN emp.companyId IS NULL OR emp.companyId = 0 THEN 1
                        ELSE 0
                    END AS BIT)                                         AS IsMain
                FROM [dbo].[Employer] emp
                WHERE (emp.id = @RootId OR emp.companyId = @RootId)
                  AND emp.IsDeleted != 1
                ORDER BY IsMain DESC, emp.[name] ASC;";

            using var db = Db;
            return await db.QueryAsync<AffiliateEmployerItem>(sql, new { EmployerId = employerId });
        }

        // 6b. GET single affiliate record (for Edit modal pre-fill)
        public async Task<AffiliateEmployerItem?> GetAffiliateByIdAsync(int id)
        {
            const string sql = @"
                SELECT
                    id              AS EmployerId,
                    id              AS AffiliateId,
                    companyId       AS ParentEmployerId,
                    [name]          AS AffiliateName,
                    taxid           AS EIN,
                    [address]       AS [Address],
                    address2        AS Address2,
                    city            AS City,
                    [state]         AS [State],
                    zip             AS Zip,
                    phoneNumber     AS Phone,
                    email           AS Email,
                    CAST(CASE WHEN companyId IS NULL OR companyId = 0 THEN 1 ELSE 0 END AS BIT) AS IsMain
                FROM [dbo].[Employer]
                WHERE id = @Id AND IsDeleted != 1;";

            using var db = Db;
            return await db.QueryFirstOrDefaultAsync<AffiliateEmployerItem>(sql, new { Id = id });
        }

        // 6c. SAVE — INSERT new Employer row (companyId = root) or UPDATE existing
        public async Task SaveAffiliateAsync(AffiliateEmployerItem item)
        {
            using var db = Db;

            if (item.EmployerId == 0)
            {
                // INSERT: new Employer row linked to primary via companyId
                const string sql = @"
            INSERT INTO [dbo].[Employer]
                ([name], taxid, [address], address2, city, [state], zip,
                 phoneNumber, email, companyId, IsDeleted, filingYear, CreatedDate)
            VALUES
                (@AffiliateName, @EIN, @Address, @Address2, @City, @State, @Zip,
                 @Phone, @Email, @ParentEmployerId, 0, @filingYear, GETUTCDATE());";

                await db.ExecuteAsync(sql, item);
            }
            else
            {
                // UPDATE: edit existing Employer row (never touch companyId on update)
                const string sql = @"
            UPDATE [dbo].[Employer]
            SET [name]      = @AffiliateName,
                taxid       = @EIN,
                [address]   = @Address,
                address2    = @Address2,
                city        = @City,
                [state]     = @State,
                zip         = @Zip,
                phoneNumber = @Phone,
                email       = @Email
            WHERE id = @EmployerId AND IsDeleted != 1;";

                await db.ExecuteAsync(sql, item);
            }
            // -- SYNC LOGIC --
            // If an EIN was updated, we might want to log a note or flag the receipt
            //if (item.EmployerId > 0)
            //{
            //    const string checkSql = @"
            //IF EXISTS (SELECT 1 FROM Tracker_ReceiptId WHERE AffiliateEINId = @Id)
            //BEGIN
            //    INSERT INTO tbl_Medtracker_Note (employerid, _category, _xnote, _timestamp)
            //    VALUES (@Id, 'System', 'Affiliate details updated. Verified associated Receipt IDs.', GETUTCDATE())
            //END";
            //    await db.ExecuteAsync(checkSql, new { Id = item.EmployerId });
            //}
        }

        // 6d. DELETE — soft-delete; blocks deletion of the primary employer
        // Returns false if the record is the root (companyId IS NULL / 0)
        public async Task<bool> DeleteAffiliateAsync(int id)
        {
            using var db = Db;

            // Check: is this the primary employer?
            var companyId = await db.QueryFirstOrDefaultAsync<int?>(
                "SELECT companyId FROM [dbo].[Employer] WHERE id = @Id AND IsDeleted != 1",
                new { Id = id });

            // companyId IS NULL or 0 means it's the root — never delete it
            if (companyId == null || companyId == 0)
                return false;

            await db.ExecuteAsync(
                "UPDATE [dbo].[Employer] SET IsDeleted = 1 WHERE id = @Id",
                new { Id = id });

            return true;
        }

        // -- NOTES -----------------------------------------------------
        // Table: [dbo].[tbl_Medtracker_Note]
        //   noteid, employerid, _category, _xnote, _PlanYear,
        //   _timestamp, _inputUser

        // 7a. GET all notes for an employer
        public async Task<IEnumerable<EmployerNoteItem>> GetNotesAsync(int employerId)
        {
            using var db = Db;
            return await db.QueryAsync<EmployerNoteItem>(
                "sp_GetEmployerNotes",
                new { EmployerId = employerId },
                commandType: CommandType.StoredProcedure);
        }

        // 7b. SAVE (insert NoteId=0, update NoteId>0)
        public async Task<int> SaveNoteAsync(EmployerNoteItem item, int currentUserId)
        {
            using var db = Db;
            var result = await db.QueryFirstOrDefaultAsync<dynamic>(
                "sp_SaveEmployerNote",
                new
                {
                    NoteId = item.EmployerNoteId,
                    EmployerId = item.EmployerId,
                    Category = item.Category,
                    NoteText = item.NoteText,
                    NoteYear = item.NoteYear > 0 ? item.NoteYear : DateTime.Now.Year,
                    InputUser = currentUserId
                },
                commandType: CommandType.StoredProcedure);

            return (int)(result?.NoteId ?? item.EmployerNoteId);
        }

        // 7c. DELETE a note
        public async Task DeleteNoteAsync(int noteId, int employerId)
        {
            using var db = Db;
            await db.ExecuteAsync(
                "sp_DeleteEmployerNote",
                new { NoteId = noteId, EmployerId = employerId },
                commandType: CommandType.StoredProcedure);
        }

        // -- Save General/Billing Contact ------------------------
        public async Task<int> SaveContactAsync(TrackerContactDto dto)
        {
            using var db = Db;
            var p = new DynamicParameters();
            p.Add("@ContactId", dto.ContactId ?? 0);      // 0 = new
            p.Add("@EmployerId", dto.EmployerId);
            p.Add("@IsBilling", dto.IsBilling);
            p.Add("@Name", dto.Name);
            p.Add("@Phone", dto.Phone);
            p.Add("@Email", dto.Email);
            // Regular contact
            p.Add("@AcaUsername", dto.AcaUsername);
            p.Add("@AcaRoles", dto.AcaRoles);
            // Billing contact
            p.Add("@Address1", dto.Address1);
            p.Add("@Address2", dto.Address2);
            p.Add("@City", dto.City);
            p.Add("@State", dto.State);
            p.Add("@Zip", dto.Zip);

            return await db.ExecuteScalarAsync<int>(
                "sp_ManageTrackerContact",
                p,
                commandType: CommandType.StoredProcedure
            );
        }


        // -- Save Broker Contact ---------------------------------
        public async Task<int> SaveBrokerContactAsync(dynamic item)
        {
            using var db = Db;

            int id = (int)(item.Id ?? 0);

            if (id > 0)
            {
                // UPDATE
                var sql = @"
                    UPDATE Tracker_BrokerContact SET
                        name     = @Name,
                        phone    = @Phone,
                        email    = @Email,
                        _acaRoles    = @AcaRoles,
                        _acausername = @AcaUsername,
                        address1 = @Address,
                        address2 = @Address2,
                        city     = @City,
                        [State]  = @State,
                        zip      = @Zip
                    WHERE Broker_Contactid = @Id;";

                await db.ExecuteAsync(sql, (object)item);
                return id;
            }
            else
            {
                // INSERT
                var sql = @"
                    INSERT INTO Tracker_BrokerContact 
                        (brokerid, name, phone, email, _acaRoles, _acausername,
                         address1, address2, city, [State], zip)
                    VALUES 
                        (@BrokerId, @Name, @Phone, @Email, @AcaRoles, @AcaUsername,
                         @Address, @Address2, @City, @State, @Zip);
                    SELECT CAST(SCOPE_IDENTITY() AS INT);";

                return await db.ExecuteScalarAsync<int>(sql, (object)item);
            }
        }

        // -- Save Lookup (DataAnalyst, DataType, Industry) -------
        public async Task<int> SaveLookupAsync(string type, string name)
        {
            using var db = Db;

            string sql = type switch
            {
                "DataAnalyst" => @"
                    INSERT INTO StaffAccounts (Staff_FName, Staff_Status) 
                    VALUES (@Name, 1);
                    SELECT CAST(SCOPE_IDENTITY() AS INT);",

                "DataType" => @"
                    INSERT INTO tbl_Medtracker_DataType (_datatypeName, IsDeleted) 
                    VALUES (@Name, 0);
                    SELECT CAST(SCOPE_IDENTITY() AS INT);",

                "Industry" => @"
                    INSERT INTO tbl_Medtracker_Industry (_industryName, _IsDeleted) 
                    VALUES (@Name, 0);
                    SELECT CAST(SCOPE_IDENTITY() AS INT);",

                _ => throw new ArgumentException($"Unknown lookup type: {type}")
            };

            return await db.ExecuteScalarAsync<int>(sql, new { Name = name });
        }
        // -- Contact CRUD ----------------------------------------
        public async Task<dynamic> GetContactByIdAsync(int id)
        {
            using var db = Db;
            return await db.QueryFirstOrDefaultAsync(
                "SELECT tbl_Medtracker_Contactid AS Id, name AS Name, phone AS Phone, email AS Email,isBilling AS IsBilling,_acausername AS AcaUsername,_acaRoles AS AcaRoles,address1 AS Address1,address2 AS Address2,city AS City,tbl_Medtracker_State AS State,zip AS Zip FROM tbl_Medtracker_Contact WHERE tbl_Medtracker_Contactid = @Id",
                new { Id = id });
        }

        public async Task DeleteContactAsync(int id)
        {
            using var db = Db;
            await db.ExecuteAsync(
                "DELETE FROM tbl_Medtracker_Contact WHERE tbl_Medtracker_Contactid = @Id",
                new { Id = id });
        }

        // -- Broker Contact CRUD ---------------------------------
        public async Task<dynamic> GetBrokerContactByIdAsync(int id)
        {
            using var db = Db;
            return await db.QueryFirstOrDefaultAsync(
                @"SELECT 
                    bc.Broker_Contactid AS id,
                    bc.brokerid         AS brokerId,
                    bc.name             AS name,
                    bc.phone            AS phone,
                    bc.email            AS email,
                    bc._acaRoles        AS acaRoles,
                    bc._acausername     AS acaUsername,
                    bc.address1         AS address,
                    bc.address2         AS address2,
                    bc.city             AS city,
                    bc.[State]          AS state,
                    bc.zip              AS zip
                  FROM Tracker_BrokerContact bc
                  WHERE bc.Broker_Contactid = @Id",
                new { Id = id });
        }


        public async Task DeleteBrokerContactAsync(int id)
        {
            using var db = Db;
            await db.ExecuteAsync(
                "DELETE FROM Tracker_BrokerContact WHERE Broker_Contactid = @Id",
                new { Id = id });
        }

        // -- Firm Info -------------------------------------------
        public async Task<dynamic> GetFirmByIdAsync(int id)
        {
            using var db = Db;
            // Removed Phone, Email, ContactName as they likely don't exist in Tracker_Firm table
            return await db.QueryFirstOrDefaultAsync(
                @"SELECT FirmId, FirmName, Address, City, State, Zip, IsDeleted
                  FROM Tracker_Firm WHERE FirmId = @Id",
                new { Id = id });
        }

        // -- Broker Info -----------------------------------------
        public async Task<dynamic> GetBrokerByIdAsync(int id)
        {
            using var db = Db;
            return await db.QueryFirstOrDefaultAsync(
                @"SELECT 
                    b.BrokerId      AS brokerId,
                    b.BrokerName    AS brokerName,
                    b.Phone         AS phone,
                    b.Email         AS email,
                    b.[Address]     AS address,
                    b.City          AS city,
                    s.StateName     AS state,
                    b.Zip           AS zip,
                    b.ConnectUser   AS connectUser,
                    b.IsActive      AS isActive,
                    f.FirmName      AS firmName
                  FROM Tracker_Broker b
                  LEFT JOIN Tracker_Firm f ON f.FirmId = b.FirmId
                  LEFT JOIN tbl_Medtracker_State s ON s.StateId = b.StateId
                  WHERE b.BrokerId = @Id",
                new { Id = id });
        }

        // -- Receipt IDs ---------------------------------------------------

        public async Task<IEnumerable<ReceiptIdItem>> GetReceiptIdsAsync(int employerServiceId, int employerId = 0)
        {
            using var db = Db;
            var receipts = await db.QueryAsync<ReceiptIdItem>(
                "sp_ManageReceiptId",
                new
                {
                    Action = "GET",
                    EmployerServiceId = employerServiceId
                },
                commandType: CommandType.StoredProcedure);

            return receipts.Where(r => r.EmployerServiceId == employerServiceId
                                 && (employerId == 0 || r.AffiliateEINId == employerId));
        }

        public async Task<int> SaveReceiptIdAsync(ReceiptIdItem item, int userId)
        {
            using var db = Db;
            var result = await db.QueryFirstOrDefaultAsync<dynamic>(
                "sp_ManageReceiptId",
                new
                {
                    Action = "SAVE",
                    item.ReceiptEntryId,
                    item.EmployerId,
                    item.AffiliateEINId,
                    item.EmployerServiceId,
                    item.ServiceId,
                    item.ReceiptIdValue,
                    item.ReceivedDate,
                    item.PlanYear,
                    item.Notes,
                    UserId = userId
                },
                commandType: CommandType.StoredProcedure);

            return (int)(result?.ReceiptEntryId ?? 0);
        }

        public async Task DeleteReceiptIdAsync(int receiptEntryId)
        {
            using var db = Db;
            await db.ExecuteAsync(
                "sp_ManageReceiptId",
                new { Action = "DELETE", ReceiptEntryId = receiptEntryId },
                commandType: CommandType.StoredProcedure);
        }

        public async Task<IEnumerable<ProcessHistoryDto>> GetProcessHistoryAsync(int employerServiceId, int categoryId)
        {
            using (var db = Db)
            {
                string sql = @"
     SELECT 
         ph.[CreatedDate], 
         ISNULL(ph.[UpdatedBy], 'System') AS UpdatedBy, 
         p.[ProcessName], 
         ph.[NextFollowUpDate]
     FROM [dbo].[Tracker_ProcessHistory] ph
     INNER JOIN [dbo].[Tracker_Process] p ON ph.[ProcessId] = p.[ProcessId]
     INNER JOIN [dbo].[Tracker_Category] c ON p.[CategoryId] = c.[CategoryId]
     WHERE ph.[EmployerServiceId] = @EmployerServiceId
       AND c.[CategoryId] = @CategoryId 
     ORDER BY ph.[CreatedDate] DESC";

                return await db.QueryAsync<ProcessHistoryDto>(sql, new
                {
                    EmployerServiceId = employerServiceId,
                    CategoryId = categoryId
                });
            }
        }

        public async Task<IEnumerable<MasterExportModel>> GetMasterExportDataAsync(string planYear, int? employerId = null)
        {
            using (var db = Db)
            {
                string sql = @"
            SELECT 
                E.[name] AS EmployerName,
                E.taxid AS EIN,
                E.[address] AS [Address],
                E.city AS City,
                E.[state] AS [State],
                E.zip AS Zip,
                E.contactName AS ContactName,
                E.phoneNumber AS Phone,
                E.email AS Email,
                F.FirmName AS FirmName,
                EE._fundingtype AS FundingType,
                EE._FormType AS FormType,
                EE._planyear AS PlanYear,
                EE._LevelofComplexity AS LevelOfComplexity,
                AM.Staff_FName + ' ' + AM.Staff_LName AS AccountManager,
                DA.Staff_FName + ' ' + DA.Staff_LName AS DataAnalyst,
                SR.SalesRepName AS SalesRep,
                SL.ServiceName AS ServiceName,
                ES.Status AS ServiceStatus,
                ES.PlanYear AS ServicePlanYear,
                ES.Notes AS ServiceNotes,
                ES.ReceiptID AS ReceiptID
            FROM [dbo].[Employer] E
            LEFT JOIN [dbo].[Tracker_EmployerExtension] EE ON E.id = EE.EmployerId
            LEFT JOIN [dbo].[Tracker_Firm] F ON EE.firmid = F.FirmId
            LEFT JOIN [dbo].[Tracker_EmployerService] ES ON E.id = ES.EmployerId
            LEFT JOIN [dbo].[Tracker_ServiceCatalog] SL ON ES.ServiceId = SL.ServiceId
            LEFT JOIN [dbo].[StaffAccounts] AM ON EE.AcctManagerid = AM.Staff_ID
            LEFT JOIN [dbo].[StaffAccounts] DA ON EE._dataAnalyst = DA.Staff_ID
            LEFT JOIN [dbo].[tracker_salesrep] SR ON EE.srepid = SR.SalesRepId
            WHERE E.IsDeleted != 1 
              AND (ES.PlanYear = @PlanYear OR @PlanYear IS NULL OR @PlanYear = '')
              AND (E.id = @EmployerId OR @EmployerId IS NULL)
            ORDER BY E.[name], ES.PlanYear DESC";

                return await db.QueryAsync<MasterExportModel>(sql, new { PlanYear = planYear, EmployerId = employerId });
            }
        }

    }
}



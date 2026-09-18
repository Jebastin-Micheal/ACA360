using ACA360.Core.Models;
using ACA360.Data.Helpers;
using ACA360.Logging.Interfaces;
using ACA360.Repositories.Interfaces;
using Dapper;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.Data.SqlClient;
using System.Data;
namespace ACA360.Repositories.Implementations
{
    public class AssignmentRepository : IAssignmentService
    {
        private readonly string _connectionString;

        public AssignmentRepository(string connectionString)
        {
            _connectionString = connectionString;
        }
        private IDbConnection Connection => new SqlConnection(_connectionString);


        // Data Analyst Assignment methods

        // Get data analyst by ID
        public async Task<DataAnalyst?> GetDataAnalystByIdAsync(int id)
        {
            using (var db = Connection)
            {
                return await db.QuerySingleOrDefaultAsync<DataAnalyst>(
                    "sp_Get_Staff_ById", // Assuming same stored procedure exists for staff
                    new { Id = id },
                    commandType: CommandType.StoredProcedure);
            }
        }

        // Get employers by data analyst ID
        public async Task<List<Employer>> GetEmployersByDataAnalystIdAsync(int analystId, int roleId)
        {
            using (var db = Connection)
            {
                var result = await db.QueryAsync<Employer>(
                    "sp_GetEmployersByDataAnalystId",
                    new { Analyst_UserID = analystId, roleId = roleId },
                    commandType: CommandType.StoredProcedure);

                return result.ToList();
            }
        }


        // Get account manager by ID
        public async Task<AccountManager?> GetAccountManagerByIdAsync(int id)
        {
            using (var db = Connection)
            {
                return await db.QuerySingleOrDefaultAsync<AccountManager>(
                    "sp_Get_Staff_ById",
                    new { Id = id },
                    commandType: CommandType.StoredProcedure);
            }
        }

        // Get employers by account manager ID (active assignments only)
        public async Task<List<Employer>> GetEmployersByAccountManagerIdAsync(int amId, int roleId)
        {
            using (var db = Connection)
            {
                var result = await db.QueryAsync<Employer>(
                    "sp_GetEmployersByAccountManagerId",
                    new { AM_UserID = amId, roleId = roleId },
                    commandType: CommandType.StoredProcedure);

                return result.ToList();
            }
        }



        // Get Broker by ID
        public async Task<AccountManager?> GetBrokerByIdAsync(int id)
        {
            using (var db = Connection)
            {
                return await db.QuerySingleOrDefaultAsync<AccountManager>(
                    "sp_Get_Broker_ById",
                    new { Id = id },
                    commandType: CommandType.StoredProcedure);
            }
        }

        // Get employers by Broker ID (active assignments only)
        public async Task<List<Employer>> GetEmployersByBrokerIdAsync(int amId, int roleId)
        {
            using (var db = Connection)
            {
                var result = await db.QueryAsync<Employer>(
                    "sp_GetEmployersByBrokerId",
                    new { Broker_ID = amId, RoleId = roleId },
                    commandType: CommandType.StoredProcedure);

                return result.ToList();
            }
        }




        // In AssignmentRepository.cs - Updated methods

        // Assign employers to account manager - FIXED VERSION
        public async Task AssignEmployersToAccountManagerAsync(int amId, List<int> employerIds, int assignedByAdminId)
        {
            using (var db = Connection)
            {
                // Now create new assignments
                var assignmentsDt = new DataTable();
                assignmentsDt.Columns.Add("EmployerID", typeof(int));
                assignmentsDt.Columns.Add("AM_UserID", typeof(int));
                assignmentsDt.Columns.Add("AssignedBy_AdminID", typeof(int));

                foreach (var employerId in employerIds)
                {
                    assignmentsDt.Rows.Add(employerId, amId, assignedByAdminId);
                }

                var createParams = new
                {
                    Assignments = assignmentsDt.AsTableValuedParameter("PortfolioAssignmentType"),
                    AM_UserID = amId
                };

                await db.ExecuteAsync(
                    "sp_SavePortfolioAssignment",
                    createParams,
                    commandType: CommandType.StoredProcedure);
            }
        }

        // Assign employers to data analyst - FIXED VERSION
        public async Task AssignEmployersToDataAnalystAsync(int analystId, List<int> employerIds, int assignedByAdminId)
        {
            using (var db = Connection)
            {
                // Now create new assignments
                var assignmentsDt = new DataTable();
                assignmentsDt.Columns.Add("EmployerID", typeof(int));
                assignmentsDt.Columns.Add("Analyst_UserID", typeof(int));
                assignmentsDt.Columns.Add("AssignedBy_AdminID", typeof(int));

                foreach (var employerId in employerIds)
                {
                    assignmentsDt.Rows.Add(employerId, analystId, assignedByAdminId);
                }

                var createParams = new
                {
                    Assignments = assignmentsDt.AsTableValuedParameter("DataAnalystAssignmentType"),
                    Analyst_ID = analystId
                };

                await db.ExecuteAsync(
                    "sp_SaveOperationalAssignment",
                    createParams,
                    commandType: CommandType.StoredProcedure);
            }
        }
        public async Task AssignEmployersToBrokerAsync(int analystId, List<int> employerIds, int assignedByAdminId)
        {
            using (var db = Connection)
            {
                // Now create new assignments
                var assignmentsDt = new DataTable();
                assignmentsDt.Columns.Add("EmployerID", typeof(int));
                assignmentsDt.Columns.Add("Broker_UserID", typeof(int));
                assignmentsDt.Columns.Add("AssignedBy_AdminID", typeof(int));

                foreach (var employerId in employerIds)
                {
                    assignmentsDt.Rows.Add(employerId, analystId, assignedByAdminId);
                }

                var createParams = new
                {
                    Assignments = assignmentsDt.AsTableValuedParameter("BrokerAssignmentType"),
                    Broker_ID = analystId
                };

                await db.ExecuteAsync(
                    "sp_SaveBrokerAssignment",
                    createParams,
                    commandType: CommandType.StoredProcedure);
            }
        }

        // AM + DA assignment history for one employer (history popup)
        public async Task<EmployerAssignmentHistoryModel?> GetEmployerAssignmentHistoryAsync(int employerId)
        {
            using (var db = Connection)
            {
                using (var multi = await db.QueryMultipleAsync(
                    "sp_GetEmployerAssignmentHistory",
                    new { EmployerID = employerId },
                    commandType: CommandType.StoredProcedure))
                {
                    var employer = await multi.ReadFirstOrDefaultAsync<dynamic>();
                    if (employer == null) return null;

                    var model = new EmployerAssignmentHistoryModel
                    {
                        EmployerId = (int)employer.EmployerId,
                        EmployerName = (string)(employer.EmployerName ?? string.Empty)
                    };
                    model.AccountManagerHistory = (await multi.ReadAsync<EmployerAssignmentHistoryItem>()).ToList();
                    model.DataAnalystHistory = (await multi.ReadAsync<EmployerAssignmentHistoryItem>()).ToList();
                    return model;
                }
            }
        }

        // Employers already actively assigned to a DIFFERENT AM / DA than the given staff member
        public async Task<List<AssignmentConflictItem>> GetAssignmentConflictsAsync(List<int> employerIds, int staffId, string side)
        {
            if (employerIds == null || employerIds.Count == 0)
                return new List<AssignmentConflictItem>();

            using (var db = Connection)
            {
                var result = await db.QueryAsync<AssignmentConflictItem>(
                    "sp_Check_Assignment_Conflicts",
                    new { EmployerIDs = string.Join(",", employerIds), StaffID = staffId, Side = side },
                    commandType: CommandType.StoredProcedure);
                return result.ToList();
            }
        }

    }
}


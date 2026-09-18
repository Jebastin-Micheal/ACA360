using ACA360.BusinessLogic.Interfaces;
using ACA360.Core.Models;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging; // Added for ILogger
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACA360.BusinessLogic.Services
{
    public class CalendarService : ICalendarService
    {
        private readonly string _connectionString;
        private readonly ILogger<CalendarService> _logger; // Logger injection

        public CalendarService(IConfiguration config, ILogger<CalendarService> logger)
        {
            _connectionString = config.GetConnectionString("DefaultConnection");
            _logger = logger;
        }

        public List<CalendarEventModel> GetEventsByUser(long userId)
        {
            var list = new List<CalendarEventModel>();
            try
            {
                using var conn = new SqlConnection(_connectionString);
                using var cmd = new SqlCommand("usp_CalendarEvents_GetByUser", conn) { CommandType = CommandType.StoredProcedure };
                cmd.Parameters.AddWithValue("@UserId", userId);

                conn.Open();
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    list.Add(new CalendarEventModel
                    {
                        Id = (int)reader["Id"],
                        Title = reader["Title"].ToString(),
                        Description = reader["Description"]?.ToString(),
                        StartDate = (DateTime)reader["StartDate"],
                        EndDate = reader["EndDate"] as DateTime?,
                        IsAllDay = (bool)reader["IsAllDay"],
                        ColorCode = reader["ColorCode"]?.ToString(),
                        EventUrl = reader["EventUrl"]?.ToString(),
                        Location = reader["Location"]?.ToString(),
                        Guests = reader["Guests"]?.ToString()
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred in GetEventsByUser for UserId: {UserId}", userId);
                throw; // Rethrowing the exception so the controller or upper layer knows something went wrong
            }
            return list;
        }

        public int AddEvent(long userId, CalendarEventModel model)
        {
            try
            {
                using var conn = new SqlConnection(_connectionString);
                using var cmd = new SqlCommand("usp_CalendarEvents_Add", conn) { CommandType = CommandType.StoredProcedure };

                cmd.Parameters.AddWithValue("@UserId", userId);
                cmd.Parameters.AddWithValue("@Title", model.Title);
                cmd.Parameters.AddWithValue("@Description", (object)model.Description ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@StartDate", model.StartDate);
                cmd.Parameters.AddWithValue("@EndDate", (object)model.EndDate ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@IsAllDay", model.IsAllDay);
                cmd.Parameters.AddWithValue("@ColorCode", (object)model.ColorCode ?? "#696cff");
                cmd.Parameters.AddWithValue("@EventUrl", (object)model.EventUrl ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@Location", (object)model.Location ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@Guests", (object)model.Guests ?? DBNull.Value);

                conn.Open();
                return Convert.ToInt32(cmd.ExecuteScalar());
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred while adding event for UserId: {UserId}, Title: {Title}", userId, model?.Title);
                throw;
            }
        }

        public void UpdateEvent(long userId, CalendarEventModel model)
        {
            try
            {
                using var conn = new SqlConnection(_connectionString);
                using var cmd = new SqlCommand("usp_CalendarEvents_Update", conn) { CommandType = CommandType.StoredProcedure };

                cmd.Parameters.AddWithValue("@Id", model.Id);
                cmd.Parameters.AddWithValue("@UserId", userId);
                cmd.Parameters.AddWithValue("@Title", model.Title);
                cmd.Parameters.AddWithValue("@Description", (object)model.Description ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@StartDate", model.StartDate);
                cmd.Parameters.AddWithValue("@EndDate", (object)model.EndDate ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@IsAllDay", model.IsAllDay);
                cmd.Parameters.AddWithValue("@ColorCode", (object)model.ColorCode ?? "#696cff");
                cmd.Parameters.AddWithValue("@EventUrl", (object)model.EventUrl ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@Location", (object)model.Location ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@Guests", (object)model.Guests ?? DBNull.Value);

                conn.Open();
                cmd.ExecuteNonQuery();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred while updating event ID: {EventId} for UserId: {UserId}", model?.Id, userId);
                throw;
            }
        }

        public void DeleteEvent(long userId, int id)
        {
            try
            {
                using var conn = new SqlConnection(_connectionString);
                using var cmd = new SqlCommand("usp_CalendarEvents_Delete", conn) { CommandType = CommandType.StoredProcedure };

                cmd.Parameters.AddWithValue("@Id", id);
                cmd.Parameters.AddWithValue("@UserId", userId);

                conn.Open();
                cmd.ExecuteNonQuery();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred while deleting event ID: {EventId} for UserId: {UserId}", id, userId);
                throw;
            }
        }
    }
}
using ACA360.Core.Models;
using ACA360.Logging.Interfaces;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using ACA360.BusinessLogic.Interfaces;

namespace ACA360.BusinessLogic.Services
{
    public class TemplateService : ITemplateService
    {
        private readonly string _connectionString;
        private readonly ILoggerService _logger;

        public TemplateService(IConfiguration configuration, ILoggerService logger)
        {
            _connectionString = configuration.GetConnectionString("DefaultConnection") 
                ?? throw new InvalidOperationException("The 'DefaultConnection' connection string is missing from the configuration.");
            _logger = logger;
        }

        /// <summary>
        /// Retrieves a list of all active import templates from the database.
        /// </summary>
        /// <returns>A list of active ImportTemplate objects.</returns>
        public async Task<List<ImportTemplate>> GetActiveTemplatesAsync()
        {
            try
            {
                // Establish a new SQL connection using the configured connection string
                using var connection = new SqlConnection(_connectionString);

                // Execute the stored procedure to fetch active templates using Dapper
                var result = await connection.QueryAsync<ImportTemplate>(
                    "sp_GetActiveTemplates",
                    commandType: CommandType.StoredProcedure);

                return result.ToList();
            }
            catch (Exception ex)
            {
                // Log the database query failure and rethrow to be handled by the controller
                _logger.LogError(ex,nameof(GetActiveTemplatesAsync),nameof(TemplateService), "An error occurred while retrieving active templates.");
                throw;
            }
        }

        /// <summary>
        /// Retrieves the column mappings associated with a specific template ID.
        /// </summary>
        /// <param name="templateId">The unique identifier of the template.</param>
        /// <returns>A list of TemplateColumnMap objects.</returns>
        public async Task<List<TemplateColumnMap>> GetColumnMapsByTemplateIdAsync(int templateId)
        {
            try
            {
                using var connection = new SqlConnection(_connectionString);

                // Fetch the mappings using the specified template ID parameter
                var result = await connection.QueryAsync<TemplateColumnMap>(
                    "sp_GetColumnMapsByTemplateId",
                    new { TemplateId = templateId },
                    commandType: CommandType.StoredProcedure);

                return result.ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, nameof(GetColumnMapsByTemplateIdAsync), nameof(TemplateService), $"An error occurred while retrieving column maps for Template ID {templateId}.");
                throw;
            }
        }

        /// <summary>
        /// Retrieves all import templates. Currently reuses the active templates logic.
        /// </summary>
        /// <returns>A list of ImportTemplate objects.</returns>
        public async Task<List<ImportTemplate>> GetAllTemplatesAsync()
        {
            try
            {
                using var connection = new SqlConnection(_connectionString);

                // Execute the stored procedure to retrieve templates
                var result = await connection.QueryAsync<ImportTemplate>(
                    "sp_GetActiveTemplates",
                    commandType: CommandType.StoredProcedure);

                return result.AsList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, nameof(GetAllTemplatesAsync), nameof(TemplateService), "An error occurred while retrieving all templates.");
                throw;
            }
        }

        /// <summary>
        /// Retrieves a specific template by its unique identifier.
        /// </summary>
        /// <param name="templateId">The template identifier.</param>
        /// <returns>The requested ImportTemplate, or null if not found.</returns>
        public async Task<ImportTemplate?> GetTemplateByIdAsync(int templateId)
        {
            try
            {
                using var connection = new SqlConnection(_connectionString);

                // Fetch the single template matching the provided ID
                return await connection.QueryFirstOrDefaultAsync<ImportTemplate>(
                    "sp_GetTemplateById",
                    new { Id = templateId },
                    commandType: CommandType.StoredProcedure);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, nameof(GetTemplateByIdAsync), nameof(TemplateService), $"An error occurred while retrieving Template ID {templateId}.");
                throw;
            }
        }

        /// <summary>
        /// Updates the source column name and alternate names for a specific mapping.
        /// </summary>
        /// <param name="mapId">The unique identifier of the mapping record.</param>
        /// <param name="newColumnName">The updated source column name.</param>
        /// <param name="alternateNames">A comma-separated string of alternate acceptable names.</param>
        public async Task UpdateMappingAsync(int mapId, string newColumnName, string alternateNames)
        {
            try
            {
                using var connection = new SqlConnection(_connectionString);

                // Execute the update query passing the modified mapping values
                await connection.ExecuteAsync(
                    "sp_UpdateMapping",
                    new { MapId = mapId, SourceColumnName = newColumnName, AlternateNames = alternateNames },
                    commandType: CommandType.StoredProcedure);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, nameof(UpdateMappingAsync), nameof(TemplateService), $"An error occurred while updating mapping for Map ID {mapId}.");
                throw;
            }
        }

        /// <summary>
        /// Creates a new import template and returns its generated unique identifier.
        /// </summary>
        /// <param name="name">The name of the new template.</param>
        /// <param name="description">An optional description for the template.</param>
        /// <returns>The ID of the newly created template.</returns>
        public async Task<int> CreateTemplateAsync(string name, string description)
        {
            try
            {
                using var connection = new SqlConnection(_connectionString);

                // Insert the new template record and retrieve the newly generated scalar ID
                return await connection.QuerySingleAsync<int>(
                    "sp_CreateTemplate",
                    new { TemplateName = name, Description = description },
                    commandType: CommandType.StoredProcedure);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, nameof(CreateTemplateAsync), nameof(TemplateService), $"An error occurred while creating template '{name}'.");
                throw;
            }
        }

        /// <summary>
        /// Clones all column mappings from a source template into a target template.
        /// </summary>
        /// <param name="sourceTemplateId">The ID of the template to copy mappings from.</param>
        /// <param name="targetTemplateId">The ID of the template to copy mappings to.</param>
        public async Task CloneMappingsAsync(int sourceTemplateId, int targetTemplateId)
        {
            try
            {
                using var connection = new SqlConnection(_connectionString);

                // Execute the cloning procedure to replicate mappings across templates
                await connection.ExecuteAsync(
                    "sp_CloneMappings",
                    new { SourceTemplateId = sourceTemplateId, TargetTemplateId = targetTemplateId },
                    commandType: CommandType.StoredProcedure);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, nameof(CloneMappingsAsync), nameof(TemplateService), $"An error occurred while cloning mappings from Template ID {sourceTemplateId} to {targetTemplateId}.");
                throw;
            }
        }
    }
}
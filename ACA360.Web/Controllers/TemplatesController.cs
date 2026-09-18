using ACA360.Core.Models;
using ACA360.Core.Models.ViewModels;
using ACA360.Logging.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Linq;
using System.Threading.Tasks;
using ACA360.BusinessLogic.Interfaces;
using ACA360.Core.Constants;
using Microsoft.AspNetCore.Authorization;
namespace ACA360.Web.Controllers
{
    [Authorize(Roles = UserRoles.InternalTeam)]
    public class TemplatesController : Controller
    {
        private readonly ITemplateService _templateService;
        private readonly ILoggerService _logger;

        public TemplatesController(ITemplateService templateService, ILoggerService logger)
        {
            _templateService = templateService;
            _logger = logger;
        }

        /// <summary>
        /// Retrieves and lists all available templates.
        /// </summary>
        /// <returns>A view displaying the collection of templates.</returns>
        [HttpGet]
        public async Task<IActionResult> Index()
        {
            try
            {
                // Fetch all system templates asynchronously
                var templates = await _templateService.GetAllTemplatesAsync();

                return View(templates);
            }
            catch (Exception ex)
            {
                // Log the retrieval failure securely
                _logger.LogError(ex, nameof(Index), nameof(TemplatesController), "An error occurred while fetching the templates list.");

                // Return a generic 500 Internal Server Error status to the client
                return StatusCode(StatusCodes.Status500InternalServerError, "An unexpected error occurred while loading the templates.");
            }
        }

        /// <summary>
        /// Creates a new template, clones the default column mappings from the master template, 
        /// and redirects the user to the configuration editor.
        /// </summary>
        /// <param name="templateName">The specified name for the new template.</param>
        /// <param name="description">An optional description detailing the template's purpose.</param>
        /// <returns>A redirection to the configuration view for the newly generated template.</returns>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(string templateName, string description)
        {
            try
            {
                // Validation: Ensure template name is provided
                if (string.IsNullOrWhiteSpace(templateName))
                {
                    return BadRequest("Template name is required.");
                }

                // 1. Create the base Template entity and retrieve its new database ID
                int newId = await _templateService.CreateTemplateAsync(templateName, description);

                // 2. Clone standard Mappings from the Master Template (ID 2) into the new template
                await _templateService.CloneMappingsAsync(sourceTemplateId: 2, targetTemplateId: newId);

                // 3. Redirect to the Editor to allow further customization of the cloned mappings
                return RedirectToAction("Configuration", new { id = newId });
            }
            catch (Exception ex)
            {
                // Log any errors that happen during the creation and cloning process
                _logger.LogError(ex, nameof(Create), nameof(TemplatesController), $"An error occurred while creating the template '{templateName}'.");

                // Return a 500 error if the transaction or cloning operations fail
                return StatusCode(StatusCodes.Status500InternalServerError, "An unexpected error occurred while creating the template.");
            }
        }

        /// <summary>
        /// Loads the configuration interface for a specific template.
        /// Flattens column mapping records and groups them by TargetSheetName to build a tabbed UI.
        /// </summary>
        /// <param name="id">The unique identifier of the template being configured.</param>
        /// <returns>A view bound to the grouped TemplateConfigurationViewModel.</returns>
        [HttpGet]
        public async Task<IActionResult> Configuration(int id)
        {
            try
            {
                // Validate incoming identifier
                if (id <= 0)
                {
                    return BadRequest("A valid template ID is required.");
                }

                // Fetch template metadata
                var template = await _templateService.GetTemplateByIdAsync(id);
                if (template == null)
                {
                    return NotFound("The specified template could not be found.");
                }

                // Fetch the flat collection of mappings associated with this template
                var mappings = await _templateService.GetColumnMapsByTemplateIdAsync(id);

                // Construct the view model and organize the flat mapping list into Tabs based on 'TargetSheetName'
                var viewModel = new TemplateConfigurationViewModel
                {
                    TemplateId = template.TemplateId,
                    TemplateName = template.TemplateName,
                    Description = template.Description,
                    GroupedMappings = mappings
                        .GroupBy(m => m.TargetSheetName)
                        .ToDictionary(g => g.Key, g => g.ToList())
                };

                return View(viewModel);
            }
            catch (Exception ex)
            {
                // Log failures during configuration data load
                _logger.LogError(ex, nameof(Configuration), nameof(TemplatesController), $"An error occurred while loading the configuration for Template ID {id}.");

                // Gracefully fail with a 500 server error
                return StatusCode(StatusCodes.Status500InternalServerError, "An unexpected error occurred while loading the template configuration.");
            }
        }

        /// <summary>
        /// Data Transfer Object mapping incoming JSON updates from the frontend client.
        /// </summary>
        public class MappingUpdateDto
        {
            public int MapId { get; set; }
            public string? SourceColumnName { get; set; }
            public string? AlternateNames { get; set; }
        }

        /// <summary>
        /// Updates an individual column mapping configuration, specifically its target column name and expected alternate names.
        /// </summary>
        /// <param name="updateDto">The payload containing the mapping edits.</param>
        /// <returns>A JSON success confirmation or an error payload.</returns>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateMapping([FromBody] MappingUpdateDto updateDto)
        {
            try
            {
                // Payload structure validation
                if (updateDto == null)
                {
                    return BadRequest("Mapping update payload is missing.");
                }

                // Business logic validation: Ensure a source column name is declared
                if (string.IsNullOrEmpty(updateDto.SourceColumnName))
                {
                    return BadRequest("Column name cannot be empty.");
                }

                // Execute the update operation in the service, passing all necessary arguments
                await _templateService.UpdateMappingAsync(
                    updateDto.MapId,
                    updateDto.SourceColumnName,
                    updateDto.AlternateNames
                );

                return Ok(new { success = true });
            }
            catch (Exception ex)
            {
                // Log errors caused during the database mapping update transaction
                _logger.LogError(ex, nameof(UpdateMapping), nameof(TemplatesController), $"An error occurred while updating the mapping for Map ID {updateDto?.MapId}.");

                // Return a structured 500 error for AJAX consumption
                return StatusCode(StatusCodes.Status500InternalServerError, new { success = false, message = "An unexpected error occurred while updating the mapping." });
            }
        }
    }
}
using ACA360.Core.Models;
using System.Collections.Generic;
using System.IO;

namespace ACA360.BusinessLogic.Interfaces
{
    public interface IExcelStructureValidatorService
    {
        // Update the signature to accept the rules list
        ExcelValidationResult ValidateStructure(Stream excelStream, List<TemplateColumnMap> rules);
    }
}
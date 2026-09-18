using ACA360.Core.Models;

namespace ACA360.Web.Models
{
    public class FileDetailsViewModel
    {
        public UploadedFileLog FileLog { get; set; } = new UploadedFileLog();

        // THE FIX: Change the property type from FileUploadError to our new StagingRowError model.
        public IEnumerable<StagingRowError> Errors { get; set; } = new List<StagingRowError>();
    }

}

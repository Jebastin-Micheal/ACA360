using System.IO;
using System.Threading.Tasks;

namespace ACA360.BusinessLogic.Interfaces
{
    public class BlobFileInfo
    {
        public string? FileName { get; set; }
        public string? FilePath { get; set; }
        public DateTime CreatedOn { get; set; }
        public long FileSize { get; set; }
    }
    public interface IAzureBlobService
    {
        // Uploads a file from a stream
        Task<string> UploadFileAsync(Stream fileStream, string folderName, string fileName);

        // Downloads a file and returns it as a byte array
        Task<byte[]?> DownloadFileAsync(string folderName, string fileName);

        // Deletes a file (handy to have!)
        Task<bool> DeleteFileAsync(string folderName, string fileName);

        Task<Dictionary<int, DateTime>> GetLatestFileDatesAsync(string folderName, int year);
        Task<List<BlobFileInfo>> ListFilesAsync(string folderName);

        Task<Stream?> DownloadStreamAsync(string folderName, string fileName);
        Task<bool> MoveFileAsync(string sourceFolder, string destFolder, string fileName);
        Task<bool> FileExistsAsync(string folderName, string fileName);



        Task<string> UploadDirectAsync(Stream stream, string folder, string fileName);
        Task<string> GenerateSasUploadUrlAsync(string blobName, int expiryMinutes);
        Task<bool> BlobExistsAsync(string blobName);
        Task DeleteIfExistsAsync(string blobName);
        string GetBlobUri(string blobName);
    }
}
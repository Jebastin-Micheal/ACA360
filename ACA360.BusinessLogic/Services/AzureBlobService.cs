using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Sas;
using Azure.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ACA360.BusinessLogic.Interfaces;

namespace ACA360.BusinessLogic.Services
{
    public class AzureBlobService : IAzureBlobService
    {
        private readonly BlobContainerClient _container;
        private readonly string _uploadsFolder;
        private readonly ILogger<AzureBlobService> _logger;

        public AzureBlobService(IConfiguration config, ILogger<AzureBlobService> logger)
        {
            _logger = logger;
            _uploadsFolder = config["AzureBlob:UploadsFolder"] ?? "Uploadsfiles";

            var connectionString = config["AzureBlob:ConnectionString"];
            var containerName = config["AzureBlob:ContainerName"];

            if (string.IsNullOrWhiteSpace(connectionString))
                throw new InvalidOperationException(
                    "AzureBlob:ConnectionString is missing. " +
                    "Add it to appsettings.json or Azure App Service > Configuration.");

            if (string.IsNullOrWhiteSpace(containerName))
                throw new InvalidOperationException(
                    "AzureBlob:ContainerName is missing. " +
                    "Add it to appsettings.json or Azure App Service > Configuration.");

            // Single shared container client — used by ALL methods below
            var serviceClient = new BlobServiceClient(connectionString);
            _container = serviceClient.GetBlobContainerClient(containerName);
            _container.CreateIfNotExists(PublicAccessType.None);
        }

        // ── Private helper — builds blob path consistently ────────────────────
        private static string BlobPath(string folder, string fileName)
            => string.IsNullOrEmpty(folder) ? fileName : $"{folder}/{fileName}";

        // ═══════════════════════════════════════════════════════════════════════
        // TIER 1 — Direct upload (≤ 10 MB), single PUT, with retry
        // ═══════════════════════════════════════════════════════════════════════
        public async Task<string> UploadDirectAsync(Stream stream, string folder, string fileName)
        {
            var blobClient = _container.GetBlobClient(BlobPath(folder, fileName));

            var options = new BlobUploadOptions
            {
                TransferOptions = new StorageTransferOptions
                {
                    InitialTransferSize = 12 * 1024 * 1024,  // Forces single PUT for ≤ 10 MB
                    MaximumConcurrency = 1
                }
            };

            int attempt = 0;
            while (true)
            {
                try
                {
                    stream.Position = 0;
                    await blobClient.UploadAsync(stream, options);
                    return blobClient.Uri.ToString();
                }
                catch (RequestFailedException ex)
                    when ((ex.Status == 503 || ex.Status == 500 || ex.Status == 429) && attempt < 3)
                {
                    attempt++;
                    _logger.LogWarning("Blob upload retry {Attempt} — HTTP {Status}", attempt, ex.Status);
                    await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt)));
                }
            }
        }

        // ═══════════════════════════════════════════════════════════════════════
        // TIER 2 — SAS URL generation (10–120 MB, browser uploads direct)
        // ═══════════════════════════════════════════════════════════════════════
        public async Task<string> GenerateSasUploadUrlAsync(string blobName, int expiryMinutes)
        {
            var blobClient = _container.GetBlobClient(blobName);

            var sasBuilder = new BlobSasBuilder
            {
                BlobContainerName = _container.Name,
                BlobName = blobName,
                Resource = "b",
                ExpiresOn = DateTimeOffset.UtcNow.AddMinutes(expiryMinutes)
            };

            sasBuilder.SetPermissions(BlobSasPermissions.Write | BlobSasPermissions.Create);

            return blobClient.GenerateSasUri(sasBuilder).ToString();
        }

        // ═══════════════════════════════════════════════════════════════════════
        // LEGACY — UploadFileAsync (kept for existing callers outside upload flow)
        // ═══════════════════════════════════════════════════════════════════════
        public async Task<string> UploadFileAsync(Stream fileStream, string folderName, string fileName)
        {
            var blobClient = _container.GetBlobClient(BlobPath(folderName, fileName));

            fileStream.Position = 0;

            var options = new BlobUploadOptions
            {
                TransferOptions = new StorageTransferOptions
                {
                    InitialTransferSize = 20 * 1024 * 1024,
                    MaximumConcurrency = 2
                }
            };

            await blobClient.UploadAsync(fileStream, options);
            return blobClient.Uri.ToString();
        }

        // ═══════════════════════════════════════════════════════════════════════
        // DOWNLOAD
        // ═══════════════════════════════════════════════════════════════════════
        public async Task<byte[]?> DownloadFileAsync(string folderName, string fileName)
        {
            var blobClient = _container.GetBlobClient(BlobPath(folderName, fileName));

            if (!await blobClient.ExistsAsync()) return null;

            using var ms = new MemoryStream();
            await blobClient.DownloadToAsync(ms);
            return ms.ToArray();
        }

        public async Task<Stream?> DownloadStreamAsync(string folderName, string fileName)
        {
            var blobClient = _container.GetBlobClient(BlobPath(folderName, fileName));

            if (!await blobClient.ExistsAsync()) return null;

            // BufferSize controls how much data the SDK fetches per internal HTTP
            // range request as the caller reads through the stream sequentially.
            // Default is 4 MB. For a 115 MB file read sequentially by ExcelDataReader,
            // 32 MB means ~4 HTTP requests instead of ~29 — fewer round trips to Azure.
            var options = new BlobOpenReadOptions(allowModifications: false)
            {
                BufferSize = 32 * 1024 * 1024  // 32 MB per range request
            };

            return await blobClient.OpenReadAsync(options);
        }

        // ═══════════════════════════════════════════════════════════════════════
        // DELETE
        // ═══════════════════════════════════════════════════════════════════════
        public async Task<bool> DeleteFileAsync(string folderName, string fileName)
        {
            var blobClient = _container.GetBlobClient(BlobPath(folderName, fileName));
            var response = await blobClient.DeleteIfExistsAsync();
            return response.Value;
        }

        public async Task DeleteIfExistsAsync(string blobName)
        {
            var blobClient = _container.GetBlobClient(blobName);
            await blobClient.DeleteIfExistsAsync();
        }

        // ═══════════════════════════════════════════════════════════════════════
        // MOVE (server-side copy within same storage account — near-instant)
        // ═══════════════════════════════════════════════════════════════════════
        public async Task<bool> MoveFileAsync(string sourceFolder, string destFolder, string fileName)
        {
            try
            {
                var sourceBlob = _container.GetBlobClient(BlobPath(sourceFolder, fileName));
                var destBlob = _container.GetBlobClient(BlobPath(destFolder, fileName));

                if (!await sourceBlob.ExistsAsync()) return false;

                // Server-side copy — no bytes leave Azure
                var copyOp = await destBlob.StartCopyFromUriAsync(sourceBlob.Uri);
                await copyOp.WaitForCompletionAsync();

                await sourceBlob.DeleteAsync();
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "MoveFileAsync failed: {Source} → {Dest}", sourceFolder, destFolder);
                return false;
            }
        }

        // ═══════════════════════════════════════════════════════════════════════
        // EXISTS
        // ═══════════════════════════════════════════════════════════════════════
        public async Task<bool> BlobExistsAsync(string blobName)
        {
            var response = await _container.GetBlobClient(blobName).ExistsAsync();
            return response.Value;
        }

        public async Task<bool> FileExistsAsync(string folderName, string fileName)
        {
            var response = await _container.GetBlobClient(BlobPath(folderName, fileName)).ExistsAsync();
            return response.Value;
        }

        // ═══════════════════════════════════════════════════════════════════════
        // URI
        // ═══════════════════════════════════════════════════════════════════════
        public string GetBlobUri(string blobName)
            => _container.GetBlobClient(blobName).Uri.ToString();

        // ═══════════════════════════════════════════════════════════════════════
        // LIST
        // ═══════════════════════════════════════════════════════════════════════
        public async Task<List<BlobFileInfo>> ListFilesAsync(string folderName)
        {
            var fileList = new List<BlobFileInfo>();
            var prefix = string.IsNullOrEmpty(folderName) ? "" : $"{folderName}/";

            await foreach (var blob in _container.GetBlobsAsync(
                BlobTraits.None, BlobStates.None, prefix, CancellationToken.None))
            {
                fileList.Add(new BlobFileInfo
                {
                    FileName = Path.GetFileName(blob.Name),
                    FilePath = blob.Name,
                    CreatedOn = blob.Properties.CreatedOn?.LocalDateTime ?? DateTime.MinValue,
                    FileSize = blob.Properties.ContentLength ?? 0
                });
            }

            return fileList;
        }

        public async Task<Dictionary<int, DateTime>> GetLatestFileDatesAsync(string folderName, int year)
        {
            var fileDates = new Dictionary<int, DateTime>();
            var prefix = string.IsNullOrEmpty(folderName) ? "" : $"{folderName}/";

            await foreach (var blob in _container.GetBlobsAsync(
                BlobTraits.None, BlobStates.None, prefix, CancellationToken.None))
            {
                var name = Path.GetFileName(blob.Name);

                if (!name.StartsWith("Filing_", StringComparison.OrdinalIgnoreCase)) continue;
                if (!name.Contains($"_{year}_")) continue;
                if (!name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) continue;

                var parts = Path.GetFileNameWithoutExtension(name).Split('_');
                if (parts.Length < 3 || !int.TryParse(parts[^1], out int empId)) continue;
                if (!blob.Properties.CreatedOn.HasValue) continue;

                var created = blob.Properties.CreatedOn.Value.LocalDateTime;
                if (!fileDates.TryGetValue(empId, out var existing) || created > existing)
                    fileDates[empId] = created;
            }

            return fileDates;
        }
    }
}
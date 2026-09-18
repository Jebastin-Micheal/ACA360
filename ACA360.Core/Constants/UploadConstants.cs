using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACA360.Core.Constants
{
    public static class UploadConstants
    {
        public const long SmallFileThreshold = 5 * 1024 * 1024;  // 10 MB  → direct stream
        public const long MaxAllowedFileSize = 120 * 1024 * 1024;  // 120 MB → hard reject
        public const int ChunkSize = 4 * 1024 * 1024;  // 4 MB   → SAS block size
        public const int SasExpiryMinutes = 60;                  // SAS URL valid for 60 min
        public static readonly string[] AllowedExtensions = { ".xlsx", ".xls", ".csv" };
    }
}

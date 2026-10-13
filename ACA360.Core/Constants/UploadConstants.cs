using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACA360.Core.Constants
{
    public static class UploadConstants
    {
        public const long SmallFileThreshold = 5 * 1024 * 1024;  // 5 MB   → direct stream
        public const long MaxAllowedFileSize = 120 * 1024 * 1024;  // 120 MB → hard reject
        public const int ChunkSize = 4 * 1024 * 1024;  // 4 MB   → SAS block size
        public const int SasExpiryMinutes = 60;                  // SAS URL valid for 60 min
        // .xlsx only: the structure check opens the file as an OpenXML workbook and looks
        // for the template's named sheets, so .xls and .csv can never pass it.
        public static readonly string[] AllowedExtensions = { ".xlsx" };
        public const string UnsupportedTypeMessage =
            "Only Excel workbooks (.xlsx) are supported. If your file is .xls or .csv, open it in Excel and save it as .xlsx.";
    }
}

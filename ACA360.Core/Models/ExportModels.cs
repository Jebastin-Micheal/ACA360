using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACA360.Core.Models
{
    public class ExportColumn
    {
        public string Key { get; set; } = string.Empty;
        public string Header { get; set; } = string.Empty;

        public ExportColumn() { }

        public ExportColumn(string key, string header)
        {
            Key = key;
            Header = header;
        }
    }
    public class ExportRequest
    {
        public string Title { get; set; } = "Export";
        public string FileName { get; set; } = "export";
        public List<ExportColumn> Columns { get; set; } = new List<ExportColumn>();
        public List<Dictionary<string, object?>> Rows { get; set; } = new List<Dictionary<string, object?>>();
    }
}
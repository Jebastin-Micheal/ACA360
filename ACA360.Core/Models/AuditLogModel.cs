using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Linq;

namespace ACA360.Core.Models
{
    public class AuditLogModel
    {
        public Guid Id { get; set; }
        public string Table_Name { get; set; }
        public string Record_Id { get; set; }
        public string Action_Type { get; set; }
        public string Old_Values { get; set; }
        public string New_Values { get; set; }
        public int User_Id { get; set; }
        public string User_Name { get; set; }
        public string User_Role { get; set; }
        public string IP_Address { get; set; }
        public DateTime Created_at { get; set; }

        // Helper properties to parse JSON for the View
        public string FormattedOldValues => FormatJsonToText(Old_Values);
        public string FormattedNewValues => FormatJsonToText(New_Values);

        private string FormatJsonToText(string jsonStr)
        {
            if (string.IsNullOrWhiteSpace(jsonStr) || jsonStr == "{}" || jsonStr == "[]" || jsonStr == "NULL")
                return "-";

            try
            {
                // Handle both Array (BULK_UPSERT) and Object (UPDATE/INSERT) formats
                var token = JToken.Parse(jsonStr);
                var dict = new Dictionary<string, string>();

                if (token is JArray arr && arr.Count > 0)
                {
                    token = arr.First; // Take first item from batch for display
                }

                if (token is JObject obj)
                {
                    var properties = obj.Properties()
                        .Where(p => p.Value.Type != JTokenType.Null && p.Value.ToString() != "")
                        .Select(p => $"{p.Name}: {p.Value}");

                    return string.Join(", ", properties);
                }
                return jsonStr;
            }
            catch
            {
                return jsonStr; // Return raw if parsing fails
            }
        }
    }
}

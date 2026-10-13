using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using System.Text.Json.Serialization;
using ACA360.Core.Binders;

namespace ACA360.Core.Models
{
    /// <summary>
    /// A calendar event. Its text is kept exactly as typed: the app upper-cases every other JSON
    /// string, which turned titles into capitals and broke URLs and guests' e-mail addresses.
    /// </summary>
    public class CalendarEventModel
    {
        public int Id { get; set; }
        [JsonConverter(typeof(KeepCaseStringJsonConverter))]
        public string Title { get; set; } = null!;
        [JsonConverter(typeof(KeepCaseStringJsonConverter))]
        public string? Description { get; set; }
        public DateTime StartDate { get; set; }
        public DateTime? EndDate { get; set; }
        public bool IsAllDay { get; set; }
        [JsonConverter(typeof(KeepCaseStringJsonConverter))]
        public string ColorCode { get; set; } = null!;
        [JsonConverter(typeof(KeepCaseStringJsonConverter))]
        public string? EventUrl { get; set; }
        [JsonConverter(typeof(KeepCaseStringJsonConverter))]
        public string? Location { get; set; }
        [JsonConverter(typeof(KeepCaseStringJsonConverter))]
        public string? Guests { get; set; }
    }
}
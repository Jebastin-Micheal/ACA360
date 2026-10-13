using System;

namespace ACA360.Core.Models
{
    public static class NoteEntityType
    {
        public const string Employer = "Employer";
        public const string Employee = "Employee";
        public const string Plan = "Plan";
        public const string Dependent = "Dependent";
    }

    public class NoteModel
    {
        public int NoteId { get; set; }
        public string? EntityType { get; set; }
        public int EntityId { get; set; }
        public string? Category { get; set; }
        public string? NoteText { get; set; }
        public int FilingYear { get; set; }
        public bool IsInternal { get; set; }
        public bool IsPinned { get; set; }
        public int CreatedByUserId { get; set; }
        public string? CreatedByName { get; set; }
        public DateTime CreatedDate { get; set; }
        public bool IsMine { get; set; }
        public string? SourceSystem { get; set; }
        public string FormattedDate => CreatedDate.ToString("MMM dd, yyyy");
    }

    public class NoteFilterModel
    {
        public string? EntityType { get; set; }
        public int EntityId { get; set; }
        public int? FilingYear { get; set; }
        public string? Category { get; set; }
        public int? CreatedByUserId { get; set; }
        public string? SearchText { get; set; }
    }

    public class SaveNoteModel
    {
        public int NoteId { get; set; }
        public string? EntityType { get; set; }
        public int EntityId { get; set; }
        public string? Category { get; set; }
        public string? NoteText { get; set; }
        public int FilingYear { get; set; }
        public bool IsInternal { get; set; }
    }

    public class NoteAuthorModel
    {
        public int UserId { get; set; }
        public string? UserName { get; set; }
    }
}

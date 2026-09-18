namespace ACA360.Core.Models
{
    public class UserPreferenceModel
    {
        public string? UserId { get; set; }
        public int? LastSelectedEmployerId { get; set; }
        public int? LastSelectedFilingYear { get; set; }
    }
}
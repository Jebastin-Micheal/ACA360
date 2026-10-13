namespace ACA360.Core.Models
{
    public class EmployerAggregatedMember
    {
        public int MemberId { get; set; }
        public int EmployerId { get; set; }
        public string? MemberName { get; set; }
        public string? MemberEIN { get; set; }
        public int OrderPriority { get; set; }
    }
}
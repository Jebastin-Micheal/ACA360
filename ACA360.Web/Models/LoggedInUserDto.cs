namespace ACA360.Web.Models
{
    public class LoggedInUserDto
    {
        public string? UserName { get; set; }
        public int RefID { get; set; }
        public string? RoleName { get; set; }
        public int IsMFA { get; set; }
        public List<string> Permissions { get; set; } = new List<string>();// Include from DB
    }
    public class SessionDataDto
    {
        public string? Key { get; set; }
        public string? Value { get; set; }
    }


}

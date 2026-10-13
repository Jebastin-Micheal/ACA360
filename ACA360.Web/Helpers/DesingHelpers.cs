namespace ACA360.Web.Helpers
{
    public class DesingHelpers
    {
        public static string GetRoleIcon(long? role)
        {
            return role switch
            {
                2 => "bxs-shield-plus text-danger",      // SuperAdmin
                3 => "bxs-shield text-primary",          // Admin
                4 => "bxs-user-badge text-info",         // ACA Director
                5 => "bxs-user-detail text-success",     // DA Supervisor
                6 => "bxs-user-voice text-warning",      // AM Supervisor
                10 => "bxs-briefcase text-dark",         // Broker
                11 => "bx-analyse text-purple",          // Data Analyst
                13 => "bxs-building text-secondary",     // Employer
                14 => "bxs-user-check text-success",     // StaffAC
                15 => "bxs-user-pin text-primary",       // StaffAU
                16 => "bxs-user-account text-warning",   // Account Manager
                _ => "bx-user"                           // default fallback
            };
        }

        public static string GetInitialColor(string? initials)
        {
            if (string.IsNullOrWhiteSpace(initials)) initials = "A";

            char c = char.ToUpper(initials[0]);

            return c switch
            {
                >= 'A' and <= 'D' => "primary",
                >= 'E' and <= 'H' => "success",
                >= 'I' and <= 'L' => "info",
                >= 'M' and <= 'P' => "warning",
                >= 'Q' and <= 'T' => "danger",
                >= 'U' and <= 'Z' => "secondary",
                _ => "dark" // Changed "bg-dark" to "dark" so it fits pattern "bg-label-{color}"
            };
        }
    }
}

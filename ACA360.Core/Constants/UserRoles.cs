namespace ACA360.Core.Constants
{
    public static class UserRoles
    {
        // --- 1. Individual Roles (Existing) ---
        public const string SuperAdmin = "SuperAdmin";
        public const string Admin = "Admin";
        public const string ACADirector = "ACA Director";
        public const string DASupervisor = "DA Supervisor";
        public const string AMSupervisor = "Account Manager Supervisor";
        public const string Broker = "Broker";
        public const string DataAnalyst = "Data Analyst";
        public const string Employer = "Employer";
        public const string StaffAC = "StaffAC";
        public const string StaffAU = "StaffAU";
        public const string AccountManager = "Account Manager";

        // --- 2. Combo Constants (The Simple Solution) ---

        // Use this for Controllers accessible by ANYONE logged in (except maybe external users if you want)
        public const string All = SuperAdmin + "," + Admin + "," + ACADirector + "," +
                                  DASupervisor + "," + AMSupervisor + "," + Broker + "," +
                                  DataAnalyst + "," + Employer + "," + StaffAC + "," +
                                  StaffAU + "," + AccountManager;

        // Use this for Internal Management Controllers (excluding Clients/Brokers)
        public const string InternalTeam = SuperAdmin + "," + Admin + "," + ACADirector + "," +
                                           DASupervisor + "," + AMSupervisor + "," +
                                           DataAnalyst + "," + AccountManager + "," +
                                           StaffAC + "," + StaffAU;

        // Roles permitted to upload a data file and to import it into the live tables.
        // The AM approval step was removed at the client's request, so the DA (and the
        // managers above them) now carry a file all the way from upload to import.
        // Deliberately excludes Broker, Employer, StaffAC and StaffAU: they may still
        // VIEW the file log, they may not put data into it or push data out of it.
        public const string FileHandlers = SuperAdmin + "," + Admin + "," + ACADirector + "," +
                                           DASupervisor + "," + AMSupervisor + "," +
                                           DataAnalyst + "," + AccountManager;
        // Consent/Employee-Portal feature — external self-service role + the internal
        // roles allowed to activate a portal login. (Added for the Consent + Employee Portal integration.)
        public const string EmployeeSelfService = "EmployeeSelfService";
        public const string PortalActivators = SuperAdmin + "," + Admin + "," + ACADirector + "," + AccountManager;
    }
}
namespace ACA360.Core.Enums.Tracker
{
    public enum EngagementStatus
    {
        Onboarding = 0,
        Active = 1,
        Hold = 2,
        Completed = 3,
        Cancelled = 4
    }

    public enum WorkflowStatus
    {
        Pending = 0,
        InProgress = 1,
        Review = 2,
        Completed = 3,
        Blocked = 4
    }

    public enum FundingType
    {
        FullyInsured = 1,
        SelfFunded = 2,
        LevelFunded = 3
    }

    public enum BandingType
    {
        Monthly = 1,
        Weekly = 2,
        BiWeekly = 3
    }
}
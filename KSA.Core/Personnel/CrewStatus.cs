namespace KSA.Core.Personnel
{
    /// <summary>Where a crew member is right now. Order matters: it is the default roster sort order.</summary>
    public enum CrewStatus
    {
        Available = 0,
        Assigned = 1,
        Missing = 2,
        Dead = 3
    }
}

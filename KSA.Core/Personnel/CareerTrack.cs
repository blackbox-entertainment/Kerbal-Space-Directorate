namespace KSA.Core.Personnel
{
    /// <summary>Which career ladder a crew member climbs. Ladders themselves are data-driven (see CareerCatalog).</summary>
    public enum CareerTrack
    {
        None = 0,
        Flight = 1,
        Engineering = 2,
        Science = 3,
        Management = 4
    }
}

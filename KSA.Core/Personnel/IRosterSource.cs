using System.Collections.Generic;

namespace KSA.Core.Personnel
{
    /// <summary>Supplies the current crew roster. Implemented by the host (KSA.KSP reads the save's crew list).</summary>
    public interface IRosterSource
    {
        /// <summary>Returns a fresh snapshot of every hired crew member, including the dead and missing.</summary>
        IReadOnlyList<CrewRecord> GetRoster();
    }
}

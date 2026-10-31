using System.Collections.Generic;

namespace KSA.Core
{
    /// <summary>One upgradeable building at the space center, as the host sees it (2026-10-30).</summary>
    public sealed class FacilityOption
    {
        /// <summary>Stable id the AI Director uses: Runway, LaunchPad, VehicleAssemblyBuilding, ...</summary>
        public string Id { get; set; }
        public string Title { get; set; }
        /// <summary>1-based level now.</summary>
        public int Level { get; set; }
        public int MaxLevel { get; set; }
        /// <summary>Funds for the next level; 0 at the top.</summary>
        public double UpgradeCost { get; set; }
        /// <summary>What the building limits today, in a few words ("craft up to 18 t").</summary>
        public string LimitNow { get; set; }
        /// <summary>What the next level allows ("craft up to 140 t").</summary>
        public string NextLevel { get; set; }
        /// <summary>Launch pad and runway: the heaviest craft they take, tonnes (0 = no limit or not a launch site).</summary>
        public double MassLimit { get; set; }
        /// <summary>VAB and SPH: the most parts a craft may have (0 = no limit or not an assembly building).</summary>
        public int PartLimit { get; set; }
        /// <summary>Astronaut Complex: the most active kerbals on staff (0 = no limit or not known).</summary>
        public int CrewLimit { get; set; }
        /// <summary>Astronaut Complex: no spacewalks in flight (only on Kerbin's surface) at this level.</summary>
        public bool NoEva { get; set; }
        /// <summary>Mission Control: no flight planning (manoeuvre nodes) at this level.</summary>
        public bool NoFlightPlanning { get; set; }
        /// <summary>Tracking Station: no patched conics (no predicted encounters with other worlds) at this level.</summary>
        public bool NoPatchedConics { get; set; }
        /// <summary>Research and Development: the costliest technology it can research, Science (0 = no limit).</summary>
        public double ScienceLimit { get; set; }
        public bool CanUpgrade { get { return Level < MaxLevel && UpgradeCost > 0; } }

        public FacilityOption() { Id = string.Empty; Title = string.Empty; LimitNow = string.Empty; NextLevel = string.Empty; }
    }

    /// <summary>
    /// The space center's buildings (2026-10-30, T: "is the AI Director upgrading the facilities at all? the runway needs to
    /// be upgraded"). The host reports levels and raises one; the agency pays from its ledger.
    /// </summary>
    public interface IFacilityHost
    {
        List<FacilityOption> Facilities();
        /// <summary>True when buildings can be upgraded right now (the Space Center scene, where they are loaded).</summary>
        bool CanUpgradeNow { get; }
        /// <summary>Raises the building one level. The agency has already paid. False with the reason when it cannot.</summary>
        bool RaiseLevel(string id, out string message);
    }
}

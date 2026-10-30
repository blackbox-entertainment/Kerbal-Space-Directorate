using System;
using System.Collections.Generic;

namespace KSA.Core.Flight
{
    /// <summary>
    /// Why a flight failed, as the flight director saw it (2026-10-30, T: "does the AI Director learn from mistakes?").
    /// The cause becomes the lesson, and the engineers' next design for that mission fixes it.
    /// </summary>
    public enum FailureCause
    {
        None = 0,
        /// <summary>The rocket turned sideways or spun in the air while climbing.</summary>
        Tumbled = 1,
        /// <summary>The propellant ran out before the job was done.</summary>
        OutOfFuel = 2,
        /// <summary>A part overheated and was lost coming down through the air.</summary>
        BurnedUp = 3,
        /// <summary>The capsule hit the ground too fast: parachutes too small, or none open.</summary>
        HardLanding = 4,
        /// <summary>A suborbital flight that never reached space.</summary>
        BelowSpace = 5
    }

    public static class FailureCauses
    {
        /// <summary>What the engineers will change on the next design for the mission (null for no cause).</summary>
        public static string Fix(FailureCause c)
        {
            switch (c)
            {
                case FailureCause.Tumbled: return "the next design gets fins on the second stage as well as the first";
                case FailureCause.OutOfFuel: return "the next design carries more delta-v margin";
                case FailureCause.BelowSpace: return "the next design carries more delta-v margin";
                case FailureCause.BurnedUp: return "the next design carries a heat shield under the pod (research one if none is available)";
                case FailureCause.HardLanding: return "the next design carries half as much parachute again as the capsule needs";
                default: return null;
            }
        }

        /// <summary>The lesson for a failed flight: what happened, and the fix.</summary>
        public static string Lesson(FailureCause c, string detail, string projectName)
        {
            if (c == FailureCause.None) return null;
            string what = string.IsNullOrEmpty(detail) ? Describe(c) : detail.TrimEnd('.');
            return projectName + ": " + what + "; " + Fix(c) + ".";
        }

        public static string Describe(FailureCause c)
        {
            switch (c)
            {
                case FailureCause.Tumbled: return "the rocket tumbled in the air";
                case FailureCause.OutOfFuel: return "it ran out of propellant";
                case FailureCause.BurnedUp: return "it overheated coming down";
                case FailureCause.HardLanding: return "the capsule hit the ground too fast";
                case FailureCause.BelowSpace: return "it never reached space";
                default: return "nothing";
            }
        }

        public static FailureCause Parse(string s)
        {
            FailureCause c;
            return !string.IsNullOrEmpty(s) && Enum.TryParse(s, true, out c) && Enum.IsDefined(typeof(FailureCause), c) ? c : FailureCause.None;
        }

        /// <summary>How many times a cause appears in a list (each repeat adds more margin).</summary>
        public static int Count(IEnumerable<FailureCause> causes, FailureCause c)
        {
            int n = 0;
            if (causes != null) foreach (FailureCause x in causes) if (x == c) n++;
            return n;
        }
    }
}

using System;
using KSA.Core.Data;

namespace KSA.Core.Personnel
{
    /// <summary>
    /// How Courage and Stupidity move with experience (GDD decision 17, Jira KSA-50). Small steps, so a veteran's
    /// edge shows over a career rather than after one flight. Values come from the TRAIT_DRIFT block of
    /// KSA_AGENCY_SETTINGS with these defaults.
    /// </summary>
    public sealed class TraitDrift
    {
        public const string NodeName = "TRAIT_DRIFT";

        public bool Enabled { get; set; } = true;
        public float StupidityFloor { get; set; } = 0.15f;
        public float CourageMin { get; set; } = 0.10f;
        public float CourageMax { get; set; } = 0.95f;

        /// <summary>Per completed mission: success.</summary>
        public float SuccessStupidity { get; set; } = -0.03f;
        public float SuccessCourage { get; set; } = 0.03f;
        /// <summary>Partial success.</summary>
        public float PartialStupidity { get; set; } = -0.02f;
        public float PartialCourage { get; set; } = 0.01f;
        /// <summary>Abort or vehicle lost, crew survived: a close call teaches a lot.</summary>
        public float SurvivedFailureStupidity { get; set; } = -0.05f;
        public float SurvivedFailureCourage { get; set; } = 0.02f;
        /// <summary>A crewmate died on the same mission (survivors).</summary>
        public float CrewmateDeathCourage { get; set; } = -0.05f;
        /// <summary>Someone else in the agency died.</summary>
        public float ColleagueDeathCourage { get; set; } = -0.02f;
        /// <summary>A whole year without a mission.</summary>
        public float IdleYearCourage { get; set; } = -0.02f;

        public static TraitDrift Parse(DataNode node)
        {
            var d = new TraitDrift();
            if (node == null) return d;
            d.Enabled = node.GetBool("enabled", d.Enabled);
            d.StupidityFloor = (float)node.GetDouble("stupidityFloor", d.StupidityFloor);
            d.CourageMin = (float)node.GetDouble("courageMin", d.CourageMin);
            d.CourageMax = (float)node.GetDouble("courageMax", d.CourageMax);
            d.SuccessStupidity = (float)node.GetDouble("successStupidity", d.SuccessStupidity);
            d.SuccessCourage = (float)node.GetDouble("successCourage", d.SuccessCourage);
            d.PartialStupidity = (float)node.GetDouble("partialStupidity", d.PartialStupidity);
            d.PartialCourage = (float)node.GetDouble("partialCourage", d.PartialCourage);
            d.SurvivedFailureStupidity = (float)node.GetDouble("survivedFailureStupidity", d.SurvivedFailureStupidity);
            d.SurvivedFailureCourage = (float)node.GetDouble("survivedFailureCourage", d.SurvivedFailureCourage);
            d.CrewmateDeathCourage = (float)node.GetDouble("crewmateDeathCourage", d.CrewmateDeathCourage);
            d.ColleagueDeathCourage = (float)node.GetDouble("colleagueDeathCourage", d.ColleagueDeathCourage);
            d.IdleYearCourage = (float)node.GetDouble("idleYearCourage", d.IdleYearCourage);
            return d;
        }

        /// <summary>Applies a change and clamps. Returns true when either trait actually moved.</summary>
        public bool Apply(CrewProfile p, float courageDelta, float stupidityDelta)
        {
            if (!Enabled || p == null) return false;
            // Move only in the direction of the delta and never past a band edge; a kerbal already outside the band
            // (stock Bob starts at 0.1 stupidity) is left where they are rather than dragged to the edge.
            float c = Step(p.Courage, courageDelta, CourageMin, CourageMax);
            float s = Step(p.Stupidity, stupidityDelta, StupidityFloor, 1f);
            bool moved = Math.Abs(c - p.Courage) > 0.0001f || Math.Abs(s - p.Stupidity) > 0.0001f;
            if (!moved) return false;
            p.Courage = c;
            p.Stupidity = s;
            p.TraitsOwned = true;
            return true;
        }

        public bool AfterMission(CrewProfile p, MissionOutcome outcome, bool crewmateDied)
        {
            float c = 0, s = 0;
            switch (outcome)
            {
                case MissionOutcome.Success: c = SuccessCourage; s = SuccessStupidity; break;
                case MissionOutcome.PartialSuccess: c = PartialCourage; s = PartialStupidity; break;
                case MissionOutcome.Abort:
                case MissionOutcome.LossOfVehicle:
                case MissionOutcome.LossOfCrew: // survivors only; the caller skips the dead
                    c = SurvivedFailureCourage; s = SurvivedFailureStupidity; break;
                default: return false;
            }
            if (crewmateDied) c += CrewmateDeathCourage;
            return Apply(p, c, s);
        }

        private static float Step(float v, float d, float lo, float hi)
        {
            float n = v + d;
            if (d > 0f && n > hi) n = Math.Max(v, hi);
            else if (d < 0f && n < lo) n = Math.Min(v, lo);
            return n;
        }
    }
}

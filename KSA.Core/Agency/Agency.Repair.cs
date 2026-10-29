using System;
using System.Collections.Generic;
using KSA.Core.Personnel;
using KSA.Core.Projects;
using KSA.Core.Finance;

namespace KSA.Core
{
    /// <summary>
    /// Save repair (2026-10-28): before this date a hire could reach the game's roster under a random name, because the
    /// host renamed the new kerbal before the roster held it and the game refuses that silently. The agency then saw its
    /// hire vanish (marked dismissed the same day) and the random name turn up as a stranger. This finds those pairs so
    /// the host can give the kerbal its proper name back; the next roster sync then revives the hire's own record.
    /// </summary>
    public sealed partial class Agency
    {
        /// <summary>
        /// Pairs of (name in the game's roster, name the agency hired) for hires lost to the old rename bug: a record
        /// dismissed on its hiring day and missing from the roster, matched to a roster kerbal of the same role that the
        /// agency does not know, or first saw on or after that day and has never flown. Earliest hires match first.
        /// </summary>
        public List<KeyValuePair<string, string>> MisnamedHires(IList<CrewSnapshot> roster)
        {
            var pairs = new List<KeyValuePair<string, string>>();
            if (roster == null) return pairs;
            var inRoster = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (CrewSnapshot s in roster) if (s != null) inRoster.Add(s.Name);

            var lost = new List<CrewProfile>();
            foreach (CrewProfile p in Crew.All)
                if (p.Departed && p.DepartureReason == DepartureReason.Fired && p.DepartedDay == p.HiredDay && !inRoster.Contains(p.Id))
                    lost.Add(p);
            lost.Sort((a, b) => a.HiredDay.CompareTo(b.HiredDay));

            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (CrewProfile p in lost)
            {
                foreach (CrewSnapshot s in roster)
                {
                    if (s == null || used.Contains(s.Name) || s.Role != p.Role) continue;
                    CrewProfile known = Crew.Get(s.Name);
                    bool stranger = known == null
                        || (!known.Departed && known.HiredDay >= p.HiredDay && known.Record.Missions.Count == 0 && known.ProjectId.Length == 0);
                    if (!stranger) continue;
                    used.Add(s.Name);
                    pairs.Add(new KeyValuePair<string, string>(s.Name, p.Id));
                    break;
                }
            }
            return pairs;
        }

        /// <summary>
        /// Save repair (2026-10-28): before 0.5.6 a Watch Live project whose rocket could not be assembled was simulated
        /// instead, and a simulated failure could strand its crew on a ship that never existed. Each such stranding is
        /// undone: the crew is home with no mission, medal or prestige from it, and the project is scrubbed with its budget
        /// returned. Returns the projects repaired.
        /// </summary>
        public List<string> UndoPhantomStrandings()
        {
            var fixedIds = new List<string>();
            foreach (Stranding s in Strandings)
            {
                if (s.Resolved || s.VesselId.Length > 0 || s.RescueProjectId.Length > 0) continue;
                Project p = null;
                foreach (Project q in Projects)
                    if (!q.IsActive && q.Status != ProjectStatus.Cancelled && string.Equals(q.Name, s.Cause, StringComparison.OrdinalIgnoreCase)
                        && q.Design != null && q.Design.IsAi && string.IsNullOrEmpty(q.Design.CraftPath) && string.IsNullOrEmpty(q.TemplateId)
                        && q.Notes.Exists(n => n.StartsWith("Live flight abandoned", StringComparison.Ordinal)))
                    { p = q; }
                if (p == null) continue;
                foreach (string id in new List<string>(s.Crew))
                {
                    CrewProfile c = Crew.Get(id);
                    if (c == null) continue;
                    if (p.MissionId.Length > 0)
                    {
                        c.Record.Missions.RemoveAll(m => m.MissionId == p.MissionId);
                        c.Record.Medals.RemoveAll(m => m.MissionId == p.MissionId);
                    }
                    c.InFlightSinceUT = -1;
                    if (c.Status == CrewStatus.Assigned) c.Status = CrewStatus.Available;
                }
                s.Crew.Clear();
                s.Resolved = true;
                State.Prestige = Math.Min(100, State.Prestige + 2);
                double refund = Math.Round(p.Budget);
                if (refund > 0) Ledger.Deposit(refund, LedgerCategory.Cancellation, "Scrubbed " + p.Name + " (no rocket was built)", CurrentDay, CurrentYear);
                p.Status = ProjectStatus.Cancelled;
                p.Notes.Add("Scrubbed by the 0.5.6 repair: the rocket was never built, so the flight never happened. The crew is home and " + Money(refund) + " was returned.");
                Push(null, p.Name + " never flew (no rocket was built): " + CrewNames(new List<string>(p.Crew)) + " home, " + Money(refund) + " returned.");
                RaiseProjectChanged(p);
                fixedIds.Add(p.Id);
            }
            return fixedIds;
        }

        /// <summary>The host renamed <paramref name="rosterName"/> back to the hire's name: drop the stranger's record so it does not linger as a dismissal.</summary>
        public void ForgetMisnamedHire(string rosterName, string hiredName)
        {
            CrewProfile stray = Crew.Get(rosterName);
            if (stray != null && !string.Equals(rosterName, hiredName, StringComparison.OrdinalIgnoreCase)) Crew.Remove(rosterName);
            Log.Info("Roster repair: " + rosterName + " is " + hiredName + " again");
        }
    }
}

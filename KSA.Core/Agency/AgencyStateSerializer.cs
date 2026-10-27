using System;
using System.Collections.Generic;
using KSA.Core.Data;
using KSA.Core.Personnel;

namespace KSA.Core
{
    /// <summary>
    /// Agency state to and from a <see cref="DataNode"/> tree (Jira KSA-22). The host stores the tree in its save file.
    /// Format version 1:
    ///   KSA_AGENCY { version, nextMissionNumber, CREW { ...fields..., MISSION {}, MEDAL {}, INCIDENT {}, achievement = ..., quote = ..., tag = ... } }
    /// Unknown keys are ignored so older saves keep loading.
    /// </summary>
    public static class AgencyStateSerializer
    {
        public const string RootName = "KSA_AGENCY";
        public const int CurrentVersion = 1;

        public static DataNode Write(Agency agency)
        {
            if (agency == null) throw new ArgumentNullException(nameof(agency));
            var root = new DataNode(RootName);
            root.AddValue("version", CurrentVersion);
            root.AddValue("nextMissionNumber", agency.NextMissionNumber);

            foreach (CrewProfile p in agency.Crew.All)
            {
                DataNode c = root.AddChild("CREW");
                c.AddValue("id", p.Id);
                c.AddValue("name", p.Name);
                c.AddValue("role", p.Role.ToString());
                c.AddValue("stars", p.Stars);
                c.AddValue("courage", p.Courage);
                c.AddValue("stupidity", p.Stupidity);
                c.AddValue("status", p.Status.ToString());
                c.AddValue("track", p.Track.ToString());
                c.AddValue("rank", p.RankId);
                c.AddValue("morale", p.Morale);
                c.AddValue("salary", p.Salary);
                c.AddValue("bornDay", p.BornDay);
                c.AddValue("hiredDay", p.HiredDay);
                c.AddValue("inFlightSinceUT", p.InFlightSinceUT);
                c.AddValue("departed", p.Departed);
                c.AddValue("daysInSpace", p.Record.DaysInSpace);
                foreach (string tag in p.PersonalityTags) c.AddValue("tag", tag);
                foreach (string a in p.Record.Achievements) c.AddValue("achievement", a);
                foreach (string q in p.Record.Quotes) c.AddValue("quote", q);

                foreach (MissionEntry m in p.Record.Missions)
                {
                    DataNode n = c.AddChild("MISSION");
                    n.AddValue("id", m.MissionId);
                    n.AddValue("name", m.Name);
                    n.AddValue("startDay", m.StartDay);
                    n.AddValue("endDay", m.EndDay);
                    n.AddValue("outcome", m.Outcome.ToString());
                    n.AddValue("directorFlown", m.DirectorFlown);
                }
                foreach (MedalAward a in p.Record.Medals)
                {
                    DataNode n = c.AddChild("MEDAL");
                    n.AddValue("id", a.MedalId);
                    n.AddValue("day", a.Day);
                    n.AddValue("mission", a.MissionId);
                    n.AddValue("note", a.Note);
                }
                foreach (IncidentEntry i in p.Record.Incidents)
                {
                    DataNode n = c.AddChild("INCIDENT");
                    n.AddValue("kind", i.Kind.ToString());
                    n.AddValue("day", i.Day);
                    n.AddValue("mission", i.MissionId);
                    n.AddValue("description", i.Description);
                }
            }
            return root;
        }

        /// <summary>Replaces the agency's crew registry with the saved state.</summary>
        public static void Read(DataNode root, Agency agency)
        {
            if (agency == null) throw new ArgumentNullException(nameof(agency));
            agency.Crew.Clear();
            if (root == null) return;

            agency.NextMissionNumber = Math.Max(1, root.GetInt("nextMissionNumber", 1));

            foreach (DataNode c in root.GetChildren("CREW"))
            {
                string id = c.GetValue("id", null);
                if (string.IsNullOrEmpty(id)) continue;

                var p = new CrewProfile(id, c.GetValue("name", id))
                {
                    Role = ParseEnum(c.GetValue("role"), CrewRole.Unknown),
                    Stars = c.GetInt("stars", 0),
                    Courage = (float)c.GetDouble("courage", 0.5),
                    Stupidity = (float)c.GetDouble("stupidity", 0.5),
                    Status = ParseEnum(c.GetValue("status"), CrewStatus.Available),
                    Track = ParseEnum(c.GetValue("track"), CareerTrack.None),
                    RankId = c.GetValue("rank", string.Empty),
                    Morale = c.GetInt("morale", CrewProfile.DefaultMorale),
                    Salary = c.GetInt("salary", 0),
                    BornDay = c.GetInt("bornDay", 0),
                    HiredDay = c.GetInt("hiredDay", 0),
                    InFlightSinceUT = c.GetDouble("inFlightSinceUT", -1),
                    Departed = c.GetBool("departed", false)
                };
                p.Record.DaysInSpace = c.GetDouble("daysInSpace", 0);
                p.PersonalityTags.AddRange(c.GetValues("tag"));
                foreach (string a in c.GetValues("achievement")) p.Record.Achievements.Add(a);
                p.Record.Quotes.AddRange(c.GetValues("quote"));

                foreach (DataNode n in c.GetChildren("MISSION"))
                {
                    p.Record.Missions.Add(new MissionEntry
                    {
                        MissionId = n.GetValue("id", string.Empty),
                        Name = n.GetValue("name", string.Empty),
                        StartDay = n.GetInt("startDay", 0),
                        EndDay = n.GetInt("endDay", -1),
                        Outcome = ParseEnum(n.GetValue("outcome"), MissionOutcome.InProgress),
                        DirectorFlown = n.GetBool("directorFlown", false)
                    });
                }
                foreach (DataNode n in c.GetChildren("MEDAL"))
                {
                    p.Record.Medals.Add(new MedalAward
                    {
                        MedalId = n.GetValue("id", string.Empty),
                        Day = n.GetInt("day", 0),
                        MissionId = n.GetValue("mission", string.Empty),
                        Note = n.GetValue("note", string.Empty)
                    });
                }
                foreach (DataNode n in c.GetChildren("INCIDENT"))
                {
                    p.Record.Incidents.Add(new IncidentEntry
                    {
                        Kind = ParseEnum(n.GetValue("kind"), IncidentKind.Other),
                        Day = n.GetInt("day", 0),
                        MissionId = n.GetValue("mission", string.Empty),
                        Description = n.GetValue("description", string.Empty)
                    });
                }
                agency.Crew.Add(p);
            }
        }

        private static T ParseEnum<T>(string text, T fallback) where T : struct
        {
            T value;
            return text != null && Enum.TryParse(text, true, out value) && Enum.IsDefined(typeof(T), value) ? value : fallback;
        }
    }
}

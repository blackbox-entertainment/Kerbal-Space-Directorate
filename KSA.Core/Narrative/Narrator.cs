using System;
using System.Collections.Generic;
using System.Text;
using KSA.Core.Missions;
using KSA.Core.Personnel;
using KSA.Core.Projects;
using KSA.Core.Review;

namespace KSA.Core.Narrative
{
    /// <summary>
    /// Canned narrative (M6). Every item the agency publishes starts here; an AI pass may rewrite it later.
    /// Text is deliberately short and dry: it reads fine on its own and gives a local model a solid brief.
    /// </summary>
    public static class Narrator
    {
        public static NewsItem MissionResolved(MissionReport r, MissionType type, IList<string> crewNames, string agencyName, Random rng)
        {
            string crew = Join(crewNames);
            string where = type != null ? type.Body : "space";
            var item = new NewsItem { Kind = NewsKind.PressRelease, Related = r.ProjectId, Byline = agencyName + " Press Office" };
            switch (r.Outcome)
            {
                case MissionOutcome.Success:
                    item.Headline = r.ProjectName + " succeeds" + (r.Mode == MissionMode.WatchLive ? " in a flight watched from Mission Control" : string.Empty);
                    item.Body = Pick(rng,
                        agencyName + " is pleased to report that " + r.ProjectName + " met every objective. " + crew + " returned safely after " + Days(r.DaysInSpace) + ". " + Achievements(r) + " The Director thanked the crew and the engineering team.",
                        "Mission " + r.ProjectName + " is complete. " + crew + " performed flawlessly on the " + where + " mission and are back on Kerbin. " + Achievements(r) + " Further flights are being planned.") ;
                    break;
                case MissionOutcome.PartialSuccess:
                    item.Headline = r.ProjectName + " returns with mixed results";
                    item.Body = agencyName + " confirms " + r.ProjectName + " has concluded. " + crew + " returned safely, although the mission's primary objective was not fully achieved. " + Achievements(r) + " An internal review is under way: " + r.Lesson;
                    break;
                case MissionOutcome.Abort:
                    item.Headline = r.ProjectName + " aborted, crew safe";
                    item.Body = r.ProjectName + " was called off early. " + crew + " are safe and in good spirits. The agency will study the flight data before the next attempt. " + r.Lesson;
                    break;
                case MissionOutcome.LossOfVehicle:
                    item.Headline = r.ProjectName + ": vehicle lost, crew recovered";
                    item.Body = agencyName + " regrets to report the loss of the " + r.ProjectName + " vehicle. " + crew + " were recovered safely. The flight safety office has opened an investigation. " + r.Lesson;
                    break;
                default:
                    item.Kind = NewsKind.Obituary;
                    item.Headline = "In memoriam: " + crew;
                    item.Body = "With profound sorrow, " + agencyName + " announces the loss of " + crew + " aboard " + r.ProjectName + ". They flew so that others could follow. Flags at the space centre are at half mast. A memorial will be held in the coming days.";
                    break;
            }
            if (!string.IsNullOrEmpty(r.PostMortem) && r.Outcome != MissionOutcome.Success) item.Body += "\n\nFrom the " + KSA.Core.Flight.PostMortemWriter.Title(r) + ": " + r.PostMortem;
            return item;
        }

        public static NewsItem YearEnded(ReviewResult r, string agencyName)
        {
            var item = new NewsItem { Kind = NewsKind.Review, Byline = "Office of the Minister for Science", Related = "year:" + r.Year };
            item.Headline = "Government review, year " + r.Year + ": " + r.BandText.ToLowerInvariant();
            var sb = new StringBuilder();
            sb.Append("The Ministry has completed its annual review of " + agencyName + ". Of " + r.GoalsIssued + " goals set, " + r.GoalsMet + " were met. ");
            if (r.Deaths > 0) sb.Append(r.Deaths + " crew member" + (r.Deaths == 1 ? " was" : "s were") + " lost this year, which weighed heavily on the assessment. ");
            switch (r.Band)
            {
                case ReviewBand.Excellent: sb.Append("The Ministry commends the Director and the staff. "); break;
                case ReviewBand.Good: sb.Append("The Ministry is satisfied with the agency's progress. "); break;
                case ReviewBand.Flat: sb.Append("The Ministry expects clearer results next year. "); break;
                case ReviewBand.Lean: sb.Append("The Ministry is disappointed. Funding has been reduced and the agency is expected to tighten its programme. "); break;
                default: sb.Append("The Ministry considers the year a failure. "); break;
            }
            sb.Append("Next year's grant is " + Agency.Money(r.NextGrant) + (r.BudgetChangePercent >= 0 ? " (+" : " (") + r.BudgetChangePercent.ToString("0") + "%).");
            if (r.HiringFreeze) sb.Append(" A hiring freeze is in effect.");
            if (r.Layoffs > 0) sb.Append(" " + r.Layoffs + " position(s) have been cut.");
            if (r.DirectorReplaced) sb.Append(" The Director has been relieved of their post; a successor has been appointed.");
            else if (r.ProbationAfter && !r.ProbationBefore) sb.Append(" The Director has been placed on probation.");
            item.Body = sb.ToString();
            return item;
        }

        public static NewsItem Hired(CrewProfile p, string sourceName, string agencyName)
        {
            return new NewsItem
            {
                Kind = NewsKind.Ticker, Related = p.Id, Byline = agencyName,
                Headline = p.Name + " joins " + agencyName + (string.IsNullOrEmpty(sourceName) ? string.Empty : " from " + sourceName),
                Body = p.Name + " (" + p.Role + ") has been hired" + (string.IsNullOrEmpty(sourceName) ? "." : " through " + sourceName + ".") + (p.PersonalityTags.Count > 0 ? " Colleagues describe them as " + Join(p.PersonalityTags).ToLowerInvariant() + "." : string.Empty)
            };
        }

        public static NewsItem Departed(CrewProfile p, DepartureReason reason, string agencyName, Random rng)
        {
            switch (reason)
            {
                case DepartureReason.Quit:
                    return new NewsItem
                    {
                        Kind = NewsKind.Memo, Related = p.Id, Byline = p.Name,
                        Headline = p.Name + " resigns",
                        Body = Pick(rng,
                            "Director, I have decided to leave the agency. I came here to fly, and I have spent too long waiting. I wish the others well. - " + p.Name,
                            "To the Director: please accept my resignation, effective today. The last few years have not been what I hoped. No hard feelings. - " + p.Name)
                    };
                case DepartureReason.Retired:
                    return new NewsItem
                    {
                        Kind = NewsKind.PressRelease, Related = p.Id, Byline = agencyName + " Press Office",
                        Headline = p.Name + " retires after " + p.Record.Missions.Count + " mission" + (p.Record.Missions.Count == 1 ? string.Empty : "s"),
                        Body = agencyName + " thanks " + p.Name + " for years of service. Their record stands at " + p.Record.Missions.Count + " missions, " + p.Record.Medals.Count + " decorations and " + Math.Round(p.Record.DaysInSpace) + " days in space. They leave with the agency's gratitude."
                    };
                case DepartureReason.Fired:
                    return new NewsItem { Kind = NewsKind.Ticker, Related = p.Id, Byline = agencyName, Headline = p.Name + " leaves the agency", Body = p.Name + " has been released from service." };
                case DepartureReason.Died:
                    return new NewsItem
                    {
                        Kind = NewsKind.Obituary, Related = p.Id, Byline = agencyName + " Press Office",
                        Headline = "In memoriam: " + p.Name,
                        Body = agencyName + " mourns " + p.Name + ", who died in service. " + p.Record.Missions.Count + " mission" + (p.Record.Missions.Count == 1 ? string.Empty : "s") + ", " + Math.Round(p.Record.DaysInSpace) + " days in space. We will remember them."
                    };
                default:
                    return null;
            }
        }

        public static NewsItem ProjectStarted(Project p, MissionType type, string leadName, string agencyName)
        {
            return new NewsItem
            {
                Kind = NewsKind.Ticker, Related = p.Id, Byline = agencyName,
                Headline = agencyName + " announces " + p.Name,
                Body = "Project " + p.Name + " (" + (type != null ? type.Name : p.MissionTypeId) + ") has been approved with a budget of " + Agency.Money(p.Budget) + "." + (string.IsNullOrEmpty(leadName) ? string.Empty : " " + leadName + " leads the engineering team.")
            };
        }

        public static NewsItem DesignApproved(Project p, Design.DesignRecord d, string leadName)
        {
            return new NewsItem
            {
                Kind = NewsKind.Memo, Related = p.Id, Byline = string.IsNullOrEmpty(leadName) ? "Engineering" : leadName,
                Headline = "Design review: " + d.DesignName + " approved for " + p.Name,
                Body = "Director, the team has signed off on " + d.DesignName + " after " + d.PassesUsed + " review pass" + (d.PassesUsed == 1 ? string.Empty : "es") + ". " + d.TotalDeltaV + " m/s against a requirement of " + d.RequiredDeltaV + ", liftoff TWR " + d.LiftoffTwr + ", " + d.PartCount + " parts. " + (string.IsNullOrEmpty(d.Rationale) ? string.Empty : d.Rationale)
            };
        }

        public static NewsItem LowMoraleMemo(CrewProfile p, Random rng)
        {
            return new NewsItem
            {
                Kind = NewsKind.Memo, Related = p.Id, Byline = p.Name,
                Headline = "Memo from " + p.Name,
                Body = Pick(rng,
                    "Director, I'll be blunt: morale in my corner of the building is low. I haven't flown in a long while and I'm not sure anyone has noticed. - " + p.Name,
                    "Director, a quiet word. I signed up to go to space. If there is a seat with my name on it this year, I'd like to know. - " + p.Name,
                    "Director, people are talking about leaving. I'm not there yet, but I'd be lying if I said I hadn't thought about it. - " + p.Name)
            };
        }

        // ------------------------------------------------------------------ AI prompts

        public static string SystemPrompt(NewsItem item, string agencyName)
        {
            switch (item.Kind)
            {
                case NewsKind.Memo:
                    return "You write short internal memos from a kerbal astronaut or engineer at " + agencyName + " to the Director. First person, 3 to 5 sentences, plain text, no headings, keep the writer's name and every fact in the brief. Kerbals are earnest and a little odd; wry is fine, cruel is not. Reply with the memo text only.";
                case NewsKind.Obituary:
                    return "You are the press office of " + agencyName + ". Write a respectful, understated obituary notice, 3 to 5 sentences, plain text. Keep every fact in the brief; invent nothing about the cause. Reply with the text only.";
                case NewsKind.Review:
                    return "You are a government ministry issuing a formal statement about " + agencyName + ". 3 to 5 sentences, bureaucratic but readable, plain text. Keep every number and fact in the brief. Reply with the statement only.";
                default:
                    return "You are the press office of " + agencyName + ", a small, scrappy space agency on the planet Kerbin. Write a press release of 3 to 5 sentences in plain text: no headings, no bullet points, no markdown. Keep every name, number and fact in the brief and invent no new ones. Tone: upbeat but honest. Reply with the text only.";
            }
        }

        public static string UserPrompt(NewsItem item)
        {
            return "BRIEF (facts to keep):\nHeadline: " + item.Headline + "\n" + item.Body + "\n\nRewrite this as the finished piece now.";
        }

        // ------------------------------------------------------------------ helpers

        public static string Join(IList<string> names)
        {
            if (names == null || names.Count == 0) return "the crew";
            if (names.Count == 1) return names[0];
            if (names.Count == 2) return names[0] + " and " + names[1];
            var sb = new StringBuilder();
            for (int i = 0; i < names.Count; i++)
            {
                if (i > 0) sb.Append(i == names.Count - 1 ? " and " : ", ");
                sb.Append(names[i]);
            }
            return sb.ToString();
        }

        private static string Days(double days)
        {
            if (days < 1) return Math.Max(1, Math.Round(days * 24)) + " hours";
            return Math.Round(days) + " day" + (Math.Round(days) == 1 ? string.Empty : "s");
        }

        private static string Achievements(MissionReport r)
        {
            var list = new List<string>();
            foreach (string a in r.Achievements)
            {
                if (a.StartsWith("Recover", StringComparison.OrdinalIgnoreCase) || a.StartsWith("Flight:", StringComparison.OrdinalIgnoreCase)) continue;
                int colon = a.IndexOf(':');
                list.Add(colon > 0 ? a.Substring(0, colon).ToLowerInvariant() + " of " + a.Substring(colon + 1) : a);
            }
            return list.Count == 0 ? string.Empty : "Milestones logged: " + Join(list) + ".";
        }

        private static string Pick(Random rng, params string[] options)
        {
            if (options.Length == 0) return string.Empty;
            int i = rng != null ? rng.Next(options.Length) : 0;
            return options[i];
        }
    }
}

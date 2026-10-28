using System;
using System.Collections.Generic;
using KSA.Core.Missions;
using KSA.Core.Personnel;
using KSA.Core.Projects;
using NUnit.Framework;

namespace KSA.Core.Tests
{
    [TestFixture]
    public class MissionTests
    {
        [Test]
        public void ShippedMissionConfigs_ParseCleanly_AndEveryMissionHasAStockTemplate()
        {
            var warnings = new List<string>();
            MissionCatalog missions = ShippedConfigs.Missions(warnings);
            TemplateCatalog templates = ShippedConfigs.Templates(warnings);
            ProgramCatalog programs = ShippedConfigs.ProgramTypes(warnings);

            Assert.That(warnings, Is.Empty, string.Join(" | ", warnings));
            Assert.That(missions.Count, Is.EqualTo(46));
            Assert.That(templates.Count, Is.EqualTo(14));
            Assert.That(programs.Count, Is.EqualTo(6));

            foreach (MissionType t in missions.Types)
            {
                Template best = templates.Best(t.Id, new string[0]);
                Assert.That(best, Is.Not.Null, "no stock-only template for " + t.Id);
                Assert.That(best.StockOnly, Is.True, t.Id);
                Assert.That(t.Achievements.Count, Is.GreaterThan(0), t.Id);
            }
            foreach (ProgramType p in programs.Types)
                foreach (ModuleRecipe m in p.Modules)
                    Assert.That(missions.Get(m.MissionTypeId), Is.Not.Null, p.Id + " module " + m.Role);
        }

        [Test]
        public void Templates_PreferModdedWhenInstalled_StockOtherwise()
        {
            TemplateCatalog templates = ShippedConfigs.Templates();
            Assert.That(templates.Best("StationModuleKerbin", new string[0]).Id, Is.EqualTo("StockStationModule"));
            Assert.That(templates.Best("StationModuleKerbin", new[] { "StationPartsExpansionRedux" }).Id, Is.EqualTo("SspxStationModule"));
            Assert.That(templates.Best("MunBaseModule", new[] { "PlanetaryBaseInc" }).Id, Is.EqualTo("KpbsBaseModule"));
        }

        [Test]
        public void Simulator_GoodCrewAndDesign_MostlySucceed_BadMostlyFail()
        {
            MissionCatalog missions = ShippedConfigs.Missions();
            TemplateCatalog templates = ShippedConfigs.Templates();
            MissionType orbit = missions.Get("KerbinOrbit");

            var good = new SimulationInput { Type = orbit, Template = templates.Get("StockKerbalX"), LeadEngineerRung = 2 };
            good.Crew.Add(new CrewProfile("Ace", "Ace") { Role = CrewRole.Pilot, Stars = 5, Morale = 90, Courage = 0.7f, Stupidity = 0.2f });
            var bad = new SimulationInput { Type = missions.Get("DunaLanding"), Template = new Template { DesignScore = 0.2 } };
            bad.Crew.Add(new CrewProfile("Rookie", "Rookie") { Role = CrewRole.Pilot, Stars = 0, Morale = 20, Courage = 0.2f, Stupidity = 0.9f });

            MissionSimulator.Odds g = MissionSimulator.Compute(good, new Random(1));
            MissionSimulator.Odds b = MissionSimulator.Compute(bad, new Random(1));
            Assert.That(g.Success, Is.GreaterThan(0.8), "good odds " + g.Success);
            Assert.That(b.Success, Is.LessThan(0.3), "bad odds " + b.Success);
            Assert.That(g.Success + g.Partial + g.Abort + g.LossOfVehicle + g.LossOfCrew, Is.EqualTo(1).Within(1e-9));

            int successes = 0, deaths = 0;
            var rng = new Random(42);
            for (int i = 0; i < 400; i++)
            {
                MissionReport r = MissionSimulator.Resolve(null, good, 1, rng);
                if (r.Outcome == MissionOutcome.Success) successes++;
                if (r.Outcome == MissionOutcome.LossOfCrew) deaths++;
            }
            Assert.That(successes, Is.GreaterThan(300));
            Assert.That(deaths, Is.LessThan(20));
        }

        [Test]
        public void Simulator_EveryOutcome_IsReachable_AndReportsAreConsistent()
        {
            MissionCatalog missions = ShippedConfigs.Missions();
            var input = new SimulationInput { Type = missions.Get("MunLanding"), Template = new Template { DesignScore = 0.45 } };
            input.Crew.Add(new CrewProfile("A", "A") { Role = CrewRole.Pilot, Stars = 1, Morale = 50, Courage = 0.5f, Stupidity = 0.5f });
            input.Crew.Add(new CrewProfile("B", "B") { Role = CrewRole.Engineer, Stars = 0, Morale = 50, Courage = 0.5f, Stupidity = 0.5f });

            var seen = new HashSet<MissionOutcome>();
            var rng = new Random(7);
            for (int i = 0; i < 2000 && seen.Count < 5; i++)
            {
                MissionReport r = MissionSimulator.Resolve(null, input, 5, rng);
                seen.Add(r.Outcome);
                Assert.That(r.Lines.Count, Is.InRange(2, 3));
                Assert.That(r.Crew, Is.EqualTo(new[] { "A", "B" }));
                switch (r.Outcome)
                {
                    case MissionOutcome.Success:
                        Assert.That(r.Achievements, Does.Contain("Land:Mun"));
                        Assert.That(r.DaysInSpace, Is.EqualTo(10));
                        break;
                    case MissionOutcome.PartialSuccess:
                        Assert.That(r.Achievements, Does.Not.Contain("EVA:Mun"), "the primary objective is what a partial success misses");
                        Assert.That(r.Achievements, Does.Contain("Recover:-"), "but the crew came home");
                        break;
                    case MissionOutcome.LossOfCrew:
                        Assert.That(r.Deaths, Is.EqualTo(new[] { "A", "B" }));
                        Assert.That(r.Incidents, Does.Contain(IncidentKind.Crash));
                        break;
                    case MissionOutcome.LossOfVehicle:
                        Assert.That(r.Incidents, Does.Contain(IncidentKind.Crash));
                        Assert.That(r.Deaths, Is.Empty);
                        break;
                }
            }
            Assert.That(seen.Count, Is.EqualTo(5), "outcomes seen: " + string.Join(",", seen));
        }

        [Test]
        public void Simulator_LandingSlope_HurtsRookiesMoreThanAces()
        {
            MissionType land = ShippedConfigs.Missions().Get("MunLanding");
            var rookie = new SimulationInput { Type = land, Template = new Template { DesignScore = 0.6 }, SiteSlope = 0.5 };
            rookie.Crew.Add(new CrewProfile("R", "R") { Role = CrewRole.Pilot, Stars = 0, Morale = 70 });
            var ace = new SimulationInput { Type = land, Template = new Template { DesignScore = 0.6 }, SiteSlope = 0.5 };
            ace.Crew.Add(new CrewProfile("A", "A") { Role = CrewRole.Pilot, Stars = 5, Morale = 70 });
            var flat = new SimulationInput { Type = land, Template = new Template { DesignScore = 0.6 }, SiteSlope = 0.0 };
            flat.Crew.Add(new CrewProfile("R", "R") { Role = CrewRole.Pilot, Stars = 0, Morale = 70 });

            Assert.That(MissionSimulator.Compute(rookie, null).SlopeFactor, Is.LessThan(MissionSimulator.Compute(ace, null).SlopeFactor));
            Assert.That(MissionSimulator.Compute(flat, null).SlopeFactor, Is.EqualTo(1.0));
        }
    }
}

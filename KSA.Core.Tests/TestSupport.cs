using System.Collections.Generic;
using System.IO;
using KSA.Core;
using KSA.Core.Careers;
using KSA.Core.Data;
using KSA.Core.Medals;
using KSA.Core.Personnel;

namespace KSA.Core.Tests
{
    /// <summary>A clock the tests can move by hand. Stock Kerbin calendar: 6-hour days, 426-day years.</summary>
    public sealed class FakeClock : IClock
    {
        public double UniversalTime { get; set; }
        public double SecondsPerDay { get; set; } = 21600;
        public int DaysPerYear { get; set; } = 426;

        public void AdvanceDays(double days) { UniversalTime += days * SecondsPerDay; }
    }

    public sealed class ListLogger : ILogger
    {
        public List<string> Lines { get; } = new List<string>();
        public void Info(string message) { Lines.Add("I " + message); }
        public void Warn(string message) { Lines.Add("W " + message); }
        public void Error(string message) { Lines.Add("E " + message); }
    }

    public sealed class MemoryPersistence : IPersistence
    {
        public DataNode Stored { get; set; }
        public DataNode LoadState() { return Stored; }
        public void SaveState(DataNode state) { Stored = state; }
    }

    /// <summary>Loads the real config files shipped in GameData so tests exercise what players get.</summary>
    public static class ShippedConfigs
    {
        public static string ConfigDir
        {
            get
            {
                // Walk up from the test binary to the repo root (the folder that holds KSA.sln).
                string dir = Path.GetDirectoryName(typeof(ShippedConfigs).Assembly.Location);
                for (int i = 0; i < 8 && dir != null; i++)
                {
                    if (File.Exists(Path.Combine(dir, "KSA.sln"))) return Path.Combine(dir, "GameData", "KSA", "Configs");
                    dir = Path.GetDirectoryName(dir);
                }
                throw new FileNotFoundException("Could not locate KSA.sln above " + typeof(ShippedConfigs).Assembly.Location);
            }
        }

        public static List<DataNode> Load(string fileName)
        {
            return DataNodeText.ParseAll(File.ReadAllText(Path.Combine(ConfigDir, fileName)));
        }

        public static CareerCatalog Careers(List<string> warnings = null) { return CareerCatalog.Parse(Load("careers.cfg"), warnings); }
        public static MedalCatalog Medals(List<string> warnings = null) { return MedalCatalog.Parse(Load("medals.cfg"), warnings); }
    }

    public static class Make
    {
        public static CrewSnapshot Snapshot(string name, CrewRole role, int stars = 0, CrewStatus status = CrewStatus.Available, params string[] achievements)
        {
            return new CrewSnapshot(name, role, stars, status, 0.5f, 0.5f, achievements);
        }
    }
}

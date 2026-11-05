using System;
using KSA.Core.Flight;
using NUnit.Framework;

namespace KSA.Core.Tests
{
    /// <summary>0.8.224: the hold on the pad for a station's pass.</summary>
    [TestFixture]
    public class LaunchWindowTests
    {
        private const double StationRate = 2 * Math.PI / 1900, PadRate = 2 * Math.PI / 21600, Downrange = 0.7, Ascent = 380, Lead = 0.05;

        [TestCase(0.0)]
        [TestCase(2.0)]
        [TestCase(5.5)]
        [TestCase(-1.0)]
        public void TheStationIsBesideTheShipAtInsertion(double aheadNow)
        {
            double wait = LaunchWindow.WaitSeconds(aheadNow, StationRate, PadRate, Downrange, Ascent, Lead);
            Assert.That(wait, Is.GreaterThanOrEqualTo(0));
            Assert.That(wait, Is.LessThan(2 * Math.PI / (StationRate - PadRate) + 1), "within one lap of the station round the pad");
            double ship = PadRate * wait + Downrange;                   // the pad's turn until liftoff, then the ascent
            double station = aheadNow + StationRate * (wait + Ascent);   // the station's turn until insertion
            double off = LaunchWindow.Wrap(station - ship - Lead);
            Assert.That(Math.Min(off, 2 * Math.PI - off), Is.LessThan(1e-9), "the station is the lead ahead of the ship at insertion");
        }

        [Test]
        public void AStationTurningNoFasterThanTheWorldNeverComesRound()
        {
            Assert.That(double.IsNaN(LaunchWindow.WaitSeconds(1, PadRate, PadRate, Downrange, Ascent, 0)));
        }
    }
}

using System;
using KSA.Core.Flight.Orbital;
using NUnit.Framework;

namespace KSA.Core.Tests
{
    public class OrbitalTests
    {
        // Stock KSP constants.
        private const double KerbinMu = 3.5316e12;
        private const double KerbinRadius = 600000;
        private const double MunOrbitRadius = 12000000;
        private const double MunPeriod = 138984;
        private const double MunMu = 6.5138e10;
        private const double ParkingRadius = KerbinRadius + 85000;

        private static readonly Vec3 Up = new Vec3(0, 0, 1);

        // ---------- Kepler ----------

        [Test]
        public void Propagate_QuarterPeriod_Rotates90Degrees()
        {
            StateVector s0 = Kepler.Circular(KerbinMu, ParkingRadius, 0, Up);
            double period = Kepler.Period(KerbinMu, ParkingRadius);
            StateVector s1 = Kepler.Propagate(s0, KerbinMu, period / 4);

            Assert.That(s1.Position.Length, Is.EqualTo(ParkingRadius).Within(1));
            Assert.That(Vec3.AngleBetween(s0.Position, s1.Position), Is.EqualTo(Math.PI / 2).Within(1e-6));
            // Counter-clockwise about +Z: quarter turn from +X lands on +Y.
            Assert.That(s1.Position.Y, Is.GreaterThan(0));
            Assert.That(s1.Velocity.Length, Is.EqualTo(s0.Velocity.Length).Within(1e-3));
        }

        [Test]
        public void Propagate_FullPeriod_ReturnsToStart()
        {
            StateVector s0 = Kepler.Circular(KerbinMu, ParkingRadius, 0.7, Up);
            double period = Kepler.Period(KerbinMu, ParkingRadius);
            StateVector s1 = Kepler.Propagate(s0, KerbinMu, period);

            Assert.That((s1.Position - s0.Position).Length, Is.LessThan(1));
            Assert.That((s1.Velocity - s0.Velocity).Length, Is.LessThan(0.01));
        }

        [Test]
        public void Propagate_Hyperbolic_ConservesEnergy()
        {
            double escape = Math.Sqrt(2 * KerbinMu / ParkingRadius);
            StateVector s0 = new StateVector(new Vec3(ParkingRadius, 0, 0), new Vec3(300, escape * 1.1, 0));
            double e0 = Kepler.SpecificEnergy(s0, KerbinMu);
            Assert.That(e0, Is.GreaterThan(0), "hyperbolic test state must have positive energy");
            Assert.That(Kepler.Apoapsis(s0, KerbinMu), Is.EqualTo(double.PositiveInfinity));

            StateVector s1 = Kepler.Propagate(s0, KerbinMu, 3600);
            double e1 = Kepler.SpecificEnergy(s1, KerbinMu);
            Assert.That(e1, Is.EqualTo(e0).Within(Math.Abs(e0) * 1e-9));
            Assert.That(s1.Position.Length, Is.GreaterThan(s0.Position.Length));
            Assert.That(Kepler.Periapsis(s1, KerbinMu), Is.EqualTo(Kepler.Periapsis(s0, KerbinMu)).Within(1));

            // Backwards in time returns to the start.
            StateVector back = Kepler.Propagate(s1, KerbinMu, -3600);
            Assert.That((back.Position - s0.Position).Length, Is.LessThan(1));
        }

        [Test]
        public void Circular_HasCircularSpeedAndPlane()
        {
            Vec3 normal = new Vec3(0.3, -0.2, 0.9);
            StateVector s = Kepler.Circular(KerbinMu, ParkingRadius, 1.2, normal);
            Assert.That(s.Position.Length, Is.EqualTo(ParkingRadius).Within(1e-6));
            Assert.That(s.Velocity.Length, Is.EqualTo(Kepler.CircularSpeed(KerbinMu, ParkingRadius)).Within(1e-9));
            Assert.That(Vec3.Dot(s.Position, normal), Is.EqualTo(0).Within(1e-6));
            Assert.That(Vec3.Dot(s.Velocity, normal), Is.EqualTo(0).Within(1e-6));
            Assert.That(Vec3.Dot(Vec3.Cross(s.Position, s.Velocity), normal), Is.GreaterThan(0), "motion is counter-clockwise about the normal");
            Assert.That(Kepler.Periapsis(s, KerbinMu), Is.EqualTo(ParkingRadius).Within(1));
            Assert.That(Kepler.Apoapsis(s, KerbinMu), Is.EqualTo(ParkingRadius).Within(1));

            StateVector z0 = Kepler.Circular(KerbinMu, ParkingRadius, 0, Up);
            Assert.That(z0.Position.X, Is.EqualTo(ParkingRadius).Within(1e-6));
            Assert.That(z0.Position.Y, Is.EqualTo(0).Within(1e-6));
        }

        // ---------- Lambert ----------

        [Test]
        public void Lambert_NearHalfRevolution_MatchesHohmann()
        {
            double transferTime;
            double hohmannDv = Ejection.Hohmann(KerbinMu, ParkingRadius, MunOrbitRadius, out transferTime);
            double expectedSpeed = Kepler.CircularSpeed(KerbinMu, ParkingRadius) + hohmannDv;

            // Exactly 180 degrees leaves the plane undetermined, so use a hair under it.
            double theta = Math.PI - 0.1 * Math.PI / 180;
            Vec3 r1 = new Vec3(ParkingRadius, 0, 0);
            Vec3 r2 = new Vec3(MunOrbitRadius * Math.Cos(theta), MunOrbitRadius * Math.Sin(theta), 0);
            Vec3 v1, v2;
            Assert.That(Lambert.Solve(r1, r2, transferTime, KerbinMu, Up, out v1, out v2), Is.True);
            Assert.That(v1.Length, Is.EqualTo(expectedSpeed).Within(expectedSpeed * 0.001));
        }

        [TestCase(60, 3.0 * 3600)]
        [TestCase(150, 6.0 * 3600)]
        public void Lambert_RoundTrip_LandsOnTarget(double degrees, double tof)
        {
            double theta = degrees * Math.PI / 180;
            Vec3 r1 = new Vec3(ParkingRadius, 0, 0);
            Vec3 r2 = new Vec3(MunOrbitRadius * Math.Cos(theta), MunOrbitRadius * Math.Sin(theta), 0);
            Vec3 v1, v2;
            Assert.That(Lambert.Solve(r1, r2, tof, KerbinMu, Up, out v1, out v2), Is.True);

            StateVector end = Kepler.Propagate(new StateVector(r1, v1), KerbinMu, tof);
            Assert.That((end.Position - r2).Length, Is.LessThan(1000));
            Assert.That((end.Velocity - v2).Length, Is.LessThan(1));
        }

        [Test]
        public void Lambert_RespectsPlaneNormalDirection()
        {
            // Same two points, opposite normal: the transfer goes the long way round (300 degrees) and needs more time.
            double theta = 60 * Math.PI / 180;
            Vec3 r1 = new Vec3(ParkingRadius, 0, 0);
            Vec3 r2 = new Vec3(MunOrbitRadius * Math.Cos(theta), MunOrbitRadius * Math.Sin(theta), 0);
            double tof = 10 * 3600;
            Vec3 v1, v2;
            Assert.That(Lambert.Solve(r1, r2, tof, KerbinMu, -Up, out v1, out v2), Is.True);
            Assert.That(Vec3.Dot(Vec3.Cross(r1, v1), Up), Is.LessThan(0), "motion is clockwise about +Z");
            StateVector end = Kepler.Propagate(new StateVector(r1, v1), KerbinMu, tof);
            Assert.That((end.Position - r2).Length, Is.LessThan(1000));
        }

        [Test]
        public void Lambert_RejectsDegenerateInput()
        {
            Vec3 v1, v2;
            Vec3 r1 = new Vec3(ParkingRadius, 0, 0);
            Assert.That(Lambert.Solve(r1, r1 * 2, 3600, KerbinMu, Up, out v1, out v2), Is.False, "zero transfer angle");
            Assert.That(Lambert.Solve(r1, -r1 * 2, 3600, KerbinMu, Up, out v1, out v2), Is.False, "180 degree transfer");
            Assert.That(Lambert.Solve(r1, new Vec3(0, MunOrbitRadius, 0), 0, KerbinMu, Up, out v1, out v2), Is.False, "zero time");
        }

        // ---------- TransferPlanner ----------

        [Test]
        public void TransferPlanner_FindsMunTransfer()
        {
            double parkingPeriod = Kepler.Period(KerbinMu, ParkingRadius);
            Func<double, StateVector> origin = ut => Kepler.Circular(KerbinMu, ParkingRadius, 2 * Math.PI * ut / parkingPeriod, Up);
            Func<double, StateVector> target = ut => Kepler.Circular(KerbinMu, MunOrbitRadius, 1.0 + 2 * Math.PI * ut / MunPeriod, Up);

            TransferPlan plan = TransferPlanner.Plan(origin, target, KerbinMu,
                0, MunPeriod, 3 * 3600, 20 * 3600, 240, 40, 0.5);

            Assert.That(plan, Is.Not.Null);
            TestContext.Out.WriteLine("Mun plan: depart {0:0} s, tof {1:0.00} h, dv {2:0.0} m/s, arrival {3:0.0} m/s",
                plan.DepartureUt, plan.TimeOfFlight / 3600, plan.DeltaVMagnitude, plan.ArrivalSpeed);
            Assert.That(plan.DeltaVMagnitude, Is.InRange(800, 1000));
            Assert.That(plan.ArrivalSpeed, Is.LessThan(1000));
            Assert.That(plan.TimeOfFlight, Is.InRange(3 * 3600, 20 * 3600));
            Assert.That(plan.DepartureUt, Is.InRange(0, MunPeriod));
            Assert.That(plan.DeltaV.Length, Is.EqualTo(plan.DeltaVMagnitude).Within(1e-9));
            Assert.That((plan.DepartureVelocity - origin(plan.DepartureUt).Velocity - plan.DeltaV).Length, Is.LessThan(1e-6));

            // The plan really arrives at the Mun's position.
            StateVector end = Kepler.Propagate(new StateVector(origin(plan.DepartureUt).Position, plan.DepartureVelocity), KerbinMu, plan.TimeOfFlight);
            Assert.That((end.Position - target(plan.ArrivalUt).Position).Length, Is.LessThan(1000));
        }

        [Test]
        public void TransferPlanner_ReturnsNullWhenNothingConverges()
        {
            // Origin and target always on the same ray: every Lambert call is degenerate.
            Func<double, StateVector> origin = ut => new StateVector(new Vec3(ParkingRadius, 0, 0), new Vec3(0, 2000, 0));
            Func<double, StateVector> target = ut => new StateVector(new Vec3(MunOrbitRadius, 0, 0), new Vec3(0, 500, 0));
            Assert.That(TransferPlanner.Plan(origin, target, KerbinMu, 0, 1000, 100, 200, 3, 3, 1), Is.Null);
        }

        // ---------- Ejection ----------

        [Test]
        public void Ejection_DeltaV_ForDunaIsAboutAThousand()
        {
            double dv = Ejection.DeltaV(KerbinMu, ParkingRadius, 900);
            TestContext.Out.WriteLine("Ejection dv for vInf 900 from 85 km: {0:0.0} m/s", dv);
            Assert.That(dv, Is.InRange(1040, 1100));
            Assert.That(Ejection.DeltaV(KerbinMu, ParkingRadius, 0),
                Is.EqualTo((Math.Sqrt(2) - 1) * Kepler.CircularSpeed(KerbinMu, ParkingRadius)).Within(1e-6));
        }

        [Test]
        public void Ejection_BurnPointDirection_GeometryHolds()
        {
            double vInf = 900;
            Vec3 normal = new Vec3(0, 0, 1);
            Vec3 asymptote = new Vec3(0.6, 0.8, 0.3); // deliberately out of plane: Z gets projected away
            Vec3 p = Ejection.BurnPointDirection(asymptote, normal, KerbinMu, ParkingRadius, vInf);

            double e = Ejection.Eccentricity(KerbinMu, ParkingRadius, vInf);
            double nuInf = Math.Acos(-1 / e);
            Vec3 d = Vec3.Exclude(normal, asymptote).Normalized;

            Assert.That(p.Length, Is.EqualTo(1).Within(1e-9));
            Assert.That(Vec3.Dot(p, normal), Is.EqualTo(0).Within(1e-9), "burn point lies in the orbital plane");
            Assert.That(Vec3.Dot(p, d), Is.EqualTo(Math.Cos(nuInf)).Within(1e-9), "angle to asymptote is nu_inf");
            // Moving counter-clockwise from p (velocity direction n x p) should sweep towards the asymptote.
            Assert.That(Vec3.Dot(Vec3.Cross(normal, p), d), Is.GreaterThan(0), "asymptote is ahead along the direction of motion");
            Assert.That(e, Is.GreaterThan(1));
        }

        [Test]
        public void Ejection_Hohmann_ToMun()
        {
            double transferTime;
            double dv = Ejection.Hohmann(KerbinMu, ParkingRadius, MunOrbitRadius, out transferTime);
            TestContext.Out.WriteLine("Hohmann 685 km -> 12,000 km: dv {0:0.0} m/s, time {1:0} s", dv, transferTime);
            Assert.That(dv, Is.EqualTo(860).Within(20));
            double a = (ParkingRadius + MunOrbitRadius) / 2;
            Assert.That(transferTime, Is.EqualTo(Kepler.Period(KerbinMu, a) / 2).Within(1e-6));
        }

        [Test]
        public void Ejection_PhaseAngle_ToMun()
        {
            // pi - omega_mun * transferTime. From a 85 km parking orbit the Hohmann leg takes ~26,700 s, during which the
            // Mun moves ~69.2 degrees, so the required lead is ~110.8 degrees.
            double phase = Ejection.PhaseAngle(KerbinMu, ParkingRadius, MunOrbitRadius) * 180 / Math.PI;
            TestContext.Out.WriteLine("Mun phase angle from 85 km: {0:0.00} degrees", phase);
            Assert.That(phase, Is.InRange(109, 113));
        }

        // ---------- BurnMath ----------

        [Test]
        public void BurnMath_BurnTime_IsExactTsiolkovsky()
        {
            double thrust = 215000, isp = 320, mass = 10000, dv = 1000;
            double ve = isp * 9.80665;
            double expected = mass * (1 - Math.Exp(-dv / ve)) * ve / thrust;
            Assert.That(BurnMath.BurnTime(dv, thrust, isp, mass), Is.EqualTo(expected).Within(1e-9));
            Assert.That(BurnMath.BurnTime(dv, 0, isp, mass), Is.EqualTo(double.PositiveInfinity));
            Assert.That(BurnMath.BurnTime(0, thrust, isp, mass), Is.EqualTo(0));
        }

        [Test]
        public void BurnMath_DeltaVAvailable()
        {
            Assert.That(BurnMath.DeltaVAvailable(10000, 5000, 320), Is.EqualTo(320 * BurnMath.G0 * Math.Log(2)).Within(1e-9));
            Assert.That(BurnMath.DeltaVAvailable(5000, 5000, 320), Is.EqualTo(0));
        }

        // ---------- DescentMath ----------

        [Test]
        public void DescentMath_StoppingDistance()
        {
            Assert.That(DescentMath.StoppingDistance(200, 10, 1.63), Is.EqualTo(2389).Within(1));
            Assert.That(DescentMath.StoppingDistance(200, 1.63, 1.63), Is.EqualTo(double.PositiveInfinity));
            Assert.That(DescentMath.SafeBurnAltitude(200, 10, 1.63, 0.1), Is.EqualTo(2389.486 * 1.1 + 50).Within(1));
        }

        [Test]
        public void DescentMath_TargetDescentSpeed_MonotonicWithFloor()
        {
            Assert.That(DescentMath.TargetDescentSpeed(0), Is.EqualTo(2));
            Assert.That(DescentMath.TargetDescentSpeed(20), Is.EqualTo(2));
            Assert.That(DescentMath.TargetDescentSpeed(100), Is.EqualTo(10));
            Assert.That(DescentMath.TargetDescentSpeed(2000), Is.EqualTo(60));
            Assert.That(DescentMath.TargetDescentSpeed(50000), Is.EqualTo(60));
            double prev = DescentMath.TargetDescentSpeed(0);
            for (double alt = 1; alt <= 5000; alt += 1)
            {
                double v = DescentMath.TargetDescentSpeed(alt);
                Assert.That(v, Is.GreaterThanOrEqualTo(prev), "not monotonic at " + alt);
                Assert.That(v - prev, Is.LessThanOrEqualTo(0.1 + 1e-9), "jump at " + alt);
                prev = v;
            }
        }

        [Test]
        public void DescentMath_SlopeFromNormal_AndFlattest()
        {
            double tilt = 10 * Math.PI / 180;
            Vec3 normal = new Vec3(Math.Sin(tilt), 0, Math.Cos(tilt));
            Assert.That(DescentMath.SlopeFromNormal(normal, Up), Is.EqualTo(Math.Tan(tilt)).Within(1e-9));
            Assert.That(DescentMath.SlopeFromNormal(Up, Up), Is.EqualTo(0).Within(1e-9));

            Assert.That(DescentMath.Flattest(new[] { 0.3, 0.05, 0.1 }), Is.EqualTo(1));
            Assert.That(DescentMath.Flattest(new double[0]), Is.EqualTo(-1));
            Assert.That(DescentMath.Flattest(null), Is.EqualTo(-1));
        }

        // ---------- Vec3 ----------

        [Test]
        public void Vec3_Basics()
        {
            Vec3 a = new Vec3(1, 2, 3);
            Vec3 b = new Vec3(-2, 0, 1);
            Assert.That(Vec3.Dot(a, b), Is.EqualTo(1));
            Assert.That(Vec3.Cross(new Vec3(1, 0, 0), new Vec3(0, 1, 0)).Z, Is.EqualTo(1));
            Assert.That((a * 2).Y, Is.EqualTo(4));
            Assert.That((2 * a).Y, Is.EqualTo(4));
            Assert.That((a / 2).Z, Is.EqualTo(1.5));
            Assert.That((-a).X, Is.EqualTo(-1));
            Assert.That((a - b).X, Is.EqualTo(3));
            Assert.That(a.LengthSquared, Is.EqualTo(14));
            Assert.That(a.Normalized.Length, Is.EqualTo(1).Within(1e-12));
            Assert.That(Vec3.Zero.Normalized.Length, Is.EqualTo(0));
            Vec3 ex = Vec3.Exclude(new Vec3(0, 0, 2), a);
            Assert.That(ex.Z, Is.EqualTo(0));
            Assert.That(ex.X, Is.EqualTo(1));
            Assert.That(Vec3.AngleBetween(new Vec3(1, 0, 0), new Vec3(0, 1, 0)), Is.EqualTo(Math.PI / 2).Within(1e-12));
            Assert.That(new Vec3(1, 2.5, -3).ToString(), Is.EqualTo("(1.000, 2.500, -3.000)"));
        }
    }
}

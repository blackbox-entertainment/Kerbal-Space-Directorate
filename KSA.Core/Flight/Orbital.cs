using System;
using System.Globalization;

namespace KSA.Core.Flight.Orbital
{
    /// <summary>Minimal double-precision 3-vector for orbital math. Portable: no Unity types.</summary>
    public struct Vec3
    {
        public double X, Y, Z;

        public Vec3(double x, double y, double z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        public static readonly Vec3 Zero = new Vec3(0, 0, 0);

        public static Vec3 operator +(Vec3 a, Vec3 b) { return new Vec3(a.X + b.X, a.Y + b.Y, a.Z + b.Z); }
        public static Vec3 operator -(Vec3 a, Vec3 b) { return new Vec3(a.X - b.X, a.Y - b.Y, a.Z - b.Z); }
        public static Vec3 operator -(Vec3 a) { return new Vec3(-a.X, -a.Y, -a.Z); }
        public static Vec3 operator *(Vec3 a, double s) { return new Vec3(a.X * s, a.Y * s, a.Z * s); }
        public static Vec3 operator *(double s, Vec3 a) { return new Vec3(a.X * s, a.Y * s, a.Z * s); }
        public static Vec3 operator /(Vec3 a, double s) { return new Vec3(a.X / s, a.Y / s, a.Z / s); }

        public static double Dot(Vec3 a, Vec3 b) { return a.X * b.X + a.Y * b.Y + a.Z * b.Z; }

        public static Vec3 Cross(Vec3 a, Vec3 b)
        {
            return new Vec3(
                a.Y * b.Z - a.Z * b.Y,
                a.Z * b.X - a.X * b.Z,
                a.X * b.Y - a.Y * b.X);
        }

        public double Length { get { return Math.Sqrt(X * X + Y * Y + Z * Z); } }
        public double LengthSquared { get { return X * X + Y * Y + Z * Z; } }

        public Vec3 Normalized
        {
            get
            {
                double len = Length;
                return len > 0 ? this / len : Zero;
            }
        }

        /// <summary>v minus its component along direction (direction need not be unit length).</summary>
        public static Vec3 Exclude(Vec3 direction, Vec3 v)
        {
            double d2 = direction.LengthSquared;
            if (d2 <= 0) return v;
            return v - direction * (Dot(direction, v) / d2);
        }

        /// <summary>Angle between two vectors in radians, in [0, pi].</summary>
        public static double AngleBetween(Vec3 a, Vec3 b)
        {
            double denom = a.Length * b.Length;
            if (denom <= 0) return 0;
            double c = Dot(a, b) / denom;
            if (c > 1) c = 1;
            if (c < -1) c = -1;
            return Math.Acos(c);
        }

        public override string ToString()
        {
            return string.Format(CultureInfo.InvariantCulture, "({0:0.000}, {1:0.000}, {2:0.000})", X, Y, Z);
        }
    }

    /// <summary>Position and velocity relative to a central body (metres, metres per second).</summary>
    public struct StateVector
    {
        public Vec3 Position;
        public Vec3 Velocity;

        public StateVector(Vec3 position, Vec3 velocity)
        {
            Position = position;
            Velocity = velocity;
        }
    }

    /// <summary>Stumpff functions shared by the propagator and the Lambert solver.</summary>
    internal static class Stumpff
    {
        public static double C(double z)
        {
            if (z > 1e-6)
            {
                double s = Math.Sqrt(z);
                return (1 - Math.Cos(s)) / z;
            }
            if (z < -1e-6)
            {
                double s = Math.Sqrt(-z);
                return (Math.Cosh(s) - 1) / (-z);
            }
            // Series: 1/2 - z/24 + z^2/720
            return 0.5 - z / 24.0 + z * z / 720.0;
        }

        public static double S(double z)
        {
            if (z > 1e-6)
            {
                double s = Math.Sqrt(z);
                return (s - Math.Sin(s)) / (s * s * s);
            }
            if (z < -1e-6)
            {
                double s = Math.Sqrt(-z);
                return (Math.Sinh(s) - s) / (s * s * s);
            }
            // Series: 1/6 - z/120 + z^2/5040
            return 1.0 / 6.0 - z / 120.0 + z * z / 5040.0;
        }
    }

    /// <summary>Two-body Keplerian helpers.</summary>
    public static class Kepler
    {
        /// <summary>Universal-variable propagation (Lagrange f and g coefficients). Works for any conic.</summary>
        public static StateVector Propagate(StateVector s, double mu, double dt)
        {
            if (dt == 0) return s;
            Vec3 r0v = s.Position;
            Vec3 v0v = s.Velocity;
            double r0 = r0v.Length;
            double v0sq = v0v.LengthSquared;
            double sqrtMu = Math.Sqrt(mu);
            double vr0 = Vec3.Dot(r0v, v0v) / r0;
            double alpha = 2.0 / r0 - v0sq / mu; // 1 / semi-major axis

            double chi;
            if (Math.Abs(alpha * r0) > 1e-6)
                chi = sqrtMu * Math.Abs(alpha) * dt;
            else
                chi = sqrtMu * dt / r0;

            double z = 0, c, sf;
            for (int i = 0; i < 200; i++)
            {
                z = alpha * chi * chi;
                c = Stumpff.C(z);
                sf = Stumpff.S(z);
                double f = r0 * vr0 / sqrtMu * chi * chi * c
                           + (1 - alpha * r0) * chi * chi * chi * sf
                           + r0 * chi
                           - sqrtMu * dt;
                double df = r0 * vr0 / sqrtMu * chi * (1 - z * sf)
                            + (1 - alpha * r0) * chi * chi * c
                            + r0;
                double step = f / df;
                chi -= step;
                if (Math.Abs(step) <= 1e-12 * Math.Max(1.0, Math.Abs(chi))) break;
            }
            z = alpha * chi * chi;
            c = Stumpff.C(z);
            sf = Stumpff.S(z);

            double fl = 1 - chi * chi / r0 * c;
            double gl = dt - chi * chi * chi / sqrtMu * sf;
            Vec3 r = r0v * fl + v0v * gl;
            double rm = r.Length;
            double fdot = sqrtMu / (rm * r0) * (z * sf - 1) * chi;
            double gdot = 1 - chi * chi / rm * c;
            Vec3 v = r0v * fdot + v0v * gdot;
            return new StateVector(r, v);
        }

        /// <summary>
        /// Circular orbit state in the plane with the given normal, moving counter-clockwise about it.
        /// For planeNormal = (0,0,1) the position at true anomaly 0 is (radius, 0, 0).
        /// </summary>
        public static StateVector Circular(double mu, double radius, double trueAnomalyRad, Vec3 planeNormal)
        {
            Vec3 e1, e2;
            PlaneBasis(planeNormal, out e1, out e2);
            double cos = Math.Cos(trueAnomalyRad);
            double sin = Math.Sin(trueAnomalyRad);
            double speed = CircularSpeed(mu, radius);
            Vec3 pos = (e1 * cos + e2 * sin) * radius;
            Vec3 vel = (e1 * -sin + e2 * cos) * speed;
            return new StateVector(pos, vel);
        }

        /// <summary>Orthonormal in-plane basis (e1, e2) with e1 x e2 = normal. e1 is (1,0,0) projected into the plane when possible.</summary>
        internal static void PlaneBasis(Vec3 planeNormal, out Vec3 e1, out Vec3 e2)
        {
            Vec3 n = planeNormal.Normalized;
            if (n.LengthSquared == 0) n = new Vec3(0, 0, 1);
            Vec3 reference = new Vec3(1, 0, 0);
            if (Math.Abs(Vec3.Dot(n, reference)) > 0.9) reference = new Vec3(0, 1, 0);
            e1 = Vec3.Exclude(n, reference).Normalized;
            e2 = Vec3.Cross(n, e1);
        }

        public static double Period(double mu, double semiMajorAxis)
        {
            return 2 * Math.PI * Math.Sqrt(semiMajorAxis * semiMajorAxis * semiMajorAxis / mu);
        }

        public static double CircularSpeed(double mu, double radius)
        {
            return Math.Sqrt(mu / radius);
        }

        /// <summary>Distance from the body centre of the periapsis of the conic through s.</summary>
        public static double Periapsis(StateVector s, double mu)
        {
            double h2 = Vec3.Cross(s.Position, s.Velocity).LengthSquared;
            double e = Eccentricity(s, mu);
            return h2 / mu / (1 + e);
        }

        /// <summary>Apoapsis distance; +Infinity when the orbit is parabolic or hyperbolic.</summary>
        public static double Apoapsis(StateVector s, double mu)
        {
            double e = Eccentricity(s, mu);
            if (e >= 1) return double.PositiveInfinity;
            double h2 = Vec3.Cross(s.Position, s.Velocity).LengthSquared;
            return h2 / mu / (1 - e);
        }

        public static double SpecificEnergy(StateVector s, double mu)
        {
            return s.Velocity.LengthSquared / 2 - mu / s.Position.Length;
        }

        private static double Eccentricity(StateVector s, double mu)
        {
            double r = s.Position.Length;
            double v2 = s.Velocity.LengthSquared;
            Vec3 ev = (s.Position * (v2 - mu / r) - s.Velocity * Vec3.Dot(s.Position, s.Velocity)) / mu;
            return ev.Length;
        }
    }

    /// <summary>Lambert's problem: the orbit that connects two positions in a given time.</summary>
    public static class Lambert
    {
        /// <summary>
        /// Universal-variable Lambert solver (Bate/Mueller/White, bisection on z). The transfer goes
        /// counter-clockwise about planeNormal from r1 to r2, so the transfer angle is in (0, 2 pi) measured that way.
        /// Returns false if no solution converges (degenerate geometry, non-positive time of flight, or no root).
        /// </summary>
        public static bool Solve(Vec3 r1, Vec3 r2, double timeOfFlight, double mu, Vec3 planeNormal, out Vec3 v1, out Vec3 v2)
        {
            v1 = Vec3.Zero;
            v2 = Vec3.Zero;
            double r1m = r1.Length;
            double r2m = r2.Length;
            if (timeOfFlight <= 0 || r1m <= 0 || r2m <= 0 || mu <= 0) return false;

            double cosTheta = Vec3.Dot(r1, r2) / (r1m * r2m);
            if (cosTheta > 1) cosTheta = 1;
            if (cosTheta < -1) cosTheta = -1;
            double theta = Math.Acos(cosTheta);
            if (Vec3.Dot(Vec3.Cross(r1, r2), planeNormal) < 0) theta = 2 * Math.PI - theta;

            double sinTheta = Math.Sin(theta);
            double oneMinusCos = 1 - cosTheta;
            if (oneMinusCos <= 1e-12 || Math.Abs(sinTheta) < 1e-12) return false; // 0 or pi: plane undetermined
            double A = sinTheta * Math.Sqrt(r1m * r2m / oneMinusCos);
            if (A == 0) return false;

            double sqrtMu = Math.Sqrt(mu);
            double zHi = 4 * Math.PI * Math.PI - 1e-6;
            double zLo = -4 * Math.PI * Math.PI;

            // Make sure the lower bracket produces a flight time below the target.
            double fLo;
            bool okLo = Residual(zLo, r1m, r2m, A, sqrtMu, timeOfFlight, out fLo);
            int expand = 0;
            while (okLo && fLo > 0 && expand < 20)
            {
                zLo *= 2;
                okLo = Residual(zLo, r1m, r2m, A, sqrtMu, timeOfFlight, out fLo);
                expand++;
            }
            if (okLo && fLo > 0) return false;

            double z = 0;
            bool converged = false;
            for (int i = 0; i < 300; i++)
            {
                z = 0.5 * (zLo + zHi);
                double f;
                bool ok = Residual(z, r1m, r2m, A, sqrtMu, timeOfFlight, out f);
                if (!ok || f < 0) zLo = z; else zHi = z;
                if (ok && Math.Abs(f) <= 1e-10 * timeOfFlight)
                {
                    converged = true;
                    break;
                }
                if (zHi - zLo < 1e-14)
                {
                    converged = ok;
                    break;
                }
            }
            if (!converged)
            {
                double f;
                converged = Residual(z, r1m, r2m, A, sqrtMu, timeOfFlight, out f) && Math.Abs(f) <= 1e-6 * timeOfFlight;
                if (!converged) return false;
            }

            double y = r1m + r2m + A * (z * Stumpff.S(z) - 1) / Math.Sqrt(Stumpff.C(z));
            if (y <= 0) return false;
            double fl = 1 - y / r1m;
            double gl = A * Math.Sqrt(y / mu);
            double gdot = 1 - y / r2m;
            if (gl == 0 || double.IsNaN(gl)) return false;
            v1 = (r2 - r1 * fl) / gl;
            v2 = (r2 * gdot - r1) / gl;
            return !double.IsNaN(v1.X) && !double.IsNaN(v2.X);
        }

        /// <summary>Flight time at z minus the target. Returns false when y(z) is negative (z too small).</summary>
        private static bool Residual(double z, double r1, double r2, double A, double sqrtMu, double target, out double f)
        {
            f = 0;
            double c = Stumpff.C(z);
            double s = Stumpff.S(z);
            if (c <= 0) { f = double.PositiveInfinity; return true; }
            double y = r1 + r2 + A * (z * s - 1) / Math.Sqrt(c);
            if (y < 0) return false;
            double chi = Math.Sqrt(y / c);
            double t = (chi * chi * chi * s + A * Math.Sqrt(y)) / sqrtMu;
            f = t - target;
            return !double.IsNaN(f);
        }
    }

    /// <summary>A transfer between two bodies (or orbits) around the same central body.</summary>
    public sealed class TransferPlan
    {
        public double DepartureUt;
        public double ArrivalUt;
        /// <summary>Required velocity (central-body frame) at departure.</summary>
        public Vec3 DepartureVelocity;
        /// <summary>DepartureVelocity minus the origin's velocity at departure.</summary>
        public Vec3 DeltaV;
        public double DeltaVMagnitude;
        /// <summary>Transfer velocity at arrival minus the target's velocity (hyperbolic excess at the target).</summary>
        public Vec3 ArrivalRelativeVelocity;
        public double ArrivalSpeed;
        public double TimeOfFlight { get { return ArrivalUt - DepartureUt; } }
    }

    /// <summary>Porkchop-style search for a cheap transfer.</summary>
    public static class TransferPlanner
    {
        /// <summary>
        /// Grid search over departure UT in [earliestUt, latestUt] and time of flight in [minTof, maxTof]
        /// (departureSamples x tofSamples), scoring |DeltaV| + arrivalWeight * |ArrivalRelativeVelocity|, then
        /// a few rounds of local refinement (halving the step around the best cell). The Lambert plane normal is the
        /// origin's orbit normal at departure. origin and target return state relative to the same central body of
        /// gravitational parameter mu. Returns null when nothing converges.
        /// </summary>
        public static TransferPlan Plan(Func<double, StateVector> origin, Func<double, StateVector> target, double mu,
            double earliestUt, double latestUt, double minTof, double maxTof, int departureSamples, int tofSamples, double arrivalWeight)
        {
            if (origin == null || target == null) return null;
            if (departureSamples < 1) departureSamples = 1;
            if (tofSamples < 1) tofSamples = 1;
            if (latestUt < earliestUt) latestUt = earliestUt;
            if (minTof <= 0) minTof = 1;
            if (maxTof < minTof) maxTof = minTof;

            double depStep = departureSamples > 1 ? (latestUt - earliestUt) / (departureSamples - 1) : 0;
            double tofStep = tofSamples > 1 ? (maxTof - minTof) / (tofSamples - 1) : 0;

            TransferPlan best = null;
            double bestScore = double.PositiveInfinity;
            for (int i = 0; i < departureSamples; i++)
            {
                double dep = earliestUt + i * depStep;
                for (int j = 0; j < tofSamples; j++)
                {
                    double tof = minTof + j * tofStep;
                    Consider(origin, target, mu, dep, tof, arrivalWeight, ref best, ref bestScore);
                }
            }
            if (best == null) return null;

            double dStep = depStep > 0 ? depStep : Math.Max(1.0, latestUt - earliestUt);
            double tStep = tofStep > 0 ? tofStep : Math.Max(1.0, maxTof - minTof);
            for (int round = 0; round < 10; round++)
            {
                dStep *= 0.5;
                tStep *= 0.5;
                double cd = best.DepartureUt;
                double ct = best.TimeOfFlight;
                for (int di = -1; di <= 1; di++)
                {
                    for (int tj = -1; tj <= 1; tj++)
                    {
                        if (di == 0 && tj == 0) continue;
                        double dep = Clamp(cd + di * dStep, earliestUt, latestUt);
                        double tof = Clamp(ct + tj * tStep, minTof, maxTof);
                        Consider(origin, target, mu, dep, tof, arrivalWeight, ref best, ref bestScore);
                    }
                }
            }
            return best;
        }

        private static double Clamp(double v, double lo, double hi)
        {
            return v < lo ? lo : (v > hi ? hi : v);
        }

        private static void Consider(Func<double, StateVector> origin, Func<double, StateVector> target, double mu,
            double dep, double tof, double arrivalWeight, ref TransferPlan best, ref double bestScore)
        {
            StateVector o = origin(dep);
            StateVector t = target(dep + tof);
            Vec3 normal = Vec3.Cross(o.Position, o.Velocity);
            Vec3 v1, v2;
            if (!Lambert.Solve(o.Position, t.Position, tof, mu, normal, out v1, out v2)) return;
            Vec3 dv = v1 - o.Velocity;
            Vec3 arr = v2 - t.Velocity;
            double score = dv.Length + arrivalWeight * arr.Length;
            if (double.IsNaN(score) || score >= bestScore) return;
            bestScore = score;
            best = new TransferPlan
            {
                DepartureUt = dep,
                ArrivalUt = dep + tof,
                DepartureVelocity = v1,
                DeltaV = dv,
                DeltaVMagnitude = dv.Length,
                ArrivalRelativeVelocity = arr,
                ArrivalSpeed = arr.Length
            };
        }
    }

    /// <summary>Escape burns from a circular parking orbit, plus Hohmann basics.</summary>
    public static class Ejection
    {
        /// <summary>Prograde delta-v from a circular parking orbit to leave with hyperbolic excess vInf.</summary>
        public static double DeltaV(double mu, double parkingRadius, double vInf)
        {
            return Math.Sqrt(vInf * vInf + 2 * mu / parkingRadius) - Math.Sqrt(mu / parkingRadius);
        }

        /// <summary>Eccentricity of the escape hyperbola whose periapsis is the parking orbit.</summary>
        public static double Eccentricity(double mu, double parkingRadius, double vInf)
        {
            return 1 + parkingRadius * vInf * vInf / mu;
        }

        /// <summary>
        /// Unit direction (from the body centre) of the point on the circular parking orbit (counter-clockwise about
        /// planeNormal) where a prograde burn sends the outgoing asymptote along vInfDirection projected into the plane.
        /// The periapsis sits nu_inf = acos(-1/e) behind the asymptote direction along the direction of motion.
        /// </summary>
        public static Vec3 BurnPointDirection(Vec3 vInfDirection, Vec3 planeNormal, double mu, double parkingRadius, double vInf)
        {
            Vec3 n = planeNormal.Normalized;
            Vec3 d = Vec3.Exclude(n, vInfDirection).Normalized;
            if (d.LengthSquared == 0) return Vec3.Zero;
            double e = Eccentricity(mu, parkingRadius, vInf);
            double nuInf = Math.Acos(-1 / e);
            // Rotate d by -nuInf about n (Rodrigues; d is already perpendicular to n).
            return (d * Math.Cos(nuInf) - Vec3.Cross(n, d) * Math.Sin(nuInf)).Normalized;
        }

        /// <summary>Delta-v of the first burn of a Hohmann transfer from circular r1 to r2, and the half-period transfer time.</summary>
        public static double Hohmann(double mu, double r1, double r2, out double transferTime)
        {
            double a = (r1 + r2) / 2;
            transferTime = Math.PI * Math.Sqrt(a * a * a / mu);
            double vCirc = Math.Sqrt(mu / r1);
            double vTransfer = Math.Sqrt(mu * (2 / r1 - 1 / a));
            return Math.Abs(vTransfer - vCirc);
        }

        /// <summary>Angle (radians) the target must lead the vessel at departure for a Hohmann transfer: pi - omega2 * transferTime.</summary>
        public static double PhaseAngle(double mu, double r1, double r2)
        {
            double transferTime;
            Hohmann(mu, r1, r2, out transferTime);
            double omega2 = Math.Sqrt(mu / (r2 * r2 * r2));
            return Math.PI - omega2 * transferTime;
        }
    }

    /// <summary>Rocket-equation helpers.</summary>
    public static class BurnMath
    {
        public const double G0 = 9.80665;

        /// <summary>Exact burn duration (Tsiolkovsky) for a constant-thrust, constant-Isp stage. +Infinity when thrust is zero or negative.</summary>
        public static double BurnTime(double deltaV, double thrustNewtons, double ispSeconds, double massKg)
        {
            if (thrustNewtons <= 0 || ispSeconds <= 0 || massKg <= 0) return double.PositiveInfinity;
            if (deltaV <= 0) return 0;
            double ve = ispSeconds * G0;
            double massFlow = thrustNewtons / ve;
            double finalMass = massKg * Math.Exp(-deltaV / ve);
            return (massKg - finalMass) / massFlow;
        }

        public static double DeltaVAvailable(double wetMassKg, double dryMassKg, double ispSeconds)
        {
            if (wetMassKg <= 0 || dryMassKg <= 0 || wetMassKg <= dryMassKg) return 0;
            return ispSeconds * G0 * Math.Log(wetMassKg / dryMassKg);
        }
    }

    /// <summary>Powered-descent and landing-site helpers.</summary>
    public static class DescentMath
    {
        /// <summary>Distance needed to kill speed at net deceleration (thrustAccel - gravity). +Infinity when thrust cannot beat gravity.</summary>
        public static double StoppingDistance(double speed, double thrustAccel, double gravity)
        {
            double net = thrustAccel - gravity;
            if (net <= 0) return double.PositiveInfinity;
            return speed * speed / (2 * net);
        }

        /// <summary>Altitude above terrain at which the braking burn should start, with a fractional margin and a 50 m pad.</summary>
        public static double SafeBurnAltitude(double speed, double thrustAccel, double gravity, double margin)
        {
            double d = StoppingDistance(speed, thrustAccel, gravity);
            if (double.IsInfinity(d)) return d;
            return d * (1 + margin) + 50;
        }

        /// <summary>
        /// Final-approach descent speed schedule: 60 m/s at 600 m and above (so also at 2000 m and above), then
        /// altitude / 10 down to a 2 m/s floor at 20 m and below. Continuous and monotonic in altitude.
        /// </summary>
        public static double TargetDescentSpeed(double altitudeAboveTerrain)
        {
            if (altitudeAboveTerrain >= 2000) return 60;
            double v = altitudeAboveTerrain / 10;
            if (v > 60) v = 60;
            if (v < 2) v = 2;
            return v;
        }

        /// <summary>Tangent of the angle between the surface normal and local up (0 = flat).</summary>
        public static double SlopeFromNormal(Vec3 surfaceNormal, Vec3 up)
        {
            return Math.Tan(Vec3.AngleBetween(surfaceNormal, up));
        }

        /// <summary>Index of the smallest slope, or -1 for an empty or null array.</summary>
        public static int Flattest(double[] slopes)
        {
            if (slopes == null || slopes.Length == 0) return -1;
            int best = 0;
            for (int i = 1; i < slopes.Length; i++)
                if (slopes[i] < slopes[best]) best = i;
            return best;
        }
    }
}

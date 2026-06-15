// CoordinateValidator.cs
// Enforces physical constraints on OmegaZaTaCoordinates.
// No physically invalid state ever reaches the GPU or the navigation layer.
//
// Constraints enforced:
//   1. Observer can never reach z = Z_PHOTON_DECOUPLING (CMB surface recedes)
//   2. Ξ is clamped to [0, 1] and checked against XI_CRITICAL
//   3. Ω is clamped to [OMEGA_MIN, OMEGA_MAX]
//   4. Angular coordinates wrap correctly (θ periodic [0,π], φ periodic [0,2π])
//   5. Subjective time never exceeds proper time (tax is non-negative)
//   6. Ξ recovers toward XI_BASELINE at XI_RECOVERY_RATE when unstressed

using UnityEngine;

namespace InfiniteImprobability.Core
{
    public static class CoordinateValidator
    {
        // Approach limit — how close the observer can get to z=1090.
        // The CMB surface recedes asymptotically; this is the hard floor.
        private const float Z_APPROACH_LIMIT = CosmologicalConstants.Z_PHOTON_DECOUPLING * 0.98f;

        /// <summary>
        /// Validate and clamp a coordinate to all physical constraints.
        /// Call this every time a coordinate is updated before passing
        /// it to any renderer or navigator.
        /// </summary>
        public static OmegaZaTaCoordinate Validate(OmegaZaTaCoordinate c)
        {
            c = ClampRedshift(c);
            c = WrapAngles(c);
            c = ClampOmega(c);
            c = ClampXi(c);
            c = ClampTime(c);
            return c;
        }

        /// <summary>
        /// Evolve Ξ by one tick — drains under density stress, recovers toward
        /// baseline when unstressed. Call after Validate() each tick.
        /// </summary>
        public static OmegaZaTaCoordinate EvolveXi(
            OmegaZaTaCoordinate c, float localDensity)
        {
            float stress   = localDensity * CosmologicalConstants.XI_DRAIN_RATE;
            float recovery = CosmologicalConstants.XI_RECOVERY_RATE
                             * Mathf.Max(0f, CosmologicalConstants.XI_BASELINE - c.Xi);

            c.Xi = Mathf.Clamp01(c.Xi - stress + recovery);
            return c;
        }

        /// <summary>
        /// Returns true if the coordinate represents a classically coherent state.
        /// False means the observer bubble has destabilised — render accordingly.
        /// </summary>
        public static bool IsCoherent(OmegaZaTaCoordinate c)
        {
            return c.Xi >= CosmologicalConstants.XI_CRITICAL;
        }

        // -----------------------------------------------------------------------
        // Private constraint methods
        // -----------------------------------------------------------------------

        private static OmegaZaTaCoordinate ClampRedshift(OmegaZaTaCoordinate c)
        {
            // Convert outer zenith to redshift, clamp, convert back
            float z = OmegaZaTaCoordinate.ZaOuterToRedshift(c.Za_outer);
            z = Mathf.Clamp(z, CosmologicalConstants.Z_NOW, Z_APPROACH_LIMIT);
            c.Za_outer = OmegaZaTaCoordinate.RedshiftToZaOuter(z);
            return c;
        }

        private static OmegaZaTaCoordinate WrapAngles(OmegaZaTaCoordinate c)
        {
            // φ (azimuth) wraps [0, 2π]
            c.Az_outer = WrapAngle2Pi(c.Az_outer);
            c.Az_inner = WrapAngle2Pi(c.Az_inner);

            // θ (colatitude) clamps [0, π] — no wrap, physical pole
            c.Za_outer = Mathf.Clamp(c.Za_outer, 0f, Mathf.PI);
            c.Za_inner = Mathf.Clamp(c.Za_inner, 0f, Mathf.PI);

            return c;
        }

        private static OmegaZaTaCoordinate ClampOmega(OmegaZaTaCoordinate c)
        {
            c.Omega = Mathf.Clamp(
                c.Omega,
                CosmologicalConstants.OMEGA_MIN,
                CosmologicalConstants.OMEGA_MAX);
            return c;
        }

        private static OmegaZaTaCoordinate ClampXi(OmegaZaTaCoordinate c)
        {
            c.Xi = Mathf.Clamp01(c.Xi);
            return c;
        }

        private static OmegaZaTaCoordinate ClampTime(OmegaZaTaCoordinate c)
        {
            // Subjective time never exceeds proper time
            if (c.Ta_subjective > c.Ta_proper)
                c.Ta_subjective = c.Ta_proper;

            // Both are non-negative
            if (c.Ta_proper    < 0.0) c.Ta_proper    = 0.0;
            if (c.Ta_subjective < 0.0) c.Ta_subjective = 0.0;

            return c;
        }

        private static float WrapAngle2Pi(float angle)
        {
            angle = angle % (Mathf.PI * 2f);
            if (angle < 0f) angle += Mathf.PI * 2f;
            return angle;
        }
    }
}

// OmegaZaTaCoordinate.cs
// The core coordinate struct for the ΩaZaTa framework.
//
// Every observer is a point on the Zeta spiral relative to Znull/Tnull/Onull.
// The double zenith gives two spherical coordinate frames simultaneously:
//   Outer zenith → CMB boundary (z ≈ 1090, past light cone)
//   Inner zenith → Milky Way / LSS boundary (z ≈ 0, present state)
// The observer bubble is the frustum volume BETWEEN these two zeniths.
//
// Time dilation lives here as the tax() method:
//   Δt_subjective = Δτ_proper - tax(density, Ω, geometry)

using System;
using UnityEngine;

namespace InfiniteImprobability.Core
{
    [Serializable]
    public struct OmegaZaTaCoordinate
    {
        // -----------------------------------------------------------------------
        // Outer zenith — oriented toward the CMB boundary (past, z ≈ 1090)
        // Nautical analogy: celestial zenith toward the pole star
        // -----------------------------------------------------------------------

        /// <summary>Colatitude toward CMB surface (θ, radians, [0, π])</summary>
        public float Za_outer;

        /// <summary>Azimuth on the CMB sphere (φ, radians, [0, 2π])</summary>
        public float Az_outer;

        // -----------------------------------------------------------------------
        // Inner zenith — oriented toward local structure (present, z ≈ 0)
        // Nautical analogy: local zenith (straight up from the ship)
        // -----------------------------------------------------------------------

        /// <summary>Colatitude toward local large scale structure (θ, radians, [0, π])</summary>
        public float Za_inner;

        /// <summary>Azimuth within local LSS frame (φ, radians, [0, 2π])</summary>
        public float Az_inner;

        // -----------------------------------------------------------------------
        // T axis — proper time, the primary evolution axis
        // Za, Az, Ω, Ξ all evolve as functions of Ta
        // -----------------------------------------------------------------------

        /// <summary>
        /// Proper time coordinate (τ). The quantum background tick axis.
        /// Subjective time is derived from this minus the movement tax.
        /// </summary>
        public double Ta_proper;

        /// <summary>
        /// Accumulated subjective time (t). What the observer experiences.
        /// Always ≤ Ta_proper — the tax is always non-negative.
        /// </summary>
        public double Ta_subjective;

        // -----------------------------------------------------------------------
        // Observer bubble parameters
        // -----------------------------------------------------------------------

        /// <summary>
        /// Ω — winding number / fidelity scale.
        /// Controls the resolution of the inner boundary.
        /// Small Ω = local neighbourhood. Large Ω = full observable universe.
        /// Range: [OMEGA_MIN, OMEGA_MAX]
        /// </summary>
        public int Omega;

        /// <summary>
        /// Ξ — coherence amplitude [0, 1].
        /// The first symmetry break threshold. Must be ≥ XI_CRITICAL for a
        /// stable classical observer bubble to exist.
        /// Baseline positive trust: drifts toward XI_BASELINE when unstressed.
        /// </summary>
        public float Xi;

        /// <summary>
        /// Oa — integer layer depth (Xi prestige level / winding tier).
        /// Each increment of Oa represents a full coherence winding.
        /// </summary>
        public int Oa;

        // -----------------------------------------------------------------------
        // Zeta spiral — position relative to the null origin
        // -----------------------------------------------------------------------

        /// <summary>
        /// Z phase — the imaginary component of the Zeta spiral coordinate.
        /// Load-bearing degree of freedom. Encodes phase relationship to Znull.
        /// </summary>
        public float Z_phase;

        /// <summary>
        /// Z magnitude — the real component of mesh coupling / horizontal weighting.
        /// Primary key for this observer in the Ohmazata mesh.
        /// </summary>
        public float Z_magnitude;

        // -----------------------------------------------------------------------
        // Derived properties
        // -----------------------------------------------------------------------

        /// <summary>True if the observer bubble is in a classically stable state (Ξ ≥ Ξ_c)</summary>
        public bool IsCoherent => Xi >= CosmologicalConstants.XI_CRITICAL;

        /// <summary>Redshift corresponding to current outer zenith depth</summary>
        public float RedshiftZ => ZaOuterToRedshift(Za_outer);

        /// <summary>
        /// Approximate volume of the double-zenith frustum.
        /// Proportional to (cos Za_inner - cos Za_outer) × Ω².
        /// Represents the observer's causal diamond at current scale.
        /// </summary>
        public float BubbleVolume
        {
            get
            {
                float cosDiff = Mathf.Cos(Za_inner) - Mathf.Cos(Za_outer);
                return Mathf.Abs(cosDiff) * Omega * Omega;
            }
        }

        /// <summary>
        /// Complex Zeta coordinate (Z_magnitude + i·Z_phase).
        /// Position of this observer on the Zeta spiral.
        /// </summary>
        public Vector2 ZetaComplex => new Vector2(Z_magnitude, Z_phase);

        // -----------------------------------------------------------------------
        // Time tax — the movement cost paid on top of proper time progression
        // Δt_subjective = Δτ_proper - ComputeTimeTax(density, geometry)
        // Tax is proportional to information/energy/mass density × dimensional geometry
        // -----------------------------------------------------------------------

        /// <summary>
        /// Compute the time tax for one proper-time tick.
        /// </summary>
        /// <param name="localDensity">Normalised local energy/mass/information density [0, 1]</param>
        /// <param name="dimensionsNavigated">Number of extra dimensions currently active (0–6)</param>
        /// <returns>Tax to subtract from Δτ to get Δt_subjective</returns>
        public float ComputeTimeTax(float localDensity, int dimensionsNavigated)
        {
            // Linear term: density × base coefficient
            float linearTax = localDensity * CosmologicalConstants.TAX_LINEAR_COEFF;

            // Non-linear term: density² (dominates near the CMB surface)
            float nonlinearTax = (localDensity * localDensity)
                                 * CosmologicalConstants.TAX_NONLINEAR_COEFF;

            // Dimensional geometry term: each extra dimension adds cost
            float dimensionTax = dimensionsNavigated
                                 * CosmologicalConstants.TAX_DIMENSION_MULTIPLIER
                                 * localDensity;

            // Ω multiplier: higher fidelity scale = more structure to traverse = more tax
            float omegaMultiplier = 1f + (Omega - 1) * 0.05f;

            return (linearTax + nonlinearTax + dimensionTax) * omegaMultiplier;
        }

        /// <summary>
        /// Advance proper time by one tick and apply the movement tax.
        /// Updates both Ta_proper and Ta_subjective.
        /// </summary>
        public void AdvanceTick(float localDensity, int dimensionsNavigated)
        {
            double dtProper = CosmologicalConstants.TAU_PLANCK;
            double tax = ComputeTimeTax(localDensity, dimensionsNavigated);

            Ta_proper    += dtProper;
            Ta_subjective += Math.Max(0.0, dtProper - tax);
        }

        // -----------------------------------------------------------------------
        // Coordinate conversions
        // -----------------------------------------------------------------------

        /// <summary>
        /// Map outer zenith colatitude to an approximate cosmological redshift.
        /// Za_outer = 0   → z = 0 (observer at present, facing self)
        /// Za_outer = π/2 → z = Z_PHOTON_DECOUPLING / 2
        /// Za_outer = π   → z = Z_PHOTON_DECOUPLING (CMB surface)
        /// </summary>
        public static float ZaOuterToRedshift(float za_outer)
        {
            float t = za_outer / Mathf.PI; // normalise to [0, 1]
            return t * CosmologicalConstants.Z_PHOTON_DECOUPLING;
        }

        /// <summary>Inverse: redshift → Za_outer colatitude</summary>
        public static float RedshiftToZaOuter(float z)
        {
            float t = Mathf.Clamp01(z / CosmologicalConstants.Z_PHOTON_DECOUPLING);
            return t * Mathf.PI;
        }

        // -----------------------------------------------------------------------
        // Factory — default coherent observer at present epoch
        // -----------------------------------------------------------------------

        /// <summary>
        /// Create a new observer coordinate at the present epoch (z=0) with
        /// baseline positive trust and default Ω fidelity.
        /// </summary>
        public static OmegaZaTaCoordinate CreateDefault()
        {
            return new OmegaZaTaCoordinate
            {
                Za_outer     = 0f,
                Az_outer     = 0f,
                Za_inner     = 0f,
                Az_inner     = 0f,
                Ta_proper    = 0.0,
                Ta_subjective = 0.0,
                Omega        = CosmologicalConstants.OMEGA_DEFAULT,
                Xi           = CosmologicalConstants.XI_BASELINE,
                Oa           = 1,
                Z_phase      = 0f,
                Z_magnitude  = 1f
            };
        }
    }
}

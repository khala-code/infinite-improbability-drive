// TimeTaxComputer.cs
// Converts quantum proper time (τ) into subjective time (t) by applying
// the movement tax — the cost paid proportional to information/energy/mass
// density and the geometry of dimensions being navigated.
//
// Architecture note:
//   Proper time τ is the universal background tick (Planck second rotor).
//   Subjective time t is what the observer experiences.
//   Time dilation is NOT a camera effect — it is a first-class coordinate
//   transformation computed here every tick before anything renders.
//
// Tax formula:
//   tax = (ρ·k_linear + ρ²·k_nonlinear + d·k_dim·ρ) × (1 + (Ω-1)·0.05)
//   Δt_subjective = max(0, Δτ - tax)
//
// Near z=1090 (approaching CMB surface): density → 1, tax → max, subjective
// time crawls. Moving through a cosmic void: tax → 0, τ ≈ t.

using UnityEngine;

namespace InfiniteImprobability.Core
{
    public class TimeTaxComputer : MonoBehaviour
    {
        // -----------------------------------------------------------------------
        // Diagnostics (Inspector readable)
        // -----------------------------------------------------------------------

        [Header("Last Tick")]
        [SerializeField, Tooltip("Proper time elapsed last tick")]
        private float _lastProperDt;

        [SerializeField, Tooltip("Tax paid last tick")]
        private float _lastTax;

        [SerializeField, Tooltip("Subjective time elapsed last tick")]
        private float _lastSubjectiveDt;

        [SerializeField, Tooltip("Dilation ratio t/τ (1 = no dilation)")]
        private float _lastDilationRatio;

        [Header("Environment")]
        [SerializeField, Range(0f, 1f), Tooltip("Normalised local density [0=void, 1=CMB surface]")]
        private float _localDensity = 0.05f;

        [SerializeField, Range(0, 6), Tooltip("Number of extra dimensions currently active")]
        private int _dimensionsNavigated = 4; // default: 4+1D present trident

        // -----------------------------------------------------------------------
        // Public state
        // -----------------------------------------------------------------------

        /// <summary>Total proper time accumulated (τ, engine seconds)</summary>
        public double TotalProperTime   { get; private set; }

        /// <summary>Total subjective time accumulated (t, engine seconds)</summary>
        public double TotalSubjectiveTime { get; private set; }

        /// <summary>Running dilation ratio t/τ</summary>
        public float DilationRatio => TotalProperTime > 0
            ? (float)(TotalSubjectiveTime / TotalProperTime)
            : 1f;

        // -----------------------------------------------------------------------
        // External API — called by ObserverBubble when environment changes
        // -----------------------------------------------------------------------

        /// <summary>
        /// Update the local density estimate.
        /// This is set from the current redshift z and the matter power spectrum:
        /// near z=1090, density → 1; in a void, density → 0.
        /// </summary>
        public void SetLocalDensity(float density)
        {
            _localDensity = Mathf.Clamp01(density);
        }

        /// <summary>Update number of active dimensions (drives the dimensional geometry tax)</summary>
        public void SetDimensionsNavigated(int dims)
        {
            _dimensionsNavigated = Mathf.Clamp(dims, 0, 6);
        }

        // -----------------------------------------------------------------------
        // Per-frame update
        // -----------------------------------------------------------------------

        private void Update()
        {
            float dtProper = Time.deltaTime; // proxy for Δτ in engine time

            float tax = ComputeTax(_localDensity, _dimensionsNavigated,
                                   CosmologicalConstants.OMEGA_DEFAULT);

            float dtSubjective = Mathf.Max(0f, dtProper - tax);

            TotalProperTime    += dtProper;
            TotalSubjectiveTime += dtSubjective;

            // Inspector diagnostics
            _lastProperDt    = dtProper;
            _lastTax         = tax;
            _lastSubjectiveDt = dtSubjective;
            _lastDilationRatio = dtProper > 0f ? dtSubjective / dtProper : 1f;
        }

        // -----------------------------------------------------------------------
        // Tax computation — pure function, safe to call from any context
        // -----------------------------------------------------------------------

        /// <summary>
        /// Compute the time tax for a given environment state.
        /// Pure function — no side effects.
        /// </summary>
        /// <param name="density">Normalised local density [0, 1]</param>
        /// <param name="dims">Extra dimensions navigated [0, 6]</param>
        /// <param name="omega">Current Ω winding number</param>
        /// <returns>Tax to subtract from Δτ</returns>
        public static float ComputeTax(float density, int dims, int omega)
        {
            float linear    = density
                              * CosmologicalConstants.TAX_LINEAR_COEFF;

            float nonLinear = density * density
                              * CosmologicalConstants.TAX_NONLINEAR_COEFF;

            float dimTax    = dims
                              * CosmologicalConstants.TAX_DIMENSION_MULTIPLIER
                              * density;

            float omegaMult = 1f + (omega - 1) * 0.05f;

            return (linear + nonLinear + dimTax) * omegaMult;
        }

        /// <summary>
        /// Estimate local density from cosmological redshift z.
        /// Approximate: density scales as (1+z)³ for matter, normalised to [0,1].
        /// </summary>
        public static float DensityFromRedshift(float z)
        {
            float raw = Mathf.Pow(1f + z, 3f)
                        / Mathf.Pow(1f + CosmologicalConstants.Z_PHOTON_DECOUPLING, 3f);
            return Mathf.Clamp01(raw);
        }
    }
}

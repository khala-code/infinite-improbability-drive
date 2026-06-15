// BlochEvolver.cs
// Integrates the extended Bloch equation for the observer bubble's spin dynamics.
//
// The equation:
//   dS/dt = γ(S × B_total)          — standard Larmor precession
//         + Ξ(S·û)(S × û)           — nonlinear Xi coherence term (activates above Ξ_c)
//         + η(t)(S × n̂)             — injected noise (quantum foam background)
//
// Properties:
//   • Preserves S² = 1 identically — stays on the Bloch sphere at all times
//   • Both cross-product terms are perpendicular to S → S·(dS/dt) = 0
//   • The Ξ term only activates when Xi ≥ XI_CRITICAL (first symmetry break)
//   • η(t) is stochastic noise — prevents the system from freezing at fixed points
//   • Each tick adds energy proportional to alignment (S·û) via ProperTimeTick

using UnityEngine;

namespace InfiniteImprobability.Core
{
    [RequireComponent(typeof(ProperTimeTick))]
    public class BlochEvolver : MonoBehaviour
    {
        // -----------------------------------------------------------------------
        // External field
        // -----------------------------------------------------------------------

        /// <summary>
        /// Total magnetic-analogue field B_total acting on this observer bubble.
        /// Set by ObserverBubble from the double-zenith configuration.
        /// In the CMB context this encodes the local CMB temperature gradient
        /// direction, projected onto the current Za_outer/Az_outer orientation.
        /// </summary>
        [SerializeField]
        private Vector3 _bTotal = Vector3.up;

        public void SetBField(Vector3 b) => _bTotal = b;

        // -----------------------------------------------------------------------
        // Integration parameters
        // -----------------------------------------------------------------------

        /// <summary>Time step for RK4 integration (seconds, normalised)</summary>
        [SerializeField, Range(0.001f, 0.1f)]
        private float _dt = 0.016f;

        // -----------------------------------------------------------------------
        // Dependencies
        // -----------------------------------------------------------------------

        private ProperTimeTick _clock;
        private OmegaZaTaCoordinate _coord; // read by reference from ObserverBubble

        // -----------------------------------------------------------------------
        // Runtime state
        // -----------------------------------------------------------------------

        /// <summary>Current spin vector S (unit vector on Bloch sphere)</summary>
        private Vector3 _S = Vector3.up;

        public Vector3 SpinVector => _S;

        // -----------------------------------------------------------------------
        // Unity lifecycle
        // -----------------------------------------------------------------------

        private void Awake()
        {
            _clock = GetComponent<ProperTimeTick>();
        }

        private void OnEnable()
        {
            _clock.OnTick += OnProperTimeTick;
        }

        private void OnDisable()
        {
            _clock.OnTick -= OnProperTimeTick;
        }

        // -----------------------------------------------------------------------
        // Public API
        // -----------------------------------------------------------------------

        public void SetCoordinate(OmegaZaTaCoordinate coord)
        {
            _coord = coord;
        }

        // -----------------------------------------------------------------------
        // Tick handler
        // -----------------------------------------------------------------------

        private void OnProperTimeTick(float alignment, float energy)
        {
            Vector3 u = _clock.SpinVector; // coherence axis û from ProperTimeTick
            float xi  = _coord.Xi;

            _S = IntegrateRK4(_S, _bTotal, u, xi, _dt);

            // Feed updated spin back to the clock
            _clock.SetSpinVector(_S);
        }

        // -----------------------------------------------------------------------
        // Extended Bloch equation — RK4 integrator
        // -----------------------------------------------------------------------

        /// <summary>
        /// Compute dS/dt for the extended Bloch equation.
        /// All three terms produce vectors perpendicular to S → S² preserved.
        /// </summary>
        private Vector3 DSDT(Vector3 S, Vector3 B, Vector3 u, float xi)
        {
            // Term 1: Larmor precession γ(S × B)
            Vector3 larmor = CosmologicalConstants.GAMMA_LARMOR
                             * Vector3.Cross(S, B);

            // Term 2: Ξ nonlinear coherence term — only above Ξ_c
            Vector3 xiTerm = Vector3.zero;
            if (xi >= CosmologicalConstants.XI_CRITICAL)
            {
                float alignment = Vector3.Dot(S, u);
                xiTerm = xi * alignment * Vector3.Cross(S, u);
            }

            // Term 3: Injected noise η(t)(S × n̂) — quantum foam background
            // n̂ is a random unit vector sampled each evaluation
            // Amplitude is small — this is background noise, not a driving force
            Vector3 nHat   = Random.onUnitSphere;
            Vector3 noise  = CosmologicalConstants.ETA_NOISE_AMPLITUDE
                             * Vector3.Cross(S, nHat);

            return larmor + xiTerm + noise;
        }

        /// <summary>
        /// 4th-order Runge-Kutta integration step.
        /// Normalises the result back onto the Bloch sphere after each step.
        /// </summary>
        private Vector3 IntegrateRK4(Vector3 S, Vector3 B, Vector3 u, float xi, float dt)
        {
            Vector3 k1 = DSDT(S,                   B, u, xi);
            Vector3 k2 = DSDT(S + 0.5f * dt * k1,  B, u, xi);
            Vector3 k3 = DSDT(S + 0.5f * dt * k2,  B, u, xi);
            Vector3 k4 = DSDT(S + dt * k3,          B, u, xi);

            Vector3 next = S + (dt / 6f) * (k1 + 2f * k2 + 2f * k3 + k4);

            // Re-normalise to keep S on the Bloch sphere (S² = 1)
            return next.normalized;
        }
    }
}

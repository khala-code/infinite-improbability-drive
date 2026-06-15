// ObserverBubble.cs
// The single authority for the observer's state in the ΩaZaTa framework.
//
// ObserverBubble owns the OmegaZaTaCoordinate and orchestrates all core systems:
//   • Validates every coordinate change before it propagates
//   • Derives the coherence axis û from the double-zenith geometry
//   • Sets the B_field for BlochEvolver from the CMB temperature gradient
//   • Drives TimeTaxComputer with the current redshift density
//   • Exposes events for renderers and navigators to subscribe to
//   • Manages Ξ evolution (drain under density, recover toward baseline)
//
// Nothing writes to the coordinate except through ObserverBubble.ApplyDelta()
// or ObserverBubble.TeleportTo(). No other script touches _coord directly.

using System;
using UnityEngine;

namespace InfiniteImprobability.Core
{
    [RequireComponent(typeof(ProperTimeTick))]
    [RequireComponent(typeof(BlochEvolver))]
    [RequireComponent(typeof(TimeTaxComputer))]
    public class ObserverBubble : MonoBehaviour
    {
        // -----------------------------------------------------------------------
        // State
        // -----------------------------------------------------------------------

        private OmegaZaTaCoordinate _coord;

        /// <summary>Read-only snapshot of the current coordinate.</summary>
        public OmegaZaTaCoordinate Coordinate => _coord;

        // -----------------------------------------------------------------------
        // Dependencies
        // -----------------------------------------------------------------------

        private ProperTimeTick  _clock;
        private BlochEvolver    _bloch;
        private TimeTaxComputer _taxComputer;

        // -----------------------------------------------------------------------
        // Events — renderers and navigators subscribe here
        // -----------------------------------------------------------------------

        /// <summary>Fired whenever the coordinate changes (after validation).</summary>
        public event Action<OmegaZaTaCoordinate> OnCoordinateChanged;

        /// <summary>Fired when Ξ crosses XI_CRITICAL (bubble destabilises or restabilises).</summary>
        public event Action<bool> OnCoherenceChanged; // true = coherent, false = destabilised

        /// <summary>Fired when Ω changes — fidelity scale shift.</summary>
        public event Action<int> OnOmegaChanged;

        // -----------------------------------------------------------------------
        // Inspector
        // -----------------------------------------------------------------------

        [Header("Debug")]
        [SerializeField] private bool _logCoordinateChanges = false;

        // -----------------------------------------------------------------------
        // Unity lifecycle
        // -----------------------------------------------------------------------

        private void Awake()
        {
            _clock       = GetComponent<ProperTimeTick>();
            _bloch       = GetComponent<BlochEvolver>();
            _taxComputer = GetComponent<TimeTaxComputer>();

            _coord = OmegaZaTaCoordinate.CreateDefault();
            SyncSubsystems();
        }

        private void FixedUpdate()
        {
            // Advance proper time and pay the movement tax
            float density = TimeTaxComputer.DensityFromRedshift(_coord.RedshiftZ);
            _coord.AdvanceTick(density, ActiveDimensions());

            // Evolve Ξ — drain under density stress, recover toward baseline
            bool wasCoherent = _coord.IsCoherent;
            _coord = CoordinateValidator.EvolveXi(_coord, density);
            _coord = CoordinateValidator.Validate(_coord);

            // Fire coherence event on threshold crossing
            if (_coord.IsCoherent != wasCoherent)
                OnCoherenceChanged?.Invoke(_coord.IsCoherent);

            OnCoordinateChanged?.Invoke(_coord);
        }

        // -----------------------------------------------------------------------
        // Public API — the only legal ways to change the coordinate
        // -----------------------------------------------------------------------

        /// <summary>
        /// Apply a smooth delta to the coordinate (navigation input).
        /// All constraints are validated before the change takes effect.
        /// </summary>
        public void ApplyDelta(
            float dZa_outer  = 0f,
            float dAz_outer  = 0f,
            float dZa_inner  = 0f,
            float dAz_inner  = 0f,
            float dXi        = 0f,
            int   dOmega     = 0)
        {
            int prevOmega = _coord.Omega;

            _coord.Za_outer += dZa_outer;
            _coord.Az_outer += dAz_outer;
            _coord.Za_inner += dZa_inner;
            _coord.Az_inner += dAz_inner;
            _coord.Xi       += dXi;
            _coord.Omega    += dOmega;

            _coord = CoordinateValidator.Validate(_coord);
            SyncSubsystems();

            if (_coord.Omega != prevOmega)
                OnOmegaChanged?.Invoke(_coord.Omega);

            if (_logCoordinateChanges)
                Debug.Log($"[ObserverBubble] z={_coord.RedshiftZ:F1} "
                          + $"Ξ={_coord.Xi:F3} Ω={_coord.Omega} "
                          + $"BubbleVol={_coord.BubbleVolume:F2}");
        }

        /// <summary>
        /// Jump directly to a coordinate on the Z_LADDER (discrete redshift step).
        /// Validates the target before applying.
        /// </summary>
        public void TeleportToRedshift(float targetZ)
        {
            float za = OmegaZaTaCoordinate.RedshiftToZaOuter(targetZ);
            _coord.Za_outer = za;
            _coord = CoordinateValidator.Validate(_coord);
            SyncSubsystems();
            OnCoordinateChanged?.Invoke(_coord);
        }

        /// <summary>Directly set Ω fidelity level (validated).</summary>
        public void SetOmega(int omega)
        {
            int prev = _coord.Omega;
            _coord.Omega = omega;
            _coord = CoordinateValidator.Validate(_coord);
            if (_coord.Omega != prev)
                OnOmegaChanged?.Invoke(_coord.Omega);
        }

        // -----------------------------------------------------------------------
        // Internal helpers
        // -----------------------------------------------------------------------

        /// <summary>
        /// Sync all subsystems to the current coordinate.
        /// Called after every coordinate change.
        /// </summary>
        private void SyncSubsystems()
        {
            // Derive coherence axis û — bisector of outer and inner zenith directions
            Vector3 outerDir = SphericalToCartesian(_coord.Za_outer, _coord.Az_outer);
            Vector3 innerDir = SphericalToCartesian(_coord.Za_inner, _coord.Az_inner);
            Vector3 uHat     = (outerDir + innerDir).normalized;

            _clock.SetCoherenceAxis(uHat);

            // B_field for Bloch evolver: approximate CMB gradient along outer zenith
            // direction, scaled by current Ξ (coherent field is stronger)
            Vector3 bField = outerDir * _coord.Xi;
            _bloch.SetBField(bField);
            _bloch.SetCoordinate(_coord);

            // Time tax: update density from current redshift
            float density = TimeTaxComputer.DensityFromRedshift(_coord.RedshiftZ);
            _taxComputer.SetLocalDensity(density);
            _taxComputer.SetDimensionsNavigated(ActiveDimensions());
        }

        /// <summary>
        /// Number of active dimensions for the time tax.
        /// Scales with Ω and current trident: base 4+1D, up to 6+1D near CMB.
        /// </summary>
        private int ActiveDimensions()
        {
            // Near the CMB surface (z > 500), the future trident becomes active
            // and we're navigating closer to 6+1D geometry
            if (_coord.RedshiftZ > 500f) return 6;
            if (_coord.RedshiftZ > 10f)  return 5;
            return 4; // default: present trident (4+1D)
        }

        /// <summary>Convert spherical (θ, φ) to Cartesian unit vector.</summary>
        private static Vector3 SphericalToCartesian(float theta, float phi)
        {
            return new Vector3(
                Mathf.Sin(theta) * Mathf.Cos(phi),
                Mathf.Cos(theta),
                Mathf.Sin(theta) * Mathf.Sin(phi)
            );
        }
    }
}

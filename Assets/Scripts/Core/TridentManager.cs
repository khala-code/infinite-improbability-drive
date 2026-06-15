// TridentManager.cs
// Orchestrates the three simultaneous Lagrangians (tridents) of the observer bubble.
//
// The three tridents:
//   L1  Past    (2+1D)  — already-intersected wavefronts, cached, immutable
//   L2  Present (4+1D)  — the active observer bubble, the Bloch equation lives here
//   L3  Future  (6+1D)  — probabilistic projection, non-linear noise feedback
//
// Computational reducibility principle (from design):
//   We do not simulate the whole universe — only the wave propagations that
//   intersect the observer bubble's trajectory. The WavefrontIndex (Phase 4)
//   will cull everything outside the causal diamond.
//
// L3 authority:
//   The future trident is NOT deterministic. It provides probabilistic weights
//   for the next coordinate state, feeding back into the present via the
//   noise term η(t) in the Bloch evolver. The observer is pulled forward
//   by L3 as well as pushed by L1.
//
// Which Lagrangian is authoritative per frame:
//   • z > Z_L3_THRESHOLD (near CMB)  → L3 dominates (future trident activates strongly)
//   • z in [Z_L2_MIN, Z_L3_THRESHOLD] → L2 dominates (standard present-epoch navigation)
//   • z < Z_L2_MIN (very local)        → L1 increasingly dominates (local history caching)

using System;
using System.Collections.Generic;
using UnityEngine;

namespace InfiniteImprobability.Core
{
    public class TridentManager : MonoBehaviour
    {
        // -----------------------------------------------------------------------
        // Thresholds
        // -----------------------------------------------------------------------

        private const float Z_L3_THRESHOLD = 500f;  // z > 500: L3 dominates
        private const float Z_L2_MIN       = 0.5f;  // z < 0.5: L1 increasingly active

        // -----------------------------------------------------------------------
        // Trident state
        // -----------------------------------------------------------------------

        public enum ActiveTrident { L1_Past, L2_Present, L3_Future }

        /// <summary>Which trident is currently authoritative for the observer.</summary>
        public ActiveTrident CurrentTrident { get; private set; } = ActiveTrident.L2_Present;

        /// <summary>Blended weight of each trident this frame [0,1], sums to 1.</summary>
        public (float l1, float l2, float l3) TridentWeights { get; private set; }

        // -----------------------------------------------------------------------
        // L1 — Past trident: cached wavefront intersections
        // -----------------------------------------------------------------------

        private struct CachedWavefront
        {
            public float Redshift;
            public Vector3 Direction;    // direction of arrival in Za_outer frame
            public float Temperature;    // CMB temperature at this pixel (normalised)
            public double ProperTimeOfIntersection;
        }

        private readonly List<CachedWavefront> _l1Cache = new List<CachedWavefront>();
        private const int L1_MAX_CACHE_SIZE = 4096;

        // -----------------------------------------------------------------------
        // L3 — Future trident: probabilistic projection weights
        // -----------------------------------------------------------------------

        /// <summary>
        /// L3 probability weights over the Z_LADDER steps.
        /// Represents the likelihood the observer will move to each epoch next.
        /// Updated each frame based on current Ξ, Ω, and Bloch spin alignment.
        /// </summary>
        private float[] _l3Weights;

        // -----------------------------------------------------------------------
        // Events
        // -----------------------------------------------------------------------

        /// <summary>Fired when the dominant trident changes.</summary>
        public event Action<ActiveTrident> OnTridentChanged;

        // -----------------------------------------------------------------------
        // Dependencies
        // -----------------------------------------------------------------------

        private ObserverBubble _bubble;
        private BlochEvolver   _bloch;

        // -----------------------------------------------------------------------
        // Unity lifecycle
        // -----------------------------------------------------------------------

        private void Awake()
        {
            _bubble = GetComponent<ObserverBubble>();
            _bloch  = GetComponent<BlochEvolver>();

            _l3Weights = new float[CosmologicalConstants.Z_LADDER.Length];
            InitL3Weights();

            if (_bubble != null)
                _bubble.OnCoordinateChanged += OnCoordinateChanged;
        }

        private void OnDestroy()
        {
            if (_bubble != null)
                _bubble.OnCoordinateChanged -= OnCoordinateChanged;
        }

        // -----------------------------------------------------------------------
        // Coordinate change handler — recompute trident weights
        // -----------------------------------------------------------------------

        private void OnCoordinateChanged(OmegaZaTaCoordinate coord)
        {
            UpdateTridentWeights(coord);
            UpdateL3Projection(coord);
        }

        // -----------------------------------------------------------------------
        // L1 — Cache a wavefront intersection (called by OuterBoundary)
        // -----------------------------------------------------------------------

        /// <summary>
        /// Record a wavefront intersection into the L1 past cache.
        /// Once cached, a wavefront is immutable — the past is fixed.
        /// </summary>
        public void CacheWavefront(
            float redshift, Vector3 direction, float temperature, double properTime)
        {
            if (_l1Cache.Count >= L1_MAX_CACHE_SIZE)
                _l1Cache.RemoveAt(0); // FIFO eviction when full

            _l1Cache.Add(new CachedWavefront
            {
                Redshift                 = redshift,
                Direction                = direction.normalized,
                Temperature              = temperature,
                ProperTimeOfIntersection = properTime
            });
        }

        /// <summary>Number of wavefronts currently in the L1 cache.</summary>
        public int L1CacheSize => _l1Cache.Count;

        // -----------------------------------------------------------------------
        // L3 — Future trident projection
        // -----------------------------------------------------------------------

        /// <summary>
        /// Get the L3 probability weight for a given Z_LADDER index.
        /// Higher weight = more likely next destination.
        /// </summary>
        public float GetL3Weight(int ladderIndex)
        {
            if (ladderIndex < 0 || ladderIndex >= _l3Weights.Length) return 0f;
            return _l3Weights[ladderIndex];
        }

        // -----------------------------------------------------------------------
        // Trident weight computation
        // -----------------------------------------------------------------------

        private void UpdateTridentWeights(OmegaZaTaCoordinate coord)
        {
            float z = coord.RedshiftZ;

            // Raw weights based on redshift
            float rawL3 = Mathf.Clamp01((z - Z_L2_MIN) / (Z_L3_THRESHOLD - Z_L2_MIN));
            float rawL1 = Mathf.Clamp01(1f - z / Z_L2_MIN);
            float rawL2 = 1f - Mathf.Max(rawL1, rawL3);

            // Ξ modulates: low coherence → L1 and L3 gain weight (past echoes + future pulls)
            float xiMod = 1f - coord.Xi;
            rawL1 += xiMod * 0.15f;
            rawL3 += xiMod * 0.10f;
            rawL2  = Mathf.Max(0f, 1f - rawL1 - rawL3);

            // Normalise
            float total = rawL1 + rawL2 + rawL3;
            if (total > 0f) { rawL1 /= total; rawL2 /= total; rawL3 /= total; }

            TridentWeights = (rawL1, rawL2, rawL3);

            // Determine dominant trident
            ActiveTrident dominant;
            if (rawL3 >= rawL1 && rawL3 >= rawL2)      dominant = ActiveTrident.L3_Future;
            else if (rawL1 >= rawL2 && rawL1 >= rawL3) dominant = ActiveTrident.L1_Past;
            else                                        dominant = ActiveTrident.L2_Present;

            if (dominant != CurrentTrident)
            {
                CurrentTrident = dominant;
                OnTridentChanged?.Invoke(CurrentTrident);
                Debug.Log($"[TridentManager] Dominant trident: {CurrentTrident} "
                          + $"(L1={rawL1:F2} L2={rawL2:F2} L3={rawL3:F2})");
            }
        }

        /// <summary>
        /// Update L3 probability weights over Z_LADDER using Bloch spin alignment.
        /// Ladder steps closer to current spin orientation get higher weight.
        /// </summary>
        private void UpdateL3Projection(OmegaZaTaCoordinate coord)
        {
            if (_bloch == null) return;

            Vector3 spinDir = _bloch.SpinVector;
            float   total   = 0f;

            for (int i = 0; i < _l3Weights.Length; i++)
            {
                float ladderZ  = CosmologicalConstants.Z_LADDER[i];
                float za       = OmegaZaTaCoordinate.RedshiftToZaOuter(ladderZ);
                Vector3 ladderDir = new Vector3(
                    Mathf.Sin(za), Mathf.Cos(za), 0f); // simplified 2D projection

                // Alignment of spin with this ladder direction
                float alignment  = (Vector3.Dot(spinDir, ladderDir) + 1f) * 0.5f; // [0,1]
                _l3Weights[i]    = alignment;
                total           += alignment;
            }

            // Normalise
            if (total > 0f)
                for (int i = 0; i < _l3Weights.Length; i++)
                    _l3Weights[i] /= total;
        }

        private void InitL3Weights()
        {
            float uniform = 1f / _l3Weights.Length;
            for (int i = 0; i < _l3Weights.Length; i++)
                _l3Weights[i] = uniform;
        }
    }
}

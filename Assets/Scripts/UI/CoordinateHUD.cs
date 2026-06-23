// CoordinateHUD.cs
// World-space coordinate readout anchored in VR space.
//
// Displays the observer's current ΩaZaTa state as a compact TextMesh:
//
//   z  1089.8  |  Ξ  0.720  |  Ω  4
//   BVol  312.4  |  τ  1.23e-41  |  t  1.19e-41
//   ── Photon decoupling (CMB) ──
//
// Subscribes to ObserverBubble events — zero per-frame polling.
// Flashes red when Ξ drops below XI_CRITICAL (bubble destabilised).
//
// Setup:
//   1. Create a child GameObject under the VR camera rig (or world anchor).
//   2. Add a TextMesh component to it.
//   3. Attach this script; assign _bubble (or leave null to auto-find).
//   4. Position in front of the player at comfortable reading distance.

using System;
using UnityEngine;

namespace InfiniteImprobability.UI
{
    using Core;

    [RequireComponent(typeof(TextMesh))]
    public class CoordinateHUD : MonoBehaviour
    {
        // -----------------------------------------------------------------------
        // Inspector
        // -----------------------------------------------------------------------

        [Header("Dependencies")]
        [SerializeField] private ObserverBubble _bubble;

        [Header("Appearance")]
        [SerializeField] private Color _normalColour    = new Color(0.85f, 0.92f, 1.0f);   // cool white
        [SerializeField] private Color _coherentColour  = new Color(0.6f,  1.0f,  0.7f);   // soft green
        [SerializeField] private Color _warnColour      = new Color(1.0f,  0.35f, 0.25f);  // red-orange
        [SerializeField] private float _flashDuration   = 0.4f;

        [Header("Update")]
        [Tooltip("Minimum seconds between text rebuilds (clamped to avoid constant GC)")]
        [SerializeField] private float _minUpdateInterval = 0.05f;

        // -----------------------------------------------------------------------
        // Private
        // -----------------------------------------------------------------------

        private TextMesh        _text;
        private float           _flashTimer     = 0f;
        private bool            _isCoherent     = true;
        private float           _lastUpdateTime = 0f;
        private OmegaZaTaCoordinate _lastCoord;

        // -----------------------------------------------------------------------
        // Unity lifecycle
        // -----------------------------------------------------------------------

        private void Awake()
        {
            _text = GetComponent<TextMesh>();

            if (_bubble == null)
                _bubble = GetComponentInParent<ObserverBubble>();
            if (_bubble == null)
                _bubble = FindObjectOfType<ObserverBubble>();

            if (_bubble == null)
            {
                Debug.LogError("[CoordinateHUD] No ObserverBubble found.");
                return;
            }

            _lastCoord = _bubble.Coordinate;
        }

        private void OnEnable()
        {
            if (_bubble == null) return;
            _bubble.OnCoordinateChanged += OnCoordinateChanged;
            _bubble.OnCoherenceChanged  += OnCoherenceChanged;
        }

        private void OnDisable()
        {
            if (_bubble == null) return;
            _bubble.OnCoordinateChanged -= OnCoordinateChanged;
            _bubble.OnCoherenceChanged  -= OnCoherenceChanged;
        }

        private void Start()
        {
            if (_bubble != null)
                Rebuild(_bubble.Coordinate);
        }

        private void Update()
        {
            // Tick flash animation
            if (_flashTimer > 0f)
            {
                _flashTimer -= Time.deltaTime;
                float t = 1f - Mathf.Clamp01(_flashTimer / _flashDuration);
                _text.color = Color.Lerp(_warnColour, _isCoherent ? _coherentColour : _normalColour, t);
            }
        }

        // -----------------------------------------------------------------------
        // Event handlers
        // -----------------------------------------------------------------------

        private void OnCoordinateChanged(OmegaZaTaCoordinate coord)
        {
            // Throttle rebuilds
            if (Time.time - _lastUpdateTime < _minUpdateInterval) return;
            _lastUpdateTime = Time.time;
            _lastCoord = coord;
            Rebuild(coord);
        }

        private void OnCoherenceChanged(bool coherent)
        {
            _isCoherent = coherent;
            _flashTimer = _flashDuration;
            // Force an immediate rebuild so the epoch line updates
            Rebuild(_lastCoord);
        }

        // -----------------------------------------------------------------------
        // Text construction
        // -----------------------------------------------------------------------

        private void Rebuild(OmegaZaTaCoordinate coord)
        {
            string epochName  = EpochName(coord.RedshiftZ);
            string coherState = coord.IsCoherent ? "COHERENT" : "DESTABILISED";

            // Format proper/subjective time in scientific notation
            string tProper = FormatSci(coord.Ta_proper);
            string tSubj   = FormatSci(coord.Ta_subjective);

            _text.text =
                $"z  {coord.RedshiftZ,9:F1}  |  \u039e  {coord.Xi:F3}  |  \u03a9  {coord.Omega}\n" +
                $"BVol  {coord.BubbleVolume,7:F1}  |  \u03c4  {tProper}  |  t  {tSubj}\n" +
                $"\u2500\u2500 {epochName} \u2500\u2500  [{coherState}]";

            // Colour: warn if destabilised, coherent green if just restabilised, normal otherwise
            if (_flashTimer <= 0f)
                _text.color = coord.IsCoherent ? _normalColour : _warnColour;
        }

        // -----------------------------------------------------------------------
        // Epoch name from Z_LADDER proximity
        // -----------------------------------------------------------------------

        private static readonly string[] EPOCH_NAMES = new string[]
        {
            "Present",
            "Nearby universe (Hubble flow)",
            "5 Gyr ago",
            "Peak star formation  z~1",
            "Cosmic noon / peak AGN  z~2",
            "Reionisation  z~5",
            "First galaxies  z~10",
            "Matter-radiation equality  z~100",
            "Photon decoupling (CMB)  z~1090",
            "Neutrino decoupling (C\u03bdB)  z~6e9"
        };

        private static string EpochName(float z)
        {
            float[] ladder = CosmologicalConstants.Z_LADDER;
            int best = 0;
            float bestDist = float.MaxValue;

            for (int i = 0; i < ladder.Length; i++)
            {
                // Distance in log space
                float logDist = Mathf.Abs(
                    Mathf.Log10(z + 1f) - Mathf.Log10(ladder[i] + 1f));
                if (logDist < bestDist)
                {
                    bestDist = logDist;
                    best = i;
                }
            }

            int idx = Mathf.Clamp(best, 0, EPOCH_NAMES.Length - 1);
            return EPOCH_NAMES[idx];
        }

        // -----------------------------------------------------------------------
        // Helpers
        // -----------------------------------------------------------------------

        private static string FormatSci(double v)
        {
            if (v == 0.0) return "0";
            int exp    = (int)Math.Floor(Math.Log10(Math.Abs(v)));
            double man = v / Math.Pow(10, exp);
            return $"{man:F2}e{exp}";
        }
    }
}

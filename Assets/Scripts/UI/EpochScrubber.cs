// EpochScrubber.cs
// Horizontal world-space epoch timeline scrubber.
//
// Layout:
//   A flat bar of width _barWidth, centred on this GameObject's position.
//   The scrub axis is log(1+z) / log(1+z_max), matching HolographicLayerController.NormLogZ.
//   Epoch markers (notches + labels) are drawn at each Z_LADDER position.
//   A thumb indicator tracks the current coordinate.
//
// Navigation:
//   Right stick X drag  → smooth scrub (calls TeleportToRedshift each frame)
//   Right trigger press → snap to next Z_LADDER step (mirrors SpacetimeNavigator)
//   Left  trigger press → snap to previous Z_LADDER step
//
// This script owns its own mesh geometry (bar, notches, thumb) built in Start()
// and rebuilt whenever the coordinate changes. All geometry uses procedural
// LineRenderer and child TextMesh objects — no UI Canvas required.
//
// Setup:
//   1. Create an empty child GameObject at a comfortable VR position
//      (e.g., 1.5 m in front and 0.3 m below eye level).
//   2. Attach this script.
//   3. Assign _bubble (or leave null to auto-find).
//   4. Assign _barMaterial — a simple unlit white material.
//   5. Adjust _barWidth (default 0.8 m) and _barHeight (default 0.01 m).

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;

namespace InfiniteImprobability.UI
{
    using Core;

    public class EpochScrubber : MonoBehaviour
    {
        // -----------------------------------------------------------------------
        // Inspector
        // -----------------------------------------------------------------------

        [Header("Dependencies")]
        [SerializeField] private ObserverBubble _bubble;

        [Header("Geometry")]
        [SerializeField] private float  _barWidth      = 0.8f;
        [SerializeField] private float  _barHeight     = 0.006f;
        [SerializeField] private float  _notchHeight   = 0.03f;
        [SerializeField] private float  _thumbSize     = 0.022f;
        [SerializeField] private float  _labelOffset   = 0.05f;   // below bar
        [SerializeField] private float  _labelScale    = 0.007f;

        [Header("Materials")]
        [SerializeField] private Material _barMaterial;            // unlit white
        [SerializeField] private Material _thumbMaterial;          // unlit accent (e.g. warm gold)
        [SerializeField] private Material _notchMaterial;          // unlit grey

        [Header("Navigation")]
        [SerializeField] private float _scrubDeadZone  = 0.15f;
        [SerializeField] private float _scrubSpeed     = 0.4f;    // log-z units/sec
        [SerializeField] private float _ladderCooldown = 0.6f;

        [Header("Colours")]
        [SerializeField] private Color _barColour      = new Color(0.7f, 0.75f, 0.85f);
        [SerializeField] private Color _thumbColour    = new Color(1.0f, 0.85f, 0.4f);
        [SerializeField] private Color _notchColour    = new Color(0.55f, 0.6f, 0.7f);
        [SerializeField] private Color _labelColour    = new Color(0.85f, 0.9f, 1.0f);
        [SerializeField] private Color _activeColour   = new Color(1.0f, 0.95f, 0.6f); // current epoch notch

        // -----------------------------------------------------------------------
        // Epoch data (parallel to CosmologicalConstants.Z_LADDER)
        // -----------------------------------------------------------------------

        private static readonly string[] EPOCH_LABELS = new string[]
        {
            "Now",
            "z~0.1",
            "z~0.5",
            "z~1\nPeak SF",
            "z~2\nCosmic Noon",
            "z~5\nReionisation",
            "z~10\nFirst Galaxies",
            "z~100\nMatter-Rad Eq",
            "z~1090\nCMB",
            "z~6e9\nC\u03bdB"
        };

        // -----------------------------------------------------------------------
        // Runtime objects
        // -----------------------------------------------------------------------

        private LineRenderer    _barLine;
        private GameObject      _thumb;
        private GameObject[]    _notches;
        private TextMesh[]      _labels;
        private int             _currentLadderIndex = 0;
        private float           _ladderCooldownTimer = 0f;

        // XR
        private InputDevice     _leftController;
        private InputDevice     _rightController;
        private bool            _devicesFound = false;

        // -----------------------------------------------------------------------
        // Unity lifecycle
        // -----------------------------------------------------------------------

        private void Awake()
        {
            if (_bubble == null)
                _bubble = GetComponentInParent<ObserverBubble>();
            if (_bubble == null)
                _bubble = FindObjectOfType<ObserverBubble>();

            if (_bubble == null)
                Debug.LogError("[EpochScrubber] No ObserverBubble found.");
        }

        private void Start()
        {
            BuildGeometry();
            if (_bubble != null)
                UpdateThumb(_bubble.Coordinate);
        }

        private void OnEnable()
        {
            if (_bubble != null)
                _bubble.OnCoordinateChanged += OnCoordinateChanged;

            InputDevices.deviceConnected    += OnDeviceConnected;
            InputDevices.deviceDisconnected += OnDeviceDisconnected;
            FindControllers();
        }

        private void OnDisable()
        {
            if (_bubble != null)
                _bubble.OnCoordinateChanged -= OnCoordinateChanged;

            InputDevices.deviceConnected    -= OnDeviceConnected;
            InputDevices.deviceDisconnected -= OnDeviceDisconnected;
        }

        private void Update()
        {
            _ladderCooldownTimer -= Time.deltaTime;

            if (!_devicesFound) { FindControllers(); return; }

            ReadInput();
        }

        // -----------------------------------------------------------------------
        // Geometry construction
        // -----------------------------------------------------------------------

        private void BuildGeometry()
        {
            float[] ladder = CosmologicalConstants.Z_LADDER;

            // --- Bar ---
            _barLine = gameObject.AddComponent<LineRenderer>();
            _barLine.useWorldSpace    = false;
            _barLine.positionCount   = 2;
            _barLine.startWidth      = _barHeight;
            _barLine.endWidth        = _barHeight;
            _barLine.material        = _barMaterial;
            _barLine.startColor      = _barColour;
            _barLine.endColor        = _barColour;
            _barLine.SetPosition(0, new Vector3(-_barWidth * 0.5f, 0f, 0f));
            _barLine.SetPosition(1, new Vector3( _barWidth * 0.5f, 0f, 0f));

            // --- Notches + Labels ---
            float logMax = LogNorm(CosmologicalConstants.Z_NEUTRINO_DECOUPLING, 1f); // should be 1
            _notches = new GameObject[ladder.Length];
            _labels  = new TextMesh[ladder.Length];

            for (int i = 0; i < ladder.Length; i++)
            {
                float t  = NormLogZ(ladder[i]);
                float x  = (t - 0.5f) * _barWidth;

                // Notch
                GameObject notchGO = new GameObject($"Notch_{i}");
                notchGO.transform.SetParent(transform, false);
                notchGO.transform.localPosition = new Vector3(x, 0f, 0f);

                LineRenderer notchLine = notchGO.AddComponent<LineRenderer>();
                notchLine.useWorldSpace  = false;
                notchLine.positionCount  = 2;
                notchLine.startWidth     = _barHeight * 0.5f;
                notchLine.endWidth       = _barHeight * 0.5f;
                notchLine.material       = _notchMaterial;
                notchLine.startColor     = _notchColour;
                notchLine.endColor       = _notchColour;
                notchLine.SetPosition(0, new Vector3(0f, -_notchHeight * 0.5f, 0f));
                notchLine.SetPosition(1, new Vector3(0f,  _notchHeight * 0.5f, 0f));

                _notches[i] = notchGO;

                // Label
                GameObject labelGO = new GameObject($"Label_{i}");
                labelGO.transform.SetParent(notchGO.transform, false);
                labelGO.transform.localPosition = new Vector3(0f, -_notchHeight * 0.5f - _labelOffset, 0f);
                labelGO.transform.localScale    = Vector3.one * _labelScale;

                TextMesh tm = labelGO.AddComponent<TextMesh>();
                tm.text      = EPOCH_LABELS[i];
                tm.anchor    = TextAnchor.UpperCenter;
                tm.alignment = TextAlignment.Center;
                tm.color     = _labelColour;
                tm.fontSize  = 100; // scaled down by localScale

                _labels[i] = tm;
            }

            // --- Thumb ---
            _thumb = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _thumb.name = "EpochThumb";
            _thumb.transform.SetParent(transform, false);
            _thumb.transform.localScale = new Vector3(_thumbSize, _thumbSize * 2.5f, _thumbSize);

            Renderer thumbRend = _thumb.GetComponent<Renderer>();
            if (_thumbMaterial != null)
            {
                thumbRend.material        = _thumbMaterial;
                thumbRend.material.color  = _thumbColour;
            }

            // Remove collider — this is display-only
            Destroy(_thumb.GetComponent<BoxCollider>());
        }

        // -----------------------------------------------------------------------
        // Thumb update
        // -----------------------------------------------------------------------

        private void UpdateThumb(OmegaZaTaCoordinate coord)
        {
            float t = NormLogZ(coord.RedshiftZ);
            float x = (t - 0.5f) * _barWidth;
            _thumb.transform.localPosition = new Vector3(x, 0f, 0f);

            // Highlight the nearest notch
            float[] ladder = CosmologicalConstants.Z_LADDER;
            int nearest = NearestLadderIndex(coord.RedshiftZ);

            for (int i = 0; i < _notches.Length; i++)
            {
                bool active      = (i == nearest);
                Color notchCol   = active ? _activeColour : _notchColour;
                Color labelCol   = active ? _activeColour : _labelColour;

                LineRenderer lr  = _notches[i].GetComponent<LineRenderer>();
                if (lr != null)
                {
                    lr.startColor = notchCol;
                    lr.endColor   = notchCol;
                }
                if (_labels[i] != null)
                    _labels[i].color = labelCol;
            }
        }

        // -----------------------------------------------------------------------
        // Coordinate change handler
        // -----------------------------------------------------------------------

        private void OnCoordinateChanged(OmegaZaTaCoordinate coord)
        {
            UpdateThumb(coord);

            // Keep _currentLadderIndex in sync with coordinate
            _currentLadderIndex = NearestLadderIndex(coord.RedshiftZ);
        }

        // -----------------------------------------------------------------------
        // Input — scrub + snap
        // -----------------------------------------------------------------------

        private void ReadInput()
        {
            if (_bubble == null) return;

            // --- Smooth scrub: right stick X ---
            Vector2 rightStick = Vector2.zero;
            _rightController.TryGetFeatureValue(CommonUsages.primary2DAxis, out rightStick);

            if (Mathf.Abs(rightStick.x) > _scrubDeadZone)
            {
                // Move in log-z space, then convert back to z
                float currentLogZ = LogNorm(_bubble.Coordinate.RedshiftZ, 1f);
                float logMax      = LogNorm(CosmologicalConstants.Z_NEUTRINO_DECOUPLING, 1f);
                float newLogZ     = Mathf.Clamp(
                    currentLogZ + rightStick.x * _scrubSpeed * Time.deltaTime,
                    0f, logMax);
                float newZ = Mathf.Pow(10f, newLogZ) - 1f;
                _bubble.TeleportToRedshift(newZ);
            }

            // --- Snap: triggers ---
            _leftController.TryGetFeatureValue(CommonUsages.triggerButton,  out bool leftTrig);
            _rightController.TryGetFeatureValue(CommonUsages.triggerButton, out bool rightTrig);

            if (_ladderCooldownTimer <= 0f)
            {
                if (leftTrig)  StepLadder(-1);
                if (rightTrig) StepLadder(+1);
            }
        }

        private void StepLadder(int dir)
        {
            float[] ladder = CosmologicalConstants.Z_LADDER;
            _currentLadderIndex = Mathf.Clamp(_currentLadderIndex + dir, 0, ladder.Length - 1);
            _bubble.TeleportToRedshift(ladder[_currentLadderIndex]);
            _ladderCooldownTimer = _ladderCooldown;
        }

        // -----------------------------------------------------------------------
        // XR device management
        // -----------------------------------------------------------------------

        private void FindControllers()
        {
            var left  = new System.Collections.Generic.List<InputDevice>();
            var right = new System.Collections.Generic.List<InputDevice>();
            InputDevices.GetDevicesWithCharacteristics(
                InputDeviceCharacteristics.Left  | InputDeviceCharacteristics.Controller, left);
            InputDevices.GetDevicesWithCharacteristics(
                InputDeviceCharacteristics.Right | InputDeviceCharacteristics.Controller, right);
            if (left.Count  > 0) _leftController  = left[0];
            if (right.Count > 0) _rightController = right[0];
            _devicesFound = left.Count > 0 && right.Count > 0;
        }

        private void OnDeviceConnected(InputDevice d)    => FindControllers();
        private void OnDeviceDisconnected(InputDevice d) => FindControllers();

        // -----------------------------------------------------------------------
        // Log-z axis helpers  (mirrors HolographicLayerController.NormLogZ)
        // -----------------------------------------------------------------------

        /// <summary>Normalise z to [0,1] on a log(1+z) axis against the CνB maximum.</summary>
        private static float NormLogZ(float z)
        {
            float logZ   = Mathf.Log10(Mathf.Max(z, 0f) + 1f);
            float logMax = Mathf.Log10(CosmologicalConstants.Z_NEUTRINO_DECOUPLING + 1f);
            return Mathf.Clamp01(logZ / logMax);
        }

        /// <summary>Raw log10(v+offset) — used for smooth scrub inverse.</summary>
        private static float LogNorm(float v, float offset)
        {
            return Mathf.Log10(Mathf.Max(v, 0f) + offset);
        }

        /// <summary>Index of the nearest Z_LADDER entry in log space.</summary>
        private static int NearestLadderIndex(float z)
        {
            float[] ladder = CosmologicalConstants.Z_LADDER;
            int best = 0;
            float bestDist = float.MaxValue;
            for (int i = 0; i < ladder.Length; i++)
            {
                float d = Mathf.Abs(
                    Mathf.Log10(z + 1f) - Mathf.Log10(ladder[i] + 1f));
                if (d < bestDist) { bestDist = d; best = i; }
            }
            return best;
        }
    }
}

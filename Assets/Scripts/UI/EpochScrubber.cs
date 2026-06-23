// EpochScrubber.cs
// Horizontal world-space epoch timeline scrubber.
//
// Layout:
//   A flat bar of width _barWidth, centred on this GameObject's position.
//   The scrub axis is log(1+z) / log(1+z_max), matching HolographicLayerController.NormLogZ.
//   Epoch markers (notches only — no labels) are drawn at each Z_LADDER position.
//   The current epoch name is shown in CoordinateHUD line 3, not here.
//   A thumb indicator tracks the current coordinate.
//
// Navigation:
//   Right stick X drag  → smooth scrub (calls TeleportToRedshift each frame)
//   Right trigger press → snap to next Z_LADDER step
//   Left  trigger press → snap to previous Z_LADDER step
//
// Setup:
//   1. Create an empty GameObject at a comfortable VR position
//      (e.g., 1.8 m in front, 0.6 m below eye level, offset left).
//   2. Attach this script.
//   3. Assign _bubble (or leave null to auto-find).
//   4. Assign _barMaterial, _thumbMaterial, _notchMaterial.

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
        [SerializeField] private float  _barWidth    = 0.8f;
        [SerializeField] private float  _barHeight   = 0.006f;
        [SerializeField] private float  _notchHeight = 0.03f;
        [SerializeField] private float  _thumbSize   = 0.022f;

        [Header("Materials")]
        [SerializeField] private Material _barMaterial;    // unlit white
        [SerializeField] private Material _thumbMaterial;  // unlit accent (warm gold)
        [SerializeField] private Material _notchMaterial;  // unlit grey

        [Header("Navigation")]
        [SerializeField] private float _scrubDeadZone  = 0.15f;
        [SerializeField] private float _scrubSpeed     = 0.4f;   // log-z units/sec
        [SerializeField] private float _ladderCooldown = 0.6f;

        [Header("Colours")]
        [SerializeField] private Color _barColour    = new Color(0.7f,  0.75f, 0.85f);
        [SerializeField] private Color _thumbColour  = new Color(1.0f,  0.85f, 0.4f);
        [SerializeField] private Color _notchColour  = new Color(0.55f, 0.6f,  0.7f);
        [SerializeField] private Color _activeColour = new Color(1.0f,  0.95f, 0.6f); // active notch

        // -----------------------------------------------------------------------
        // Runtime objects
        // -----------------------------------------------------------------------

        private LineRenderer _barLine;
        private GameObject   _thumb;
        private GameObject[] _notches;
        private int          _currentLadderIndex  = 0;
        private float        _ladderCooldownTimer = 0f;

        // XR
        private InputDevice _leftController;
        private InputDevice _rightController;
        private bool        _devicesFound = false;

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
            _barLine.useWorldSpace  = false;
            _barLine.positionCount  = 2;
            _barLine.startWidth     = _barHeight;
            _barLine.endWidth       = _barHeight;
            _barLine.material       = _barMaterial;
            _barLine.startColor     = _barColour;
            _barLine.endColor       = _barColour;
            _barLine.SetPosition(0, new Vector3(-_barWidth * 0.5f, 0f, 0f));
            _barLine.SetPosition(1, new Vector3( _barWidth * 0.5f, 0f, 0f));

            // --- Notches (no labels — epoch name lives in CoordinateHUD) ---
            _notches = new GameObject[ladder.Length];

            for (int i = 0; i < ladder.Length; i++)
            {
                float t = NormLogZ(ladder[i]);
                float x = (t - 0.5f) * _barWidth;

                GameObject notchGO = new GameObject($"Notch_{i}");
                notchGO.transform.SetParent(transform, false);
                notchGO.transform.localPosition = new Vector3(x, 0f, 0f);

                LineRenderer lr = notchGO.AddComponent<LineRenderer>();
                lr.useWorldSpace  = false;
                lr.positionCount  = 2;
                lr.startWidth     = _barHeight * 0.5f;
                lr.endWidth       = _barHeight * 0.5f;
                lr.material       = _notchMaterial;
                lr.startColor     = _notchColour;
                lr.endColor       = _notchColour;
                lr.SetPosition(0, new Vector3(0f, -_notchHeight * 0.5f, 0f));
                lr.SetPosition(1, new Vector3(0f,  _notchHeight * 0.5f, 0f));

                _notches[i] = notchGO;
            }

            // --- Thumb ---
            _thumb = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _thumb.name = "EpochThumb";
            _thumb.transform.SetParent(transform, false);
            _thumb.transform.localScale = new Vector3(_thumbSize, _thumbSize * 2.5f, _thumbSize);

            Renderer thumbRend = _thumb.GetComponent<Renderer>();
            if (_thumbMaterial != null)
            {
                thumbRend.material       = _thumbMaterial;
                thumbRend.material.color = _thumbColour;
            }

            Destroy(_thumb.GetComponent<BoxCollider>());
        }

        // -----------------------------------------------------------------------
        // Thumb + notch highlight update
        // -----------------------------------------------------------------------

        private void UpdateThumb(OmegaZaTaCoordinate coord)
        {
            float t = NormLogZ(coord.RedshiftZ);
            float x = (t - 0.5f) * _barWidth;
            _thumb.transform.localPosition = new Vector3(x, 0f, 0f);

            int nearest = NearestLadderIndex(coord.RedshiftZ);

            for (int i = 0; i < _notches.Length; i++)
            {
                Color col = (i == nearest) ? _activeColour : _notchColour;
                LineRenderer lr = _notches[i].GetComponent<LineRenderer>();
                if (lr != null) { lr.startColor = col; lr.endColor = col; }
            }
        }

        // -----------------------------------------------------------------------
        // Event handler
        // -----------------------------------------------------------------------

        private void OnCoordinateChanged(OmegaZaTaCoordinate coord)
        {
            UpdateThumb(coord);
            _currentLadderIndex = NearestLadderIndex(coord.RedshiftZ);
        }

        // -----------------------------------------------------------------------
        // Input
        // -----------------------------------------------------------------------

        private void ReadInput()
        {
            if (_bubble == null) return;

            _rightController.TryGetFeatureValue(CommonUsages.primary2DAxis, out Vector2 rightStick);

            if (Mathf.Abs(rightStick.x) > _scrubDeadZone)
            {
                float currentLogZ = LogNorm(_bubble.Coordinate.RedshiftZ, 1f);
                float logMax      = LogNorm(CosmologicalConstants.Z_NEUTRINO_DECOUPLING, 1f);
                float newLogZ     = Mathf.Clamp(
                    currentLogZ + rightStick.x * _scrubSpeed * Time.deltaTime,
                    0f, logMax);
                float newZ = Mathf.Pow(10f, newLogZ) - 1f;
                _bubble.TeleportToRedshift(newZ);
            }

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
        // Log-z axis helpers (mirrors HolographicLayerController.NormLogZ)
        // -----------------------------------------------------------------------

        private static float NormLogZ(float z)
        {
            float logZ   = Mathf.Log10(Mathf.Max(z, 0f) + 1f);
            float logMax = Mathf.Log10(CosmologicalConstants.Z_NEUTRINO_DECOUPLING + 1f);
            return Mathf.Clamp01(logZ / logMax);
        }

        private static float LogNorm(float v, float offset)
            => Mathf.Log10(Mathf.Max(v, 0f) + offset);

        private static int NearestLadderIndex(float z)
        {
            float[] ladder = CosmologicalConstants.Z_LADDER;
            int   best     = 0;
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

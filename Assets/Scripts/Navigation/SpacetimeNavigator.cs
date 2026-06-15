// SpacetimeNavigator.cs
// Translates Quest 2 controller input into OmegaZaTaCoordinate changes
// via ObserverBubble.ApplyDelta() and TeleportToRedshift().
//
// Input mapping:
//   Left  stick X   → Az_outer rotation (pan around CMB sky)
//   Left  stick Y   → Za_outer depth (move toward/away from CMB)
//   Right stick X   → Az_inner rotation (pan within local LSS)
//   Right stick Y   → Ta advance (move along proper time axis, future trident)
//   Left  grip      → Decrease Ω (zoom out fidelity scale)
//   Right grip      → Increase Ω (zoom in fidelity scale)
//   Left  trigger   → Step down Z_LADDER (toward CMB)
//   Right trigger   → Step up  Z_LADDER (toward now)
//   A / X button    → Toggle coordinate HUD
//
// Design constraints:
//   • Smooth angular navigation (panning the sky feels natural)
//   • Discrete z stepping along Z_LADDER (physically meaningful epochs)
//   • No direct manipulation of Ta_proper (driven by ProperTimeTick)
//   • Dead zones applied to all analogue inputs

using UnityEngine;
using UnityEngine.XR;
using System.Collections.Generic;

namespace InfiniteImprobability.Navigation
{
    using Core;

    public class SpacetimeNavigator : MonoBehaviour
    {
        // -----------------------------------------------------------------------
        // Settings
        // -----------------------------------------------------------------------

        [Header("Angular Navigation")]
        [SerializeField, Tooltip("Azimuth pan speed (radians/sec)")]
        private float _azimuthSpeed = 0.8f;

        [SerializeField, Tooltip("Zenith depth speed (radians/sec)")]
        private float _zenithSpeed = 0.4f;

        [SerializeField, Tooltip("Analogue stick dead zone")]
        private float _deadZone = 0.15f;

        [Header("Z Ladder")]
        [SerializeField, Tooltip("Cooldown between redshift ladder steps (seconds)")]
        private float _ladderCooldown = 0.8f;

        [Header("Omega")]
        [SerializeField, Tooltip("Cooldown between Ω steps (seconds)")]
        private float _omegaCooldown = 0.5f;

        [Header("HUD")]
        [SerializeField] private GameObject _coordinateHUD;

        // -----------------------------------------------------------------------
        // Dependencies
        // -----------------------------------------------------------------------

        private ObserverBubble _bubble;

        // -----------------------------------------------------------------------
        // Runtime state
        // -----------------------------------------------------------------------

        private float _ladderCooldownTimer = 0f;
        private float _omegaCooldownTimer  = 0f;
        private int   _currentLadderIndex  = 0; // index into Z_LADDER, starts at z=0

        private bool _leftGripWasPressed  = false;
        private bool _rightGripWasPressed = false;
        private bool _hudToggleWasPressed = false;

        // XR device handles
        private InputDevice _leftController;
        private InputDevice _rightController;
        private bool        _devicesFound = false;

        // -----------------------------------------------------------------------
        // Unity lifecycle
        // -----------------------------------------------------------------------

        private void Awake()
        {
            _bubble = GetComponentInParent<ObserverBubble>();
            if (_bubble == null)
                _bubble = FindObjectOfType<ObserverBubble>();

            if (_bubble == null)
                Debug.LogError("[SpacetimeNavigator] No ObserverBubble found in scene.");
        }

        private void OnEnable()
        {
            InputDevices.deviceConnected    += OnDeviceConnected;
            InputDevices.deviceDisconnected += OnDeviceDisconnected;
            FindControllers();
        }

        private void OnDisable()
        {
            InputDevices.deviceConnected    -= OnDeviceConnected;
            InputDevices.deviceDisconnected -= OnDeviceDisconnected;
        }

        private void Update()
        {
            if (_bubble == null) return;

            _ladderCooldownTimer -= Time.deltaTime;
            _omegaCooldownTimer  -= Time.deltaTime;

            if (!_devicesFound) { FindControllers(); return; }

            ReadAnalogueInput();
            ReadDiscreteInput();
        }

        // -----------------------------------------------------------------------
        // Analogue input — smooth angular navigation
        // -----------------------------------------------------------------------

        private void ReadAnalogueInput()
        {
            Vector2 leftStick  = Vector2.zero;
            Vector2 rightStick = Vector2.zero;

            _leftController.TryGetFeatureValue(
                CommonUsages.primary2DAxis, out leftStick);
            _rightController.TryGetFeatureValue(
                CommonUsages.primary2DAxis, out rightStick);

            leftStick  = ApplyDeadZone(leftStick);
            rightStick = ApplyDeadZone(rightStick);

            float dt = Time.deltaTime;

            // Left stick: outer zenith (CMB sky)
            float dAz_outer = leftStick.x  * _azimuthSpeed * dt;
            float dZa_outer = -leftStick.y  * _zenithSpeed  * dt; // push up = toward CMB

            // Right stick: inner zenith (local LSS)
            float dAz_inner = rightStick.x * _azimuthSpeed * dt;
            float dZa_inner = -rightStick.y * _zenithSpeed  * dt;

            if (dAz_outer != 0f || dZa_outer != 0f ||
                dAz_inner != 0f || dZa_inner != 0f)
            {
                _bubble.ApplyDelta(
                    dZa_outer: dZa_outer,
                    dAz_outer: dAz_outer,
                    dZa_inner: dZa_inner,
                    dAz_inner: dAz_inner);
            }
        }

        // -----------------------------------------------------------------------
        // Discrete input — Z ladder, Ω steps, HUD toggle
        // -----------------------------------------------------------------------

        private void ReadDiscreteInput()
        {
            // --- Z Ladder (triggers) ---
            _leftController.TryGetFeatureValue(
                CommonUsages.triggerButton, out bool leftTrigger);
            _rightController.TryGetFeatureValue(
                CommonUsages.triggerButton, out bool rightTrigger);

            if (_ladderCooldownTimer <= 0f)
            {
                if (leftTrigger)  StepLadder(-1); // toward CMB
                if (rightTrigger) StepLadder(+1); // toward now
            }

            // --- Ω steps (grips) ---
            _leftController.TryGetFeatureValue(
                CommonUsages.gripButton, out bool leftGrip);
            _rightController.TryGetFeatureValue(
                CommonUsages.gripButton, out bool rightGrip);

            if (_omegaCooldownTimer <= 0f)
            {
                if (leftGrip  && !_leftGripWasPressed)  { _bubble.ApplyDelta(dOmega: -1); _omegaCooldownTimer = _omegaCooldown; }
                if (rightGrip && !_rightGripWasPressed) { _bubble.ApplyDelta(dOmega: +1); _omegaCooldownTimer = _omegaCooldown; }
            }
            _leftGripWasPressed  = leftGrip;
            _rightGripWasPressed = rightGrip;

            // --- HUD toggle (primary button: A on right, X on left) ---
            _rightController.TryGetFeatureValue(
                CommonUsages.primaryButton, out bool hudButton);

            if (hudButton && !_hudToggleWasPressed && _coordinateHUD != null)
                _coordinateHUD.SetActive(!_coordinateHUD.activeSelf);

            _hudToggleWasPressed = hudButton;
        }

        // -----------------------------------------------------------------------
        // Z Ladder stepping
        // -----------------------------------------------------------------------

        private void StepLadder(int direction)
        {
            float[] ladder = CosmologicalConstants.Z_LADDER;
            _currentLadderIndex = Mathf.Clamp(
                _currentLadderIndex + direction, 0, ladder.Length - 1);

            _bubble.TeleportToRedshift(ladder[_currentLadderIndex]);
            _ladderCooldownTimer = _ladderCooldown;

            Debug.Log($"[Navigator] Z Ladder step → z={ladder[_currentLadderIndex]:F1}");
        }

        // -----------------------------------------------------------------------
        // XR device management
        // -----------------------------------------------------------------------

        private void FindControllers()
        {
            var leftDevices  = new List<InputDevice>();
            var rightDevices = new List<InputDevice>();

            InputDevices.GetDevicesWithCharacteristics(
                InputDeviceCharacteristics.Left  | InputDeviceCharacteristics.Controller,
                leftDevices);
            InputDevices.GetDevicesWithCharacteristics(
                InputDeviceCharacteristics.Right | InputDeviceCharacteristics.Controller,
                rightDevices);

            if (leftDevices.Count  > 0) _leftController  = leftDevices[0];
            if (rightDevices.Count > 0) _rightController = rightDevices[0];

            _devicesFound = leftDevices.Count > 0 && rightDevices.Count > 0;
        }

        private void OnDeviceConnected(InputDevice device)    => FindControllers();
        private void OnDeviceDisconnected(InputDevice device) => FindControllers();

        // -----------------------------------------------------------------------
        // Helpers
        // -----------------------------------------------------------------------

        private Vector2 ApplyDeadZone(Vector2 v)
        {
            return v.magnitude < _deadZone ? Vector2.zero : v;
        }
    }
}

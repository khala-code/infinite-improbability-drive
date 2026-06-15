// ProperTimeTick.cs
// The quantum proper-time rotor clock.
//
// Each observer has an independent rotor that ticks once per Planck second.
// There is no universal synchronisation — phases are independent.
// Speed of light c is the hard limit that prevents superluminal phase alignment.
//
// Each tick:
//   1. Advances the rotor phase by its personal increment
//   2. Computes energy injection proportional to alignment (S·û)
//   3. Fires the OnTick event for BlochEvolver and TimeTaxComputer to consume

using System;
using UnityEngine;

namespace InfiniteImprobability.Core
{
    public class ProperTimeTick : MonoBehaviour
    {
        // -----------------------------------------------------------------------
        // Rotor state
        // -----------------------------------------------------------------------

        /// <summary>Independent phase of this observer's rotor (radians)</summary>
        [SerializeField, Range(0f, Mathf.PI * 2f)]
        private float _rotorPhase = 0f;

        /// <summary>Phase increment per Unity FixedUpdate step (normalised to Planck time)</summary>
        [SerializeField]
        private float _phaseIncrement = 0.1f;

        /// <summary>Current spin vector S on the Bloch sphere (unit vector)</summary>
        private Vector3 _spinVector = Vector3.up;

        /// <summary>Coherence axis û — bisector of Za_inner and Za_outer directions</summary>
        private Vector3 _coherenceAxis = Vector3.forward;

        // -----------------------------------------------------------------------
        // Energy tracking
        // -----------------------------------------------------------------------

        /// <summary>Energy accumulated this tick from alignment (S·û)</summary>
        public float LastTickEnergy { get; private set; }

        /// <summary>Total accumulated energy since session start</summary>
        public float TotalEnergy { get; private set; }

        // -----------------------------------------------------------------------
        // Events
        // -----------------------------------------------------------------------

        /// <summary>Fired every proper-time tick. Carries alignment score and energy.</summary>
        public event Action<float, float> OnTick; // (alignment, energy)

        // -----------------------------------------------------------------------
        // Public API
        // -----------------------------------------------------------------------

        /// <summary>Current spin vector on the Bloch sphere</summary>
        public Vector3 SpinVector => _spinVector;

        /// <summary>Current rotor phase (radians)</summary>
        public float RotorPhase => _rotorPhase;

        /// <summary>
        /// Set the coherence axis û from the observer bubble's current
        /// double-zenith configuration. Called by ObserverBubble when
        /// coordinates change.
        /// </summary>
        public void SetCoherenceAxis(Vector3 axis)
        {
            _coherenceAxis = axis.normalized;
        }

        /// <summary>Inject an updated spin vector from BlochEvolver</summary>
        public void SetSpinVector(Vector3 spin)
        {
            _spinVector = spin.normalized;
        }

        // -----------------------------------------------------------------------
        // Unity lifecycle
        // -----------------------------------------------------------------------

        private void FixedUpdate()
        {
            Tick();
        }

        // -----------------------------------------------------------------------
        // Internal tick
        // -----------------------------------------------------------------------

        private void Tick()
        {
            // Advance independent rotor phase — no universal sync
            _rotorPhase = (_rotorPhase + _phaseIncrement) % (Mathf.PI * 2f);

            // Alignment score: projection of spin onto coherence axis
            float alignment = Vector3.Dot(_spinVector, _coherenceAxis); // [-1, 1]

            // Energy injected this tick — proportional to positive alignment
            // Zero or negative alignment → no energy injection
            float energy = Mathf.Max(0f, alignment)
                           * CosmologicalConstants.E_TICK_MAX;

            LastTickEnergy = energy;
            TotalEnergy   += energy;

            OnTick?.Invoke(alignment, energy);
        }
    }
}

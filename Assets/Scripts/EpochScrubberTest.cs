// EpochScrubberTest.cs
// Test-scene-only component. Drives HolographicParticleLayer epoch and xi
// without any XR or OVR dependency. Tag the parent GameObject EditorOnly
// so it is stripped from Quest builds automatically.
//
// Inspector controls
// ------------------
//  SweepSpeed      -- how fast NormalisedEpoch oscillates (cycles per second)
//  XiPulseSpeed    -- how fast XiCoherence oscillates
//  Paused          -- freeze epoch/xi at current values
//  ManualEpoch     -- override epoch to a fixed value when Paused = true
//  ManualXi        -- override xi to a fixed value when Paused = true
//
// Execution order note
// --------------------
// Values are pushed in LateUpdate so they are always written AFTER
// HolographicParticleLayer.Update() has finished its Dispatch call.
// This means the compute shader sees the scrubber's values on the
// NEXT frame -- one frame of latency, acceptable for a test harness.
// For production, EpochScrubber should be set earlier in Script
// Execution Order (Project Settings > Script Execution Order) and
// push values in Update, with HPL reading them in LateUpdate.

using UnityEngine;

namespace InfiniteImprobability.CMB
{
    [AddComponentMenu("CMB/Epoch Scrubber Test")]
    public class EpochScrubberTest : MonoBehaviour
    {
        [Header("Target")]
        public HolographicParticleLayer ParticleLayer;

        [Header("Sweep")]
        [Range(0.01f, 1f)]
        [Tooltip("Epoch oscillation speed (cycles per second).")]
        public float SweepSpeed = 0.05f;

        [Range(0.01f, 1f)]
        [Tooltip("Xi coherence oscillation speed (cycles per second).")]
        public float XiPulseSpeed = 0.08f;

        [Header("Manual Override")]
        public bool Paused = false;

        [Range(0f, 1f)]
        public float ManualEpoch = 0.5f;

        [Range(0f, 1f)]
        public float ManualXi = 0.5f;

        // Readout -- visible in Inspector during Play mode
        [Header("Readout (read-only)")]
        [SerializeField] private float _currentEpoch;
        [SerializeField] private float _currentXi;

        private void Awake()
        {
            // Resolve sibling reference at runtime rather than relying on
            // Reset() which only fires once when the component is first added.
            if (ParticleLayer == null)
                ParticleLayer = GetComponent<HolographicParticleLayer>();

            if (ParticleLayer == null)
                Debug.LogError("[EpochScrubberTest] No HolographicParticleLayer found on this GameObject. "
                             + "Assign it manually in the Inspector.");
        }

        // LateUpdate: runs after all Update() calls in the same frame.
        // HPL.Update() dispatches the compute shader in Update(), so writing
        // here guarantees the GPU sees fresh values on the next Dispatch.
        private void LateUpdate()
        {
            if (ParticleLayer == null) return;

            float epoch, xi;

            if (Paused)
            {
                epoch = ManualEpoch;
                xi    = ManualXi;
            }
            else
            {
                // PingPong: 0 -> 1 -> 0 at SweepSpeed cycles/sec
                epoch = Mathf.PingPong(Time.time * SweepSpeed, 1f);
                // Offset xi phase by half cycle so they don't peak together
                xi    = Mathf.PingPong(Time.time * XiPulseSpeed + 0.5f, 1f);
            }

            ParticleLayer.SetEpoch(epoch);
            ParticleLayer.SetXiCoherence(xi);

            _currentEpoch = epoch;
            _currentXi    = xi;
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            UnityEditor.Handles.Label(
                transform.position + Vector3.up * 0.3f,
                $"epoch={_currentEpoch:F2}  xi={_currentXi:F2}"
            );
        }
#endif
    }
}

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

        private void Reset()
        {
            // Auto-find sibling component on the same GameObject if not assigned
            ParticleLayer = GetComponent<HolographicParticleLayer>();
        }

        private void Update()
        {
            if (ParticleLayer == null)
            {
                Debug.LogWarning("[EpochScrubberTest] ParticleLayer not assigned.");
                return;
            }

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
        // Draw a simple gizmo label in Scene view so you can see live values
        // without keeping the Inspector open.
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

// CausalFieldBridge.cs
// Pipes CausalFieldEngine state into a Unity VFX Graph each frame.
//
// Setup (Scene):
//   1. Add CausalFieldBridge to the same GameObject as CausalFieldEngine.
//   2. Assign the VisualEffect component in the Inspector (_vfx field).
//   3. In the VFX Graph, create a GraphicsBuffer property named "_CausalNodes"
//      with stride 16 (float4) and capacity = resolution^3.
//   4. Create three float exposed properties: _HeegnerIntensity,
//      _BifurcationPulse, _CompositeFlow.
//
// Buffer layout per node (float4):
//   x = |psi|^2       — wave intensity, drives particle spawn rate
//   y = VoidDensity   — local void, drives particle scale / opacity
//   z = XiCoherence   — local xi scalar, drives particle speed
//   w = NodeClass     — 0=BULK, 1=BIFURCATED, 2=HEEGNER_LOCKED
//
// Colour convention (set in VFX Graph output node, driven by NodeClass):
//   BULK          — deep blue,  HDR (0.1, 0.2, 0.8, 1)  dim ambient drift
//   BIFURCATED    — amber,      HDR (1.0, 0.5, 0.0, 1)  pulsing choice window
//   HEEGNER_LOCKED — white flare, HDR (2.0, 2.0, 2.0, 1)  overwhelm flash

using UnityEngine;
using UnityEngine.VFX;

namespace InfiniteImprobability.Core
{
    [RequireComponent(typeof(CausalFieldEngine))]
    public class CausalFieldBridge : MonoBehaviour
    {
        // ── Inspector ─────────────────────────────────────────────────────────
        [Header("VFX Target")]
        [SerializeField] VisualEffect _vfx;

        [Header("Omega Event Intensities")]
        [Tooltip("Peak _HeegnerIntensity value pushed to VFX on Heegner crossing.")]
        [SerializeField] float _heegnerPeak     = 3.0f;
        [Tooltip("Peak _BifurcationPulse value pushed to VFX on prime crossing.")]
        [SerializeField] float _bifurcationPeak = 1.5f;
        [Tooltip("_CompositeFlow value pushed to VFX on composite Omega crossing.")]
        [SerializeField] float _compositeFlow   = 0.4f;
        [Tooltip("Seconds for Omega event intensities to decay back to baseline.")]
        [SerializeField] float _decayTime       = 2.0f;

        // ── VFX Graph property name IDs ────────────────────────────────────────
        // Cached at startup — cheaper than hashing strings every frame.
        static readonly int ID_CausalNodes      = Shader.PropertyToID("_CausalNodes");
        static readonly int ID_HeegnerIntensity = Shader.PropertyToID("_HeegnerIntensity");
        static readonly int ID_BifurcationPulse = Shader.PropertyToID("_BifurcationPulse");
        static readonly int ID_CompositeFlow    = Shader.PropertyToID("_CompositeFlow");

        // ── Private state ─────────────────────────────────────────────────────
        CausalFieldEngine _engine;
        GraphicsBuffer    _vfxBuffer;   // float4 per node, written CPU-side each frame
        float[]           _bufferData;  // staging array — avoids per-frame allocation

        int   _nodeCount;
        float _heegnerCurrent;
        float _bifurcationCurrent;
        float _compositeCurrent;

        // ── Lifecycle ─────────────────────────────────────────────────────────
        void Awake()
        {
            _engine = GetComponent<CausalFieldEngine>();
        }

        void OnEnable()
        {
            // Derive node count from the engine's resolution (cube)
            int res  = _engine.Resolution;
            _nodeCount = res * res * res;

            // float4 per node — 4 floats, stride 16 bytes
            _vfxBuffer  = new GraphicsBuffer(
                GraphicsBuffer.Target.Structured,
                _nodeCount,
                sizeof(float) * 4);

            _bufferData = new float[_nodeCount * 4];

            // Push buffer reference to VFX Graph once
            if (_vfx != null)
                _vfx.SetGraphicsBuffer(ID_CausalNodes, _vfxBuffer);

            // Subscribe to Omega crossing events
            _engine.OnHeegnerCrossing.AddListener(OnHeegner);
            _engine.OnPrimeCrossing.AddListener(OnPrime);
            _engine.OnCompositeCrossing.AddListener(OnComposite);
        }

        void OnDisable()
        {
            _engine.OnHeegnerCrossing.RemoveListener(OnHeegner);
            _engine.OnPrimeCrossing.RemoveListener(OnPrime);
            _engine.OnCompositeCrossing.RemoveListener(OnComposite);

            _vfxBuffer?.Release();
            _vfxBuffer  = null;
            _bufferData = null;
        }

        void Update()
        {
            WriteBuffer();
            DecayIntensities();
            PushIntensities();
        }

        // ── Buffer write ──────────────────────────────────────────────────────
        /// <summary>
        /// Sample every node from the engine and pack into the staging array,
        /// then upload to the GraphicsBuffer in one SetData call.
        /// </summary>
        void WriteBuffer()
        {
            int res = _engine.Resolution;

            for (int z = 0; z < res; z++)
            for (int y = 0; y < res; y++)
            for (int x = 0; x < res; x++)
            {
                Vector3    worldPos = CausalOctree.ToWorld(x, y, z,
                                        _engine.BubbleScale, res);
                CausalNode node    = _engine.GetNode(worldPos);

                int i = CausalOctree.Index(x, y, z, res) * 4;

                // x: |psi|^2
                _bufferData[i + 0] = node.Psi.sqrMagnitude;
                // y: void density
                _bufferData[i + 1] = node.VoidDensity;
                // z: xi coherence (scalar until tensor promotion)
                _bufferData[i + 2] = node.Xi;
                // w: node class — encoded from xi.w sign convention
                //    BULK=0, BIFURCATED=1, HEEGNER_LOCKED=2
                _bufferData[i + 3] = EncodeClass(node);
            }

            _vfxBuffer.SetData(_bufferData);
        }

        /// <summary>
        /// Encode node classification as a float for VFX Graph sampling.
        /// Matches the xi.w sign convention from ClassifyNodes kernel:
        ///   xi.w > 0  → BULK (0)
        ///   xi.w = 0  → BIFURCATED (1)
        ///   xi.w < 0  → HEEGNER_LOCKED (2)
        /// </summary>
        static float EncodeClass(CausalNode node)
        {
            if (node.IsHeegnerLocked())  return 2f;
            if (node.IsBifurcated())     return 1f;
            return 0f;
        }

        // ── Omega intensity decay ─────────────────────────────────────────────
        void DecayIntensities()
        {
            float dt   = Time.deltaTime;
            float decay = dt / Mathf.Max(_decayTime, 0.001f);

            _heegnerCurrent     = Mathf.Max(0f, _heegnerCurrent     - _heegnerPeak     * decay);
            _bifurcationCurrent = Mathf.Max(0f, _bifurcationCurrent - _bifurcationPeak * decay);
            _compositeCurrent   = Mathf.Max(0f, _compositeCurrent   - _compositeFlow   * decay);
        }

        void PushIntensities()
        {
            if (_vfx == null) return;
            _vfx.SetFloat(ID_HeegnerIntensity, _heegnerCurrent);
            _vfx.SetFloat(ID_BifurcationPulse, _bifurcationCurrent);
            _vfx.SetFloat(ID_CompositeFlow,    _compositeCurrent);
        }

        // ── Omega event handlers ──────────────────────────────────────────────
        void OnHeegner(float omega)
        {
            // Full peak — overwhelming constructive interference
            _heegnerCurrent = _heegnerPeak;
        }

        void OnPrime(float omega)
        {
            // Bifurcation pulse — choice window opens
            _bifurcationCurrent = _bifurcationPeak;
        }

        void OnComposite(float omega)
        {
            // Soft ambient flow tick
            _compositeCurrent = _compositeFlow;
        }
    }
}

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
//   5. Create two GPU Event Spawn contexts named "OnBifurcation" (200 particles)
//      and "OnHeegner" (2000 particles). These are triggered by SendEvent below.
//
// Buffer layout per node (float4):
//   x = |psi|^2       — wave intensity, drives particle spawn rate
//   y = VoidDensity   — local void, drives particle scale / opacity
//   z = XiCoherence   — local xi scalar, drives particle speed
//   w = NodeClass     — 0=BULK, 1=BIFURCATED, 2=HEEGNER_LOCKED
//
// Colour convention (set in VFX Graph output node, driven by NodeClass):
//   BULK           — deep blue,  HDR (0.1, 0.2, 0.8)  dim ambient drift
//   BIFURCATED     — amber,      HDR (1.0, 0.5, 0.0)  pulsing choice window
//   HEEGNER_LOCKED — white flare, HDR (2.0, 2.0, 2.0)  overwhelm flash

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

        // ── VFX Graph property + event name IDs ───────────────────────────────
        // Cached at startup — cheaper than hashing strings every frame.
        static readonly int ID_CausalNodes      = Shader.PropertyToID("_CausalNodes");
        static readonly int ID_HeegnerIntensity = Shader.PropertyToID("_HeegnerIntensity");
        static readonly int ID_BifurcationPulse = Shader.PropertyToID("_BifurcationPulse");
        static readonly int ID_CompositeFlow    = Shader.PropertyToID("_CompositeFlow");

        // Event names must match the GPU Event Spawn context names in the VFX Graph.
        static readonly int ID_OnBifurcation    = Shader.PropertyToID("OnBifurcation");
        static readonly int ID_OnHeegner        = Shader.PropertyToID("OnHeegner");

        // ── Private state ─────────────────────────────────────────────────────
        CausalFieldEngine _engine;
        GraphicsBuffer    _vfxBuffer;
        float[]           _bufferData;

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
            int res    = _engine.Resolution;
            _nodeCount = res * res * res;

            _vfxBuffer  = new GraphicsBuffer(
                GraphicsBuffer.Target.Structured,
                _nodeCount,
                sizeof(float) * 4);

            _bufferData = new float[_nodeCount * 4];

            if (_vfx != null)
            {
                _vfx.SetGraphicsBuffer(ID_CausalNodes, _vfxBuffer);
                _vfx.SetInt(Shader.PropertyToID("_Resolution"), _engine.Resolution);
                _vfx.SetFloat(Shader.PropertyToID("_BubbleScale"), _engine.BubbleScale);
            }

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

                _bufferData[i + 0] = node.Psi.sqrMagnitude;
                _bufferData[i + 1] = node.VoidDensity;
                _bufferData[i + 2] = node.Xi;
                _bufferData[i + 3] = EncodeClass(node);
            }

            _vfxBuffer.SetData(_bufferData);
        }

        static float EncodeClass(CausalNode node)
        {
            if (node.IsHeegnerLocked()) return 2f;
            if (node.IsBifurcated())    return 1f;
            return 0f;
        }

        // ── Omega intensity decay ─────────────────────────────────────────────
        void DecayIntensities()
        {
            float decay = Time.deltaTime / Mathf.Max(_decayTime, 0.001f);
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

        // ── Omega event handlers ─────────────────────────────────────────────
        void OnHeegner(float omega)
        {
            _heegnerCurrent = _heegnerPeak;
            // Fire named event — triggers the 2000-particle GPU Event Spawn context.
            // The event name in the VFX Graph must be "OnHeegner" (exact match).
            _vfx?.SendEvent(ID_OnHeegner);
        }

        void OnPrime(float omega)
        {
            _bifurcationCurrent = _bifurcationPeak;
            // Fire named event — triggers the 200-particle GPU Event Spawn context.
            // The event name in the VFX Graph must be "OnBifurcation" (exact match).
            _vfx?.SendEvent(ID_OnBifurcation);
        }

        void OnComposite(float omega)
        {
            // Composite crossings only update the continuous flow float —
            // no burst event needed, the Constant Rate context handles ambient.
            _compositeCurrent = _compositeFlow;
        }
    }
}

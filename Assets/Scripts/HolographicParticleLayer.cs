// HolographicParticleLayer.cs
// CMB particle pipeline - Unity C# layer.
//
// Loads particle_buffer.bin (written by generate_particle_buffer.py) into a
// ComputeBuffer and drives a ComputeShader each frame to update positions,
// velocities, and colours.  Renders via Graphics.DrawProceduralIndirect so
// the GPU never roundtrips to the CPU after initialisation.
//
// Coupling points
// ---------------
// EpochScrubber      -- sets NormalisedEpoch (0=high-z, 1=present).
//                       Heegner Omega crossings fire HeegnerFlash event.
// ObserverBubbleRenderer -- sets XiCoherence scalar (0-1).
//                       High xi = soliton brightens, void dims.
// Shader globals     -- _ParityAsymmetry, _HeegnerFraction, _VoidFraction,
//                       _SolitonFraction injected from field_scalars.json so
//                       all boundary shaders share the same competition state.
//
// Quest 2 budget note
// -------------------
// DrawProceduralIndirect + ComputeShader is the correct path for Quest 2.
// Unity's built-in ParticleSystem has a 65k hard cap and CPU overhead;
// this path keeps everything on GPU after the one-time buffer upload.
// Target: <= 0.8ms GPU time per eye at 72Hz.  Profile with OVR Metrics Tool.
//
// Coordinate frame
// ----------------
// particle_buffer.bin positions are in Galactic Cartesian coordinates
// (right-handed, X toward GC, Z toward NGP).  _GalToUnity must be set to
// the rotation matrix that maps this frame to Unity world space.  Pin this
// to the ZaTaOa frame from epoch_frame.json before first use -- see
// Docs/holographic-projection.md, "Open Design Questions".

using System;
using System.IO;
using System.Runtime.InteropServices;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace InfiniteImprobability.CMB
{
    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    public struct ParticleRecord
    {
        public Vector3 Position;
        public Vector3 Velocity;
        public Vector4 Colour;
        public float   HeegnerPower;
        public float   VoidPressure;
        public float   SolitonDensity;
        public float   Kappa;
        public int     ParticleClass;
        public float   ParityWeight;
        public float _p0, _p1, _p2, _p3, _p4, _p5, _p6, _p7;
    }

    [Serializable]
    public class FieldScalars
    {
        public float parity_asymmetry;
        public float heegner_fraction;
        public float void_fraction;
        public float soliton_fraction;
        public float xi_baseline;
    }

    [AddComponentMenu("CMB/Holographic Particle Layer")]
    public class HolographicParticleLayer : MonoBehaviour
    {
        // ----------------------------------------------------------------
        // Inspector
        // ----------------------------------------------------------------

        [Header("Pipeline Assets")]
        public string ParticleBufferFilename = "particle_buffer.bin";
        public ComputeShader ParticleUpdateShader;
        public Material ParticleMaterial;

        [Header("Coupling")]
        [Range(0f, 1f)] public float NormalisedEpoch = 1f;
        [Range(0f, 1f)] public float XiCoherence     = 0.5f;
        public Matrix4x4 GalToUnity = Matrix4x4.identity;

        [Header("Visual Tuning")]
        [Range(0.001f, 0.05f)] public float ParticleSize      = 0.008f;
        [Range(0f, 2f)]        public float SolitonBrightness = 1f;
        [Range(0f, 2f)]        public float VoidBrightness    = 0.4f;
        [Range(0f, 2f)]        public float HeegnerBrightness = 1.8f;

        [Header("Events")]
        public UnityEngine.Events.UnityEvent HeegnerFlash;

        // ----------------------------------------------------------------
        // Private state
        // ----------------------------------------------------------------

        private ComputeBuffer _particleBuffer;
        private ComputeBuffer _argsBuffer;
        private ComputeBuffer _anchorBuffer;      // _HeegnerAnchors in compute shader
        private int           _particleCount;
        private int           _anchorCount;       // 0 until anchor_positions.bin exists
        private int           _kernelUpdate;
        private FieldScalars  _fieldScalars;
        private float         _lastEpoch;

        private static readonly float[] HeegnerOmegaThresholds = { 0.15f, 0.38f, 0.61f, 0.84f };

        // Cached property IDs
        private static readonly int ID_ParticleBuffer    = Shader.PropertyToID("_ParticleBuffer");
        private static readonly int ID_HeegnerAnchors    = Shader.PropertyToID("_HeegnerAnchors");
        private static readonly int ID_HeegnerAnchorCount= Shader.PropertyToID("_HeegnerAnchorCount");
        private static readonly int ID_ParticleCount     = Shader.PropertyToID("_ParticleCount");
        private static readonly int ID_DeltaTime         = Shader.PropertyToID("_DeltaTime");
        private static readonly int ID_NormEpoch         = Shader.PropertyToID("_NormEpoch");
        private static readonly int ID_XiCoherence       = Shader.PropertyToID("_XiCoherence");
        private static readonly int ID_GalToUnity        = Shader.PropertyToID("_GalToUnity");
        private static readonly int ID_ParticleSize      = Shader.PropertyToID("_ParticleSize");
        private static readonly int ID_SolitonBrightness = Shader.PropertyToID("_SolitonBrightness");
        private static readonly int ID_VoidBrightness    = Shader.PropertyToID("_VoidBrightness");
        private static readonly int ID_HeegnerBrightness = Shader.PropertyToID("_HeegnerBrightness");
        private static readonly int ID_ParityAsymmetry   = Shader.PropertyToID("_ParityAsymmetry");
        private static readonly int ID_HeegnerFraction   = Shader.PropertyToID("_HeegnerFraction");
        private static readonly int ID_VoidFraction      = Shader.PropertyToID("_VoidFraction");
        private static readonly int ID_SolitonFraction   = Shader.PropertyToID("_SolitonFraction");
        private static readonly int ID_EpochVoidBoost    = Shader.PropertyToID("_EpochVoidBoost");
        private static readonly int ID_EpochSolitonBoost = Shader.PropertyToID("_EpochSolitonBoost");
        private static readonly int ID_XiSolitonMod      = Shader.PropertyToID("_XiSolitonMod");
        private static readonly int ID_XiVoidMod         = Shader.PropertyToID("_XiVoidMod");

        // ----------------------------------------------------------------
        // Lifecycle
        // ----------------------------------------------------------------

        private void Awake()
        {
            LoadFieldScalars();
            LoadParticleBuffer();
            LoadAnchorBuffer();      // must come after LoadParticleBuffer
            InitArgsBuffer();
            CacheShaderKernel();
        }

        private void OnDestroy()
        {
            _particleBuffer?.Release();
            _argsBuffer?.Release();
            _anchorBuffer?.Release();
        }

        private void Update()
        {
            CheckHeegnerCrossings();
            DispatchComputeUpdate();
            DrawParticles();
            _lastEpoch = NormalisedEpoch;
        }

        // ----------------------------------------------------------------
        // Initialisation
        // ----------------------------------------------------------------

        private void LoadParticleBuffer()
        {
            string path = Path.Combine(Application.streamingAssetsPath, "CMB", ParticleBufferFilename);

#if UNITY_ANDROID && !UNITY_EDITOR
            // Quest 2: StreamingAssets are inside the .apk.
            // TODO: implement StreamingAssetsBootstrap.cs for first-boot copy.
            path = Path.Combine(Application.persistentDataPath, "CMB", ParticleBufferFilename);
#endif

            if (!File.Exists(path))
            {
                Debug.LogError($"[HPL] particle_buffer.bin not found at: {path}");
                return;
            }

            byte[] raw    = File.ReadAllBytes(path);
            int    stride = Marshal.SizeOf<ParticleRecord>();
            _particleCount = raw.Length / stride;

            if (_particleCount == 0) { Debug.LogError("[HPL] particle_buffer.bin is empty."); return; }
            if (stride != 96) Debug.LogWarning($"[HPL] stride={stride}, expected 96.");

            Debug.Log($"[HPL] Loading {_particleCount} particles ({raw.Length / 1024f / 1024f:F1} MB)  stride={stride}B");

            int floatsPerRecord = stride / sizeof(float);
            float[] floatData   = new float[_particleCount * floatsPerRecord];
            Buffer.BlockCopy(raw, 0, floatData, 0, raw.Length);

            _particleBuffer = new ComputeBuffer(_particleCount, stride, ComputeBufferType.Structured);
            _particleBuffer.SetData(floatData);
        }

        private void LoadAnchorBuffer()
        {
            // anchor_positions.bin: packed float32 triples (x,y,z) on unit sphere,
            // written by generate_particle_buffer.py once the Heegner node list is
            // finalised.  Until that file exists we allocate a 1-element dummy buffer
            // so Unity does not throw "property not set" on every Dispatch call.
            // The compute shader checks _HeegnerAnchorCount and falls back to the
            // north galactic pole (0,0,1) when count == 0.

            string path = Path.Combine(Application.streamingAssetsPath, "CMB", "anchor_positions.bin");

#if UNITY_ANDROID && !UNITY_EDITOR
            path = Path.Combine(Application.persistentDataPath, "CMB", "anchor_positions.bin");
#endif

            if (File.Exists(path))
            {
                byte[] raw    = File.ReadAllBytes(path);
                int    stride = sizeof(float) * 3;          // 12 bytes per Vector3
                _anchorCount  = raw.Length / stride;

                float[] floatData = new float[_anchorCount * 3];
                Buffer.BlockCopy(raw, 0, floatData, 0, raw.Length);

                _anchorBuffer = new ComputeBuffer(_anchorCount, stride, ComputeBufferType.Structured);
                _anchorBuffer.SetData(floatData);

                Debug.Log($"[HPL] Loaded {_anchorCount} Heegner anchors.");
            }
            else
            {
                // Dummy 1-element buffer -- satisfies the slot binding requirement.
                // Particles fall back to NGP convergence until real anchors are loaded.
                _anchorCount  = 0;
                _anchorBuffer = new ComputeBuffer(1, sizeof(float) * 3, ComputeBufferType.Structured);
                _anchorBuffer.SetData(new float[] { 0f, 0f, 1f });  // NGP

                Debug.Log("[HPL] anchor_positions.bin not found; using NGP fallback anchor.");
            }
        }

        private void InitArgsBuffer()
        {
            uint[] args = new uint[4] { 1u, (uint)_particleCount, 0u, 0u };
            _argsBuffer = new ComputeBuffer(4, sizeof(uint), ComputeBufferType.IndirectArguments);
            _argsBuffer.SetData(args);
        }

        private void CacheShaderKernel()
        {
            if (ParticleUpdateShader == null) { Debug.LogWarning("[HPL] No ComputeShader assigned."); return; }
            _kernelUpdate = ParticleUpdateShader.FindKernel("UpdateParticles");
        }

        private void LoadFieldScalars()
        {
            string path = Path.Combine(Application.streamingAssetsPath, "CMB", "field_scalars.json");
            if (!File.Exists(path))
            {
                Debug.LogWarning("[HPL] field_scalars.json not found; using defaults.");
                _fieldScalars = new FieldScalars
                {
                    parity_asymmetry = 1f, heegner_fraction = 0.1f,
                    void_fraction    = 0.55f, soliton_fraction = 0.35f,
                    xi_baseline      = 0.5f
                };
            }
            else
            {
                _fieldScalars = JsonUtility.FromJson<FieldScalars>(File.ReadAllText(path));
            }

            Shader.SetGlobalFloat(ID_ParityAsymmetry, _fieldScalars.parity_asymmetry);
            Shader.SetGlobalFloat(ID_HeegnerFraction, _fieldScalars.heegner_fraction);
            Shader.SetGlobalFloat(ID_VoidFraction,    _fieldScalars.void_fraction);
            Shader.SetGlobalFloat(ID_SolitonFraction, _fieldScalars.soliton_fraction);

            Debug.Log($"[HPL] FieldScalars: parity={_fieldScalars.parity_asymmetry:F3} "
                    + $"h={_fieldScalars.heegner_fraction:F3} "
                    + $"v={_fieldScalars.void_fraction:F3} "
                    + $"s={_fieldScalars.soliton_fraction:F3}");
        }

        // ----------------------------------------------------------------
        // Per-frame
        // ----------------------------------------------------------------

        private void DispatchComputeUpdate()
        {
            if (ParticleUpdateShader == null || _particleBuffer == null || _anchorBuffer == null) return;

            float epochVoidBoost    = Mathf.Lerp(1.8f, 1.0f, NormalisedEpoch);
            float epochSolitonBoost = Mathf.Lerp(0.3f, 1.0f, NormalisedEpoch);
            float xiSolitonMod      = Mathf.Lerp(0.5f, 1.5f, XiCoherence);
            float xiVoidMod         = Mathf.Lerp(1.5f, 0.5f, XiCoherence);

            ParticleUpdateShader.SetBuffer(_kernelUpdate, ID_ParticleBuffer,     _particleBuffer);
            ParticleUpdateShader.SetBuffer(_kernelUpdate, ID_HeegnerAnchors,     _anchorBuffer);
            ParticleUpdateShader.SetInt(   ID_HeegnerAnchorCount,                _anchorCount);
            ParticleUpdateShader.SetInt(   ID_ParticleCount,                     _particleCount);
            ParticleUpdateShader.SetFloat( ID_DeltaTime,                         Time.deltaTime);
            ParticleUpdateShader.SetFloat( ID_NormEpoch,                         NormalisedEpoch);
            ParticleUpdateShader.SetFloat( ID_XiCoherence,                       XiCoherence);
            ParticleUpdateShader.SetMatrix(ID_GalToUnity,                        GalToUnity);
            ParticleUpdateShader.SetFloat( ID_ParticleSize,                      ParticleSize);
            ParticleUpdateShader.SetFloat( ID_SolitonBrightness,                 SolitonBrightness);
            ParticleUpdateShader.SetFloat( ID_VoidBrightness,                    VoidBrightness);
            ParticleUpdateShader.SetFloat( ID_HeegnerBrightness,                 HeegnerBrightness);
            ParticleUpdateShader.SetFloat( ID_ParityAsymmetry,                   _fieldScalars.parity_asymmetry);
            ParticleUpdateShader.SetFloat( ID_EpochVoidBoost,                    epochVoidBoost);
            ParticleUpdateShader.SetFloat( ID_EpochSolitonBoost,                 epochSolitonBoost);
            ParticleUpdateShader.SetFloat( ID_XiSolitonMod,                      xiSolitonMod);
            ParticleUpdateShader.SetFloat( ID_XiVoidMod,                         xiVoidMod);

            int groups = Mathf.CeilToInt(_particleCount / 64f);
            ParticleUpdateShader.Dispatch(_kernelUpdate, groups, 1, 1);
        }

        private void DrawParticles()
        {
            if (_particleBuffer == null || ParticleMaterial == null) return;

            ParticleMaterial.SetBuffer(ID_ParticleBuffer, _particleBuffer);
            ParticleMaterial.SetMatrix(ID_GalToUnity,     GalToUnity);
            ParticleMaterial.SetFloat( ID_ParticleSize,   ParticleSize);

            Bounds bounds = new Bounds(Vector3.zero, Vector3.one * 2200f);
            Graphics.DrawProceduralIndirect(
                ParticleMaterial,
                bounds,
                MeshTopology.Points,
                _argsBuffer);
        }

        private void CheckHeegnerCrossings()
        {
            foreach (float threshold in HeegnerOmegaThresholds)
            {
                if (_lastEpoch < threshold && NormalisedEpoch >= threshold)
                {
                    Debug.Log($"[HPL] HeegnerFlash @ epoch={NormalisedEpoch:F3} threshold={threshold}");
                    HeegnerFlash?.Invoke();
                }
            }
        }

        // ----------------------------------------------------------------
        // Public API
        // ----------------------------------------------------------------

        public void SetEpoch(float normalisedEpoch)  => NormalisedEpoch = Mathf.Clamp01(normalisedEpoch);
        public void SetXiCoherence(float xi)         => XiCoherence     = Mathf.Clamp01(xi);
        public void SetGalToUnity(Matrix4x4 m)       => GalToUnity      = m;
        public void RefreshFieldScalars()            => LoadFieldScalars();

        /// <summary>
        /// Hot-reload anchor_positions.bin at runtime (e.g. after generate_particle_buffer.py
        /// has been re-run).  Releases the old buffer and re-allocates.
        /// </summary>
        public void ReloadAnchorBuffer()
        {
            _anchorBuffer?.Release();
            LoadAnchorBuffer();
        }

#if UNITY_EDITOR
        [ContextMenu("Reload Field Scalars")]
        private void EditorReloadFieldScalars() => RefreshFieldScalars();

        [ContextMenu("Reload Anchor Buffer")]
        private void EditorReloadAnchorBuffer() => ReloadAnchorBuffer();

        [ContextMenu("Log Buffer Stats")]
        private void EditorLogBufferStats()
        {
            if (_particleBuffer == null) { Debug.Log("Buffer not loaded."); return; }
            Debug.Log($"[HPL] Particles: {_particleCount} ({_particleCount * Marshal.SizeOf<ParticleRecord>() / 1024f / 1024f:F2} MB GPU)  "
                    + $"Anchors: {_anchorCount}");
        }
#endif
    }
}

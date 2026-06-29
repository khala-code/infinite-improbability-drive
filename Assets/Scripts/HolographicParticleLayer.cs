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
    // -----------------------------------------------------------------------
    // ParticleRecord: must match the struct layout in generate_particle_buffer.py
    // 96 bytes, 16-byte aligned for GPU.
    // Stored as a flat float[] in memory; we use Buffer.BlockCopy to transfer
    // from the raw .bin bytes without requiring an unsafe context.
    // -----------------------------------------------------------------------
    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    public struct ParticleRecord
    {
        public Vector3 Position;          // 12 bytes
        public Vector3 Velocity;          // 12 bytes
        public Vector4 Colour;            // 16 bytes
        public float   HeegnerPower;      //  4
        public float   VoidPressure;      //  4
        public float   SolitonDensity;    //  4
        public float   Kappa;             //  4
        public int     ParticleClass;     //  4  (0=Heegner,1=Soliton,2=Void,3=Transition)
        public float   ParityWeight;      //  4
        // Padding to 96 bytes (total fields above = 64 bytes; 8 x float = 32)
        public float _p0, _p1, _p2, _p3, _p4, _p5, _p6, _p7;
    }

    // -----------------------------------------------------------------------
    // FieldScalars: loaded from StreamingAssets/CMB/field_scalars.json
    // -----------------------------------------------------------------------
    [Serializable]
    public class FieldScalars
    {
        public float parity_asymmetry;
        public float heegner_fraction;
        public float void_fraction;
        public float soliton_fraction;
        public float xi_baseline;
    }

    // -----------------------------------------------------------------------
    // Main MonoBehaviour
    // -----------------------------------------------------------------------
    [AddComponentMenu("CMB/Holographic Particle Layer")]
    public class HolographicParticleLayer : MonoBehaviour
    {
        // ----------------------------------------------------------------
        // Inspector fields
        // ----------------------------------------------------------------

        [Header("Pipeline Assets")]
        [Tooltip("particle_buffer.bin from generate_particle_buffer.py")]
        public string ParticleBufferFilename = "particle_buffer.bin";

        [Tooltip("ComputeShader: Assets/Shaders/ParticleUpdate.compute")]
        public ComputeShader ParticleUpdateShader;

        [Tooltip("Material using the particle render shader")]
        public Material ParticleMaterial;

        [Header("Coupling")]
        [Tooltip("Set by EpochScrubber. 0 = high-z, 1 = present day.")]
        [Range(0f, 1f)]
        public float NormalisedEpoch = 1f;

        [Tooltip("Set by ObserverBubbleRenderer. High = soliton dominant.")]
        [Range(0f, 1f)]
        public float XiCoherence = 0.5f;

        [Tooltip("Galactic-to-Unity rotation. Pin to ZaTaOa frame.")]
        public Matrix4x4 GalToUnity = Matrix4x4.identity;

        [Header("Visual Tuning")]
        [Range(0.001f, 0.05f)]
        public float ParticleSize = 0.008f;

        [Range(0f, 2f)] public float SolitonBrightness  = 1f;
        [Range(0f, 2f)] public float VoidBrightness     = 0.4f;
        [Range(0f, 2f)] public float HeegnerBrightness  = 1.8f;

        [Header("Events")]
        public UnityEngine.Events.UnityEvent HeegnerFlash;

        // ----------------------------------------------------------------
        // Private state
        // ----------------------------------------------------------------

        private ComputeBuffer _particleBuffer;
        private ComputeBuffer _argsBuffer;
        private int           _particleCount;
        private int           _kernelUpdate;
        private FieldScalars  _fieldScalars;
        private float         _lastEpoch;

        private static readonly float[] HeegnerOmegaThresholds = { 0.15f, 0.38f, 0.61f, 0.84f };

        // Cached shader property IDs
        private static readonly int ID_ParticleBuffer     = Shader.PropertyToID("_ParticleBuffer");
        private static readonly int ID_ParticleCount      = Shader.PropertyToID("_ParticleCount");
        private static readonly int ID_DeltaTime          = Shader.PropertyToID("_DeltaTime");
        private static readonly int ID_NormEpoch          = Shader.PropertyToID("_NormEpoch");
        private static readonly int ID_XiCoherence        = Shader.PropertyToID("_XiCoherence");
        private static readonly int ID_GalToUnity         = Shader.PropertyToID("_GalToUnity");
        private static readonly int ID_ParticleSize       = Shader.PropertyToID("_ParticleSize");
        private static readonly int ID_SolitonBrightness  = Shader.PropertyToID("_SolitonBrightness");
        private static readonly int ID_VoidBrightness     = Shader.PropertyToID("_VoidBrightness");
        private static readonly int ID_HeegnerBrightness  = Shader.PropertyToID("_HeegnerBrightness");
        private static readonly int ID_ParityAsymmetry    = Shader.PropertyToID("_ParityAsymmetry");
        private static readonly int ID_HeegnerFraction    = Shader.PropertyToID("_HeegnerFraction");
        private static readonly int ID_VoidFraction       = Shader.PropertyToID("_VoidFraction");
        private static readonly int ID_SolitonFraction    = Shader.PropertyToID("_SolitonFraction");
        private static readonly int ID_EpochVoidBoost     = Shader.PropertyToID("_EpochVoidBoost");
        private static readonly int ID_EpochSolitonBoost  = Shader.PropertyToID("_EpochSolitonBoost");
        private static readonly int ID_XiSolitonMod       = Shader.PropertyToID("_XiSolitonMod");
        private static readonly int ID_XiVoidMod          = Shader.PropertyToID("_XiVoidMod");

        // ----------------------------------------------------------------
        // Unity lifecycle
        // ----------------------------------------------------------------

        private void Awake()
        {
            LoadFieldScalars();
            LoadParticleBuffer();
            InitArgsBuffer();
            CacheShaderKernel();
        }

        private void OnDestroy()
        {
            _particleBuffer?.Release();
            _argsBuffer?.Release();
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
            // Quest 2: StreamingAssets are inside the .apk archive.
            // Requires an async UnityWebRequest copy to persistentDataPath on first boot.
            // TODO: implement StreamingAssetsBootstrap.cs before Quest build.
            path = Path.Combine(Application.persistentDataPath, "CMB", ParticleBufferFilename);
#endif

            if (!File.Exists(path))
            {
                Debug.LogError($"[HPL] particle_buffer.bin not found at: {path}\n"
                             + "Run generate_particle_buffer.py and copy output to StreamingAssets/CMB/.");
                return;
            }

            byte[] raw    = File.ReadAllBytes(path);
            int    stride = Marshal.SizeOf<ParticleRecord>();   // expect 96
            _particleCount = raw.Length / stride;

            if (_particleCount == 0)
            {
                Debug.LogError("[HPL] particle_buffer.bin is empty.");
                return;
            }

            // Validate stride matches expectation
            if (stride != 96)
                Debug.LogWarning($"[HPL] ParticleRecord stride is {stride}, expected 96. "
                               + "Check struct padding matches Python output.");

            Debug.Log($"[HPL] Loading {_particleCount} particles "
                    + $"({raw.Length / 1024f / 1024f:F1} MB)  stride={stride}B");

            // ---- Safe copy: raw bytes -> float[] via Buffer.BlockCopy ----
            // ParticleRecord is a blittable struct of floats/ints.
            // Reinterpreting as float[] lets us use Buffer.BlockCopy which
            // is a safe, non-unsafe memmove equivalent in managed C#.
            int floatsPerRecord = stride / sizeof(float);  // 24 floats per record
            float[] floatData   = new float[_particleCount * floatsPerRecord];
            Buffer.BlockCopy(raw, 0, floatData, 0, raw.Length);

            // Upload to GPU
            _particleBuffer = new ComputeBuffer(_particleCount, stride, ComputeBufferType.Structured);
            _particleBuffer.SetData(floatData);
        }

        private void InitArgsBuffer()
        {
            // DrawProceduralIndirect: vertexCount=1 per particle,
            // quad expansion happens in the vertex shader via SV_VertexID.
            uint[] args = new uint[4] { 1u, (uint)_particleCount, 0u, 0u };
            _argsBuffer = new ComputeBuffer(4, sizeof(uint), ComputeBufferType.IndirectArguments);
            _argsBuffer.SetData(args);
        }

        private void CacheShaderKernel()
        {
            if (ParticleUpdateShader == null)
            {
                Debug.LogWarning("[HPL] No ComputeShader assigned.");
                return;
            }
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

            // Push to global scope -- boundary shaders (LensingBoundary, MilkyWayBoundary)
            // read these without any additional wiring.
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
            if (ParticleUpdateShader == null || _particleBuffer == null) return;

            float epochVoidBoost    = Mathf.Lerp(1.8f, 1.0f, NormalisedEpoch);
            float epochSolitonBoost = Mathf.Lerp(0.3f, 1.0f, NormalisedEpoch);
            float xiSolitonMod      = Mathf.Lerp(0.5f, 1.5f, XiCoherence);
            float xiVoidMod         = Mathf.Lerp(1.5f, 0.5f, XiCoherence);

            ParticleUpdateShader.SetBuffer(_kernelUpdate, ID_ParticleBuffer,    _particleBuffer);
            ParticleUpdateShader.SetInt(   ID_ParticleCount,                    _particleCount);
            ParticleUpdateShader.SetFloat( ID_DeltaTime,                        Time.deltaTime);
            ParticleUpdateShader.SetFloat( ID_NormEpoch,                        NormalisedEpoch);
            ParticleUpdateShader.SetFloat( ID_XiCoherence,                      XiCoherence);
            ParticleUpdateShader.SetMatrix(ID_GalToUnity,                       GalToUnity);
            ParticleUpdateShader.SetFloat( ID_ParticleSize,                     ParticleSize);
            ParticleUpdateShader.SetFloat( ID_SolitonBrightness,                SolitonBrightness);
            ParticleUpdateShader.SetFloat( ID_VoidBrightness,                   VoidBrightness);
            ParticleUpdateShader.SetFloat( ID_HeegnerBrightness,                HeegnerBrightness);
            ParticleUpdateShader.SetFloat( ID_ParityAsymmetry,                  _fieldScalars.parity_asymmetry);
            ParticleUpdateShader.SetFloat( ID_EpochVoidBoost,                   epochVoidBoost);
            ParticleUpdateShader.SetFloat( ID_EpochSolitonBoost,                epochSolitonBoost);
            ParticleUpdateShader.SetFloat( ID_XiSolitonMod,                     xiSolitonMod);
            ParticleUpdateShader.SetFloat( ID_XiVoidMod,                        xiVoidMod);

            int groups = Mathf.CeilToInt(_particleCount / 64f);
            ParticleUpdateShader.Dispatch(_kernelUpdate, groups, 1, 1);
        }

        private void DrawParticles()
        {
            if (_particleBuffer == null || ParticleMaterial == null) return;

            ParticleMaterial.SetBuffer(ID_ParticleBuffer, _particleBuffer);
            ParticleMaterial.SetMatrix(ID_GalToUnity,     GalToUnity);
            ParticleMaterial.SetFloat( ID_ParticleSize,   ParticleSize);

            // Bounds large enough to cover the boundary sphere (~1000 Unity units)
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

        /// <summary>Called by EpochScrubber.</summary>
        public void SetEpoch(float normalisedEpoch)     => NormalisedEpoch = Mathf.Clamp01(normalisedEpoch);

        /// <summary>Called by ObserverBubbleRenderer.</summary>
        public void SetXiCoherence(float xi)            => XiCoherence = Mathf.Clamp01(xi);

        /// <summary>Set Galactic-to-Unity rotation from epoch_frame.json. Call once on scene load.</summary>
        public void SetGalToUnity(Matrix4x4 m)          => GalToUnity = m;

        /// <summary>Re-read field_scalars.json and push globals (editor hot-reload).</summary>
        public void RefreshFieldScalars()               => LoadFieldScalars();

#if UNITY_EDITOR
        [ContextMenu("Reload Field Scalars")]
        private void EditorReloadFieldScalars() => RefreshFieldScalars();

        [ContextMenu("Log Buffer Stats")]
        private void EditorLogBufferStats()
        {
            if (_particleBuffer == null) { Debug.Log("Buffer not loaded."); return; }
            Debug.Log($"[HPL] Buffer: {_particleCount} particles, "
                    + $"{_particleCount * Marshal.SizeOf<ParticleRecord>() / 1024f / 1024f:F2} MB GPU");
        }
#endif
    }
}

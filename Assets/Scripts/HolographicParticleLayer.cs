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
    // -----------------------------------------------------------------------
    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    public struct ParticleRecord
    {
        // Position in Galactic Cartesian coords (unit sphere surface at t=0)
        public Vector3 Position;          // 12 bytes
        // Velocity seed -- direction of drift at t=0
        public Vector3 Velocity;          // 12 bytes
        // Base colour (RGBA, linear)
        public Vector4 Colour;            // 16 bytes
        // Scalar field values at this particle's sky position
        public float HeegnerPower;        // 4  -- HEEGNER_LOCKED alm power
        public float VoidPressure;        // 4  -- VOID_PRIME alm power
        public float SolitonDensity;      // 4  -- SOLITON alm power
        public float Kappa;               // 4  -- lensing convergence
        // Classification
        public int   ParticleClass;       // 4  (0=Heegner,1=Soliton,2=Void,3=Transition)
        public float ParityWeight;        // 4  -- local contribution to parity asymmetry
        // Padding to 96 bytes
        public float _pad0;               // 4
        public float _pad1;               // 4
        public float _pad2;               // 4
        public float _pad3;               // 4
        // Total: 12+12+16+4+4+4+4+4+4+4+4+4+4 = 80... pad to 96
        public float _pad4;               // 4
        public float _pad5;               // 4
        public float _pad6;               // 4
        public float _pad7;               // 4
    }

    // -----------------------------------------------------------------------
    // FieldScalars: loaded from StreamingAssets/CMB/field_scalars.json
    // -----------------------------------------------------------------------
    [Serializable]
    public class FieldScalars
    {
        public float parity_asymmetry;    // void_power / soliton_power
        public float heegner_fraction;    // fraction of total power in Heegner modes
        public float void_fraction;
        public float soliton_fraction;
        public float xi_baseline;         // observer coherence baseline from epoch
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

        [Range(0f, 2f)]
        public float SolitonBrightness = 1f;

        [Range(0f, 2f)]
        public float VoidBrightness = 0.4f;

        [Range(0f, 2f)]
        public float HeegnerBrightness = 1.8f;

        [Header("Events")]
        [Tooltip("Fired when epoch crosses a Heegner Omega threshold.")]
        public UnityEngine.Events.UnityEvent HeegnerFlash;

        // ----------------------------------------------------------------
        // Private state
        // ----------------------------------------------------------------

        private ComputeBuffer _particleBuffer;
        private ComputeBuffer _argsBuffer;
        private int _particleCount;
        private int _kernelUpdate;

        private FieldScalars _fieldScalars;
        private float _lastEpoch;

        // Heegner Omega crossings -- epoch values where the field competition
        // tips.  Values derived from field_scalars parity history; hardcoded
        // defaults here, overridden at runtime if epoch_frame.json supplies them.
        private static readonly float[] HeegnerOmegaThresholds = { 0.15f, 0.38f, 0.61f, 0.84f };

        // Shader property IDs -- cached to avoid string lookups per frame
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
            // On Quest 2, StreamingAssets are inside a .apk and must be read
            // via UnityWebRequest.  For now, copy to persistentDataPath on
            // first launch.  TODO: implement async copy on first boot.
            path = Path.Combine(Application.persistentDataPath, "CMB", ParticleBufferFilename);
#endif

            if (!File.Exists(path))
            {
                Debug.LogError($"[HolographicParticleLayer] particle_buffer.bin not found at {path}. "
                             + "Run generate_particle_buffer.py and copy output to StreamingAssets/CMB/.");
                return;
            }

            byte[] bytes = File.ReadAllBytes(path);
            int stride   = Marshal.SizeOf<ParticleRecord>();    // must be 96
            _particleCount = bytes.Length / stride;

            if (_particleCount == 0)
            {
                Debug.LogError("[HolographicParticleLayer] particle_buffer.bin is empty.");
                return;
            }

            Debug.Log($"[HolographicParticleLayer] Loaded {_particleCount} particles "
                    + $"({bytes.Length / 1024f / 1024f:F1} MB)  stride={stride}B");

            _particleBuffer = new ComputeBuffer(_particleCount, stride, ComputeBufferType.Structured);

            // Pin managed array and bulk-copy into GPU buffer
            ParticleRecord[] records = new ParticleRecord[_particleCount];
            GCHandle pin = GCHandle.Alloc(bytes, GCHandleType.Pinned);
            try
            {
                IntPtr src = pin.AddrOfPinnedObject();
                GCHandle dstPin = GCHandle.Alloc(records, GCHandleType.Pinned);
                try
                {
                    Buffer.MemoryCopy(
                        (void*)src,
                        (void*)dstPin.AddrOfPinnedObject(),
                        (long)(records.Length * stride),
                        (long)bytes.Length);
                }
                finally { dstPin.Free(); }
            }
            finally { pin.Free(); }

            _particleBuffer.SetData(records);
        }

        private void InitArgsBuffer()
        {
            // DrawProceduralIndirect args: vertexCount, instanceCount, startVertex, startInstance
            // We draw 1 vertex per particle and expand to a quad in the geometry/vertex shader.
            uint[] args = new uint[4] { 1u, (uint)_particleCount, 0u, 0u };
            _argsBuffer = new ComputeBuffer(4, sizeof(uint), ComputeBufferType.IndirectArguments);
            _argsBuffer.SetData(args);
        }

        private void CacheShaderKernel()
        {
            if (ParticleUpdateShader == null)
            {
                Debug.LogWarning("[HolographicParticleLayer] No ComputeShader assigned.");
                return;
            }
            _kernelUpdate = ParticleUpdateShader.FindKernel("UpdateParticles");
        }

        private void LoadFieldScalars()
        {
            string path = Path.Combine(Application.streamingAssetsPath, "CMB", "field_scalars.json");
            if (!File.Exists(path))
            {
                Debug.LogWarning("[HolographicParticleLayer] field_scalars.json not found; using defaults.");
                _fieldScalars = new FieldScalars
                {
                    parity_asymmetry = 1f, heegner_fraction = 0.1f,
                    void_fraction = 0.55f, soliton_fraction = 0.35f,
                    xi_baseline = 0.5f
                };
                return;
            }
            string json = File.ReadAllText(path);
            _fieldScalars = JsonUtility.FromJson<FieldScalars>(json);

            // Push to global shader scope so boundary shaders read the same values
            Shader.SetGlobalFloat(ID_ParityAsymmetry, _fieldScalars.parity_asymmetry);
            Shader.SetGlobalFloat(ID_HeegnerFraction, _fieldScalars.heegner_fraction);
            Shader.SetGlobalFloat(ID_VoidFraction,    _fieldScalars.void_fraction);
            Shader.SetGlobalFloat(ID_SolitonFraction, _fieldScalars.soliton_fraction);

            Debug.Log($"[HolographicParticleLayer] FieldScalars loaded: "
                    + $"parity={_fieldScalars.parity_asymmetry:F3} "
                    + $"heegner={_fieldScalars.heegner_fraction:F3} "
                    + $"void={_fieldScalars.void_fraction:F3} "
                    + $"soliton={_fieldScalars.soliton_fraction:F3}");
        }

        // ----------------------------------------------------------------
        // Per-frame
        // ----------------------------------------------------------------

        private void DispatchComputeUpdate()
        {
            if (ParticleUpdateShader == null || _particleBuffer == null) return;

            ParticleUpdateShader.SetBuffer(_kernelUpdate, ID_ParticleBuffer, _particleBuffer);
            ParticleUpdateShader.SetInt(   ID_ParticleCount,     _particleCount);
            ParticleUpdateShader.SetFloat( ID_DeltaTime,         Time.deltaTime);
            ParticleUpdateShader.SetFloat( ID_NormEpoch,         NormalisedEpoch);
            ParticleUpdateShader.SetFloat( ID_XiCoherence,       XiCoherence);
            ParticleUpdateShader.SetMatrix(ID_GalToUnity,        GalToUnity);
            ParticleUpdateShader.SetFloat( ID_ParticleSize,      ParticleSize);
            ParticleUpdateShader.SetFloat( ID_SolitonBrightness, SolitonBrightness);
            ParticleUpdateShader.SetFloat( ID_VoidBrightness,    VoidBrightness);
            ParticleUpdateShader.SetFloat( ID_HeegnerBrightness, HeegnerBrightness);
            ParticleUpdateShader.SetFloat( ID_ParityAsymmetry,   _fieldScalars.parity_asymmetry);

            // Epoch modulations:
            // - High redshift (low NormEpoch): void visibility boosted, soliton muted
            // - Low redshift (high NormEpoch): soliton dominant, void recedes
            float epochVoidBoost    = Mathf.Lerp(1.8f, 1.0f, NormalisedEpoch);
            float epochSolitonBoost = Mathf.Lerp(0.3f, 1.0f, NormalisedEpoch);
            ParticleUpdateShader.SetFloat(Shader.PropertyToID("_EpochVoidBoost"),    epochVoidBoost);
            ParticleUpdateShader.SetFloat(Shader.PropertyToID("_EpochSolitonBoost"), epochSolitonBoost);

            // Xi modulation: high coherence = soliton brightens, void dims
            float xiSolitonMod = Mathf.Lerp(0.5f, 1.5f, XiCoherence);
            float xiVoidMod    = Mathf.Lerp(1.5f, 0.5f, XiCoherence);
            ParticleUpdateShader.SetFloat(Shader.PropertyToID("_XiSolitonMod"), xiSolitonMod);
            ParticleUpdateShader.SetFloat(Shader.PropertyToID("_XiVoidMod"),    xiVoidMod);

            // Dispatch: threadgroup size 64 (matches [numthreads(64,1,1)] in compute shader)
            int groups = Mathf.CeilToInt(_particleCount / 64f);
            ParticleUpdateShader.Dispatch(_kernelUpdate, groups, 1, 1);
        }

        private void DrawParticles()
        {
            if (_particleBuffer == null || ParticleMaterial == null) return;

            ParticleMaterial.SetBuffer(ID_ParticleBuffer, _particleBuffer);
            ParticleMaterial.SetMatrix(ID_GalToUnity,     GalToUnity);
            ParticleMaterial.SetFloat( ID_ParticleSize,   ParticleSize);

            // Render into a bounds large enough to cover the boundary sphere
            // (radius ~1000 Unity units for a skybox-scale scene)
            Bounds bounds = new Bounds(Vector3.zero, Vector3.one * 2200f);

            Graphics.DrawProceduralIndirect(
                ParticleMaterial,
                bounds,
                MeshTopology.Points,
                _argsBuffer,
                camera: null   // renders to all cameras; restrict if needed
            );
        }

        private void CheckHeegnerCrossings()
        {
            foreach (float threshold in HeegnerOmegaThresholds)
            {
                bool wasBefore = _lastEpoch < threshold;
                bool isAfter   = NormalisedEpoch >= threshold;
                if (wasBefore && isAfter)
                {
                    Debug.Log($"[HolographicParticleLayer] HeegnerFlash @ epoch={NormalisedEpoch:F3} threshold={threshold}");
                    HeegnerFlash?.Invoke();
                }
            }
        }

        // ----------------------------------------------------------------
        // Public API for coupling components
        // ----------------------------------------------------------------

        /// <summary>Called by EpochScrubber to advance the simulation epoch.</summary>
        public void SetEpoch(float normalisedEpoch)
        {
            NormalisedEpoch = Mathf.Clamp01(normalisedEpoch);
        }

        /// <summary>Called by ObserverBubbleRenderer to update xi coherence.</summary>
        public void SetXiCoherence(float xi)
        {
            XiCoherence = Mathf.Clamp01(xi);
        }

        /// <summary>
        /// Set the Galactic-to-Unity rotation matrix from epoch_frame.json.
        /// Call this once on scene load after reading the ZaTaOa frame.
        /// </summary>
        public void SetGalToUnity(Matrix4x4 m)
        {
            GalToUnity = m;
        }

        /// <summary>Reload field_scalars.json and re-push globals (call after hot-reload in editor).</summary>
        public void RefreshFieldScalars()
        {
            LoadFieldScalars();
        }

#if UNITY_EDITOR
        [ContextMenu("Reload Field Scalars")]
        private void EditorReloadFieldScalars() => RefreshFieldScalars();

        [ContextMenu("Log Buffer Stats")]
        private void EditorLogBufferStats()
        {
            if (_particleBuffer == null) { Debug.Log("Buffer not loaded."); return; }
            Debug.Log($"ParticleBuffer: {_particleCount} particles, "
                    + $"{_particleCount * Marshal.SizeOf<ParticleRecord>() / 1024f / 1024f:F2} MB GPU");
        }
#endif
    }
}

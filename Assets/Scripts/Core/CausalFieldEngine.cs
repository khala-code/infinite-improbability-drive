// CausalFieldEngine.cs
// MonoBehaviour driver for the causal field engine.
//
// Owns the NativeArray<CausalNode> octree buffer, dispatches the
// CvBField compute shader each frame, and exposes the public API
// used by ObserverBubbleRenderer, EpochScrubber, and the VFX Graph.
//
// Ω crossing classification:
//   Composite Ω → bulk update, no event
//   Prime Ω     → OnPrimeCrossing fired (bifurcated choice window)
//   Heegner Ω   → OnHeegnerCrossing fired (overwhelming forced resolution)
//
// NOTE: OmegaZaTaCoordinate.Xi is currently a scalar float.
// When Xi is promoted to Vector4 (4-axis tensor), replace the
// scalar broadcast in DispatchCompute() with the full vector.

using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Events;
using InfiniteImprobability.Core;

namespace InfiniteImprobability.Core
{
    [RequireComponent(typeof(ObserverBubble))]
    public class CausalFieldEngine : MonoBehaviour
    {
        // ── Inspector ─────────────────────────────────────────────────────────
        [Header("Field")]
        [SerializeField] ComputeShader _computeShader;
        [SerializeField] Texture2D     _cmbTexture;
        [SerializeField] int           _resolution  = CausalOctree.DefaultResolution;
        [SerializeField] float         _bubbleScale = 2f;

        [Header("CνB Helix")]
        [SerializeField] Vector3 _spinVector = Vector3.up; // Majorana L-handed axis

        [Header("Thresholds")]
        [SerializeField] float _bifurcationEpsilon = 0.05f;
        [SerializeField] float _heegnerEpsilon     = 0.05f;
        [SerializeField] float _spawnThreshold     = 0.1f;
        [SerializeField] float _annihilationRate   = 0.5f;
        [SerializeField] float _voidDecayRate      = 0.02f;

        [Header("Ω Events")]
        public UnityEvent<float> OnPrimeCrossing;     // arg: Ω value
        public UnityEvent<float> OnHeegnerCrossing;  // arg: Ω value — 9 moments
        public UnityEvent<float> OnCompositeCrossing;

        // ── Private state ─────────────────────────────────────────────────────
        NativeArray<CausalNode> _nodes;
        ComputeBuffer           _nodeBuffer;
        ObserverBubble          _observer;

        int _kernelUpdatePsi;
        int _kernelUpdateVoid;
        int _kernelClassify;

        float _lastOmega = -1f;

        // The 9 Ω boundaries where unique factorisation holds and
        // constructive interference between CMB/CνB spirals is complete.
        static readonly HashSet<int> HeegnerNumbers =
            new HashSet<int> { 1, 2, 3, 7, 11, 19, 43, 67, 163 };

        // ── Lifecycle ──────────────────────────────────────────────────────────
        void Awake()
        {
            _observer = GetComponent<ObserverBubble>();
        }

        void OnEnable()
        {
            int count = _resolution * _resolution * _resolution;

            _nodes      = CausalOctree.Allocate(_resolution);
            _nodeBuffer = new ComputeBuffer(count * 7, sizeof(float));

            _kernelUpdatePsi  = _computeShader.FindKernel("UpdatePsi");
            _kernelUpdateVoid = _computeShader.FindKernel("UpdateVoid");
            _kernelClassify   = _computeShader.FindKernel("ClassifyNodes");

            BindStaticUniforms();

            _observer.OnCoordinateChanged += OnCoordinateChanged;
            _observer.OnCoherenceChanged  += OnCoherenceChanged;
        }

        void OnDisable()
        {
            _observer.OnCoordinateChanged -= OnCoordinateChanged;
            _observer.OnCoherenceChanged  -= OnCoherenceChanged;

            _nodeBuffer?.Release();
            if (_nodes.IsCreated) _nodes.Dispose();
        }

        void Update()
        {
            DispatchCompute();
        }

        // ── Compute dispatch ───────────────────────────────────────────────────
        void BindStaticUniforms()
        {
            foreach (int k in new[] { _kernelUpdatePsi, _kernelUpdateVoid, _kernelClassify })
            {
                _computeShader.SetBuffer(k, "_Nodes", _nodeBuffer);
                _computeShader.SetInt("_Resolution", _resolution);
                _computeShader.SetFloat("_BubbleScale", _bubbleScale);
            }
            _computeShader.SetTexture(_kernelUpdatePsi, "_CMBTexture", _cmbTexture);
            _computeShader.SetFloats("_SpinVector",
                _spinVector.x, _spinVector.y, _spinVector.z);
            _computeShader.SetFloat("_BifurcationEpsilon", _bifurcationEpsilon);
            _computeShader.SetFloat("_HeegnerEpsilon",     _heegnerEpsilon);
            _computeShader.SetFloat("_SpawnThreshold",     _spawnThreshold);
            _computeShader.SetFloat("_AnnihilationRate",   _annihilationRate);
            _computeShader.SetFloat("_VoidDecayRate",      _voidDecayRate);
        }

        void DispatchCompute()
        {
            // Xi is currently a scalar on OmegaZaTaCoordinate.
            // Broadcast it uniformly across all 4 tensor axes.
            // TODO: expand to Vector4 when Xi tensor promotion lands.
            float xiScalar = _observer.Coordinate.Xi;
            _computeShader.SetFloat("_DeltaTime",  Time.deltaTime);
            _computeShader.SetFloat("_OmegaValue", CurrentOmega());
            _computeShader.SetFloats("_XiObserver",
                xiScalar, xiScalar, xiScalar, xiScalar);

            int groups = Mathf.CeilToInt(_resolution / 8f);
            _computeShader.Dispatch(_kernelUpdatePsi,  groups, groups, groups);
            _computeShader.Dispatch(_kernelUpdateVoid, groups, groups, groups);
            _computeShader.Dispatch(_kernelClassify,   groups, groups, groups);
        }

        // ── Ω crossing classification ──────────────────────────────────────────
        float CurrentOmega()
        {
            float z = _observer != null ? _observer.Coordinate.RedshiftZ : 0f;
            return Mathf.Log(1f + z) / Mathf.Log(1f + 1090f);
        }

        void CheckOmegaCrossing(float newOmega)
        {
            if (Mathf.Approximately(_lastOmega, newOmega)) return;

            // Scale [0,1] Ω to [0,163] for Heegner/prime classification
            int omegaInt = Mathf.RoundToInt(newOmega * 163f);

            if (HeegnerNumbers.Contains(omegaInt))
                OnHeegnerCrossing?.Invoke(newOmega);
            else if (IsPrime(omegaInt))
                OnPrimeCrossing?.Invoke(newOmega);
            else
                OnCompositeCrossing?.Invoke(newOmega);

            _lastOmega = newOmega;
        }

        // ── Observer event handlers ────────────────────────────────────────────
        void OnCoordinateChanged(OmegaZaTaCoordinate coord)
        {
            CheckOmegaCrossing(CurrentOmega());
        }

        // Matches ObserverBubble.OnCoherenceChanged: Action<bool>
        // true = coherent, false = destabilised
        void OnCoherenceChanged(bool isCoherent)
        {
            // Coherence state change propagates via XiObserver uniform
            // each frame in DispatchCompute() — no extra work needed here.
            // Downstream: VFX Graph reads xi.w classification flag from
            // ClassifyNodes kernel output.
        }

        // ── Public API ─────────────────────────────────────────────────────────
        /// <summary>
        /// Sample the causal field at a world-space position.
        /// Returns the CausalNode for the nearest octree cell.
        /// </summary>
        public CausalNode GetNode(Vector3 worldPos)
        {
            Vector3Int g = CausalOctree.ToGrid(worldPos, _bubbleScale, _resolution);
            int idx = CausalOctree.Index(g.x, g.y, g.z, _resolution);
            return _nodes[idx];
        }

        /// <summary>
        /// Compute a ConsensusEdge between two world-space positions.
        /// Returns the shared phase and coherence gain for that interaction.
        /// </summary>
        public ConsensusEdge ResolveEdge(Vector3 posA, Vector3 posB)
        {
            Vector3Int gA = CausalOctree.ToGrid(posA, _bubbleScale, _resolution);
            Vector3Int gB = CausalOctree.ToGrid(posB, _bubbleScale, _resolution);
            int idxA = CausalOctree.Index(gA.x, gA.y, gA.z, _resolution);
            int idxB = CausalOctree.Index(gB.x, gB.y, gB.z, _resolution);
            return ConsensusEdge.Compute(_nodes[idxA], _nodes[idxB], idxA, idxB);
        }

        /// <summary>
        /// Observer resolves a bifurcation branch at their current position.
        /// Selects the branch matching branchSign (+1 or -1), annihilates
        /// the other, and reduces local void density.
        /// Called by input handler when user resolves a choice window.
        /// </summary>
        public void ResolveBranch(float branchSign)
        {
            Vector3Int g   = CausalOctree.ToGrid(Vector3.zero, _bubbleScale, _resolution);
            int        idx = CausalOctree.Index(g.x, g.y, g.z, _resolution);
            CausalNode node = _nodes[idx];

            node.Psi = new Vector2(
                branchSign * Mathf.Max(Mathf.Abs(node.Psi.x), _bifurcationEpsilon * 2f),
                node.Psi.y);
            node.VoidDensity = Mathf.Max(0f, node.VoidDensity - 0.1f);
            _nodes[idx] = node;
        }

        // ── Utilities ──────────────────────────────────────────────────────────
        static bool IsPrime(int n)
        {
            if (n < 2)  return false;
            if (n == 2) return true;
            if (n % 2 == 0) return false;
            for (int i = 3; i * i <= n; i += 2)
                if (n % i == 0) return false;
            return true;
        }
    }
}

// CausalNode.cs
// The fundamental unit of the causal octree.
//
// Each node encodes the local state of the causal field at one grid cell:
//   Psi         — complex wave amplitude (Re=CMB signal, Im=CνB phase)
//   VoidDensity — local void density ρ_v (information substrate depletion)
//   Xi          — coherence scalar (C# side; GPU side uses float4 xyzw)
//   XiAxis      — coherence axis direction (bisector of Za_outer / Za_inner)
//
// Classification (read by CausalFieldBridge, written by ClassifyNodes kernel):
//   BULK           xi > +epsilon        stable premeasurement region
//   BIFURCATED     |xi| < epsilon       choice window, adversarial membrane
//   HEEGNER_LOCKED xi < -epsilon        overwhelming forced resolution
//
// CausalOctree provides the flat-array helpers: Allocate, Index, ToGrid, ToWorld.

using Unity.Collections;
using UnityEngine;

namespace InfiniteImprobability.Core
{
    // -------------------------------------------------------------------------
    // CausalNode struct — 7 floats, blittable, NativeArray-safe
    // -------------------------------------------------------------------------
    public struct CausalNode
    {
        /// <summary>Complex wave amplitude. x=Re(ψ), y=Im(ψ).</summary>
        public Vector2 Psi;          // 2 floats

        /// <summary>Local void density ρ_v in [0,1].</summary>
        public float VoidDensity;    // 1 float

        /// <summary>
        /// Coherence scalar on the C# side (scalar until tensor promotion).
        /// On the GPU the ClassifyNodes kernel packs classification into xi.w sign.
        /// </summary>
        public float Xi;             // 1 float

        /// <summary>Coherence axis — unit vector bisecting Za_outer/Za_inner.</summary>
        public Vector3 XiAxis;       // 3 floats
        // Total: 7 floats = 28 bytes

        // ── Classification predicates ─────────────────────────────────────
        private const float BifurcationEpsilon = 0.05f;

        /// <summary>True when |Xi| is below the bifurcation threshold.</summary>
        public bool IsBifurcated()    => Mathf.Abs(Xi) < BifurcationEpsilon;

        /// <summary>True when Xi is strongly negative (Heegner locked).</summary>
        public bool IsHeegnerLocked() => Xi < -BifurcationEpsilon;

        // ── Wave intensity ──────────────────────────────────────────────
        /// <summary>|psi|^2 — probability density at this node.</summary>
        public float PsiMagnitudeSq   => Psi.sqrMagnitude;
    }

    // -------------------------------------------------------------------------
    // ConsensusEdge — shared phase between two nodes
    // -------------------------------------------------------------------------
    /// <summary>
    /// The result of a consensus operation between two CausalNodes.
    /// SharedPhase is the 8th dimension produced by the interaction.
    /// CoherenceGain is the void-density reduction at both nodes.
    /// </summary>
    public struct ConsensusEdge
    {
        public float SharedPhase;    // Im(ψ_A) * Im(ψ_B) product
        public float CoherenceGain;  // (Xi_A + Xi_B) / 2 - baseline
        public int   NodeIndexA;
        public int   NodeIndexB;

        public static ConsensusEdge Compute(
            CausalNode a, CausalNode b, int idxA, int idxB)
        {
            return new ConsensusEdge
            {
                SharedPhase   = a.Psi.y * b.Psi.y,
                CoherenceGain = (a.Xi + b.Xi) * 0.5f,
                NodeIndexA    = idxA,
                NodeIndexB    = idxB,
            };
        }

        /// <summary>
        /// Apply the consensus result: reduce void density at both nodes,
        /// boost Xi toward the shared coherence level.
        /// </summary>
        public void Apply(
            ref CausalNode a, ref CausalNode b,
            float voidReductionRate = 0.05f)
        {
            float reduction = SharedPhase * voidReductionRate;
            a.VoidDensity = Mathf.Max(0f, a.VoidDensity - reduction);
            b.VoidDensity = Mathf.Max(0f, b.VoidDensity - reduction);
            a.Xi = Mathf.Lerp(a.Xi, CoherenceGain, 0.1f);
            b.Xi = Mathf.Lerp(b.Xi, CoherenceGain, 0.1f);
        }
    }

    // -------------------------------------------------------------------------
    // CausalOctree — flat 3D array helpers
    // -------------------------------------------------------------------------
    /// <summary>
    /// Static helpers for the flat NativeArray<CausalNode> octree.
    /// Grid coordinates are integers in [0, resolution).
    /// World coordinates are centred on the origin, scaled by bubbleScale.
    /// </summary>
    public static class CausalOctree
    {
        /// <summary>Default octree resolution (32^3 = 32768 nodes).</summary>
        public const int DefaultResolution = 32;

        // ── Allocation ──────────────────────────────────────────────────────
        public static NativeArray<CausalNode> Allocate(int resolution)
        {
            int count = resolution * resolution * resolution;
            return new NativeArray<CausalNode>(
                count, Allocator.Persistent,
                NativeArrayOptions.ClearMemory);
        }

        // ── Index ───────────────────────────────────────────────────────────
        /// <summary>Flat index from 3D grid coordinates.</summary>
        public static int Index(int x, int y, int z, int resolution)
            => x + y * resolution + z * resolution * resolution;

        // ── Grid ↔ World ──────────────────────────────────────────────────
        /// <summary>
        /// Convert a world-space position to the nearest grid cell.
        /// Clamps to valid range.
        /// </summary>
        public static Vector3Int ToGrid(Vector3 worldPos, float bubbleScale, int resolution)
        {
            float halfScale = bubbleScale * 0.5f;
            float cellSize  = bubbleScale / resolution;

            int x = Mathf.Clamp(
                Mathf.FloorToInt((worldPos.x + halfScale) / cellSize), 0, resolution - 1);
            int y = Mathf.Clamp(
                Mathf.FloorToInt((worldPos.y + halfScale) / cellSize), 0, resolution - 1);
            int z = Mathf.Clamp(
                Mathf.FloorToInt((worldPos.z + halfScale) / cellSize), 0, resolution - 1);

            return new Vector3Int(x, y, z);
        }

        /// <summary>
        /// Convert a grid cell (x,y,z) to its world-space centre position.
        /// Inverse of ToGrid.
        /// </summary>
        public static Vector3 ToWorld(int x, int y, int z, float bubbleScale, int resolution)
        {
            float cellSize  = bubbleScale / resolution;
            float halfScale = bubbleScale * 0.5f;
            float halfCell  = cellSize * 0.5f;

            return new Vector3(
                x * cellSize - halfScale + halfCell,
                y * cellSize - halfScale + halfCell,
                z * cellSize - halfScale + halfCell);
        }
    }
}

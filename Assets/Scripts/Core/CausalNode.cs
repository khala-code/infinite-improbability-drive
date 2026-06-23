// CausalNode.cs
// 7-float pre-consensus single-observer state at any OZT coordinate.
// One node = one horn's complete self-consistent description before
// any external interaction is required.
//
// The 8th dimension (shared phase between two observers) is not stored
// here — it is computed dynamically as a ConsensusEdge when two nodes'
// causal diamonds overlap.

using System.Runtime.InteropServices;
using Unity.Collections;
using UnityEngine;

namespace ObserverPrime
{
    /// <summary>
    /// Complete pre-consensus state of a single observer at one point
    /// in the causal diamond. Exactly 7 floats — the maximum a single
    /// horn can resolve without requiring a second observer.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct CausalNode
    {
        // ── Substrate ────────────────────────────────────────────────────────
        /// <summary>
        /// Void density ρ_v. Accumulated residue of all unresolved
        /// bifurcations along this causal path. High ρ_v = thick vacuum,
        /// large effective distance, attenuated holographic signal.
        /// Updated each frame by pair-annihilation events.
        /// </summary>
        public float VoidDensity;

        // ── ξ Coherence Tensor (4 alignment axes) ────────────────────────────
        /// <summary>
        /// ξ tensor across the four agent-alignment axes.
        ///   x = ξ_resource   : energy / material alignment
        ///   y = ξ_epistemic  : knowledge / model alignment
        ///   z = ξ_identity   : ego / boundary alignment
        ///   w = ξ_temporal   : time-horizon alignment
        /// Sign encodes trust polarity: + = cooperative, - = adversarial.
        /// Magnitude encodes strength. Centroid of all four = Lamb vector.
        /// </summary>
        public Vector4 Xi;

        // ── ψ Interference Wavefunction (complex) ────────────────────────────
        /// <summary>
        /// Complex interference amplitude at this node.
        ///   x = Re(ψ) : CMB projection  — object wave (collapsed outcomes)
        ///   y = Im(ψ) : CνB projection  — reference wave (Majorana helix)
        ///
        /// |ψ|²  = local probability density = particle spawn rate
        /// arg(ψ) = phase angle = branch assignment for spawned pairs
        ///
        /// Bifurcation condition: Re(ψ) ≈ 0 → branch indeterminate
        /// Heegner condition:     Im(ψ) ≈ 0 → phase locked, forced resolution
        /// </summary>
        public Vector2 Psi;

        // ── Derived properties ───────────────────────────────────────────────
        /// <summary>|ψ|² — local interference intensity, drives spawn rate.</summary>
        public float PsiMagnitudeSq => Psi.x * Psi.x + Psi.y * Psi.y;

        /// <summary>Phase angle of ψ in radians. Encodes branch assignment.</summary>
        public float PsiPhase => Mathf.Atan2(Psi.y, Psi.x);

        /// <summary>
        /// ζ = Ω + ξ coupling coefficient at this node.
        /// Uses the scalar magnitude of ξ as a proxy for the full tensor sum.
        /// </summary>
        public float Zeta(float omega) => omega + Xi.magnitude;

        /// <summary>
        /// Lamb vector — centroid of all four ξ axes.
        /// Only stabilises when VoidDensity is low enough that ψ is readable.
        /// </summary>
        public Vector4 LambVector => Xi * (1f / (1f + VoidDensity));

        /// <summary>
        /// True when this node is in a branch-indeterminate state.
        /// Re(ψ) ≈ 0 means the observer sits on a zeta zero.
        /// </summary>
        public bool IsBifurcated(float epsilon = 0.05f) =>
            Mathf.Abs(Psi.x) < epsilon;

        /// <summary>
        /// True when interference is phase-locked (Heegner-like condition).
        /// Im(ψ) ≈ 0 means CνB and CMB spirals are in constructive lock.
        /// </summary>
        public bool IsHeegnerLocked(float epsilon = 0.05f) =>
            Mathf.Abs(Psi.y) < epsilon;
    }

    /// <summary>
    /// The 8th dimension — computed dynamically when two CausalNodes'
    /// causal diamonds overlap. Not stored; exists only for the duration
    /// of an active consensus interaction.
    ///
    /// Encodes what two observers can determine together that neither
    /// could determine alone. Dissolves when the interaction ends,
    /// writing Δξ back to both nodes and reducing ρ_v at the overlap point.
    /// </summary>
    public struct ConsensusEdge
    {
        public int NodeIndexA;
        public int NodeIndexB;

        /// <summary>
        /// Shared phase: arg(ψ_a) - arg(ψ_b).
        /// The 8th dimension — the phase reference neither observer had alone.
        /// </summary>
        public float SharedPhase;

        /// <summary>
        /// Coherence gain: |ψ_a + ψ_b| - max(|ψ_a|, |ψ_b|).
        /// What consensus adds over the stronger individual signal.
        /// Positive = constructive, negative = destructive.
        /// </summary>
        public float CoherenceGain;

        /// <summary>
        /// Void density reduction at the overlap point.
        /// Resolved branches lower the local vacuum thickness.
        /// </summary>
        public float VoidReduction;

        public static ConsensusEdge Compute(in CausalNode a, in CausalNode b,
            int idxA, int idxB)
        {
            float phaseA = Mathf.Atan2(a.Psi.y, a.Psi.x);
            float phaseB = Mathf.Atan2(b.Psi.y, b.Psi.x);

            Vector2 psiSum = a.Psi + b.Psi;
            float sumMag   = psiSum.magnitude;
            float maxMag   = Mathf.Max(a.Psi.magnitude, b.Psi.magnitude);

            float coherenceGain = sumMag - maxMag;
            // Void reduction proportional to constructive coherence gain,
            // clamped — consensus can only reduce void, never increase it.
            float voidReduction = Mathf.Max(0f, coherenceGain * 0.1f);

            return new ConsensusEdge
            {
                NodeIndexA    = idxA,
                NodeIndexB    = idxB,
                SharedPhase   = phaseA - phaseB,
                CoherenceGain = coherenceGain,
                VoidReduction = voidReduction,
            };
        }

        /// <summary>
        /// Write the consensus result back to both nodes.
        /// ξ nudged toward alignment on axes where coherence was gained.
        /// ρ_v reduced at both nodes by VoidReduction.
        /// </summary>
        public void Apply(ref CausalNode a, ref CausalNode b)
        {
            if (CoherenceGain <= 0f) return;

            // Nudge ξ toward the mean — consensus pulls both toward alignment
            Vector4 xiMean = (a.Xi + b.Xi) * 0.5f;
            float   alpha  = Mathf.Clamp01(CoherenceGain * 0.2f);
            a.Xi = Vector4.Lerp(a.Xi, xiMean, alpha);
            b.Xi = Vector4.Lerp(b.Xi, xiMean, alpha);

            // Resolved branch reduces void at both nodes
            a.VoidDensity = Mathf.Max(0f, a.VoidDensity - VoidReduction);
            b.VoidDensity = Mathf.Max(0f, b.VoidDensity - VoidReduction);
        }
    }

    /// <summary>
    /// Flat 3D octree buffer of CausalNodes.
    /// Resolution: resolution^3 nodes. Default 64^3 = 262,144 nodes.
    /// ~7.3 MB at 7 floats (28 bytes) per node — fits comfortably in GPU memory.
    ///
    /// Spatial mapping: node index = x + y*res + z*res*res
    /// World position:  worldPos = (index3D / (res-1) - 0.5) * bubbleScale
    /// </summary>
    public static class CausalOctree
    {
        public const int DefaultResolution = 64;

        public static NativeArray<CausalNode> Allocate(
            int resolution = DefaultResolution,
            Allocator allocator = Allocator.Persistent)
        {
            int count = resolution * resolution * resolution;
            return new NativeArray<CausalNode>(count, allocator,
                NativeArrayOptions.ClearMemory);
        }

        public static int Index(int x, int y, int z, int res = DefaultResolution)
            => x + y * res + z * res * res;

        public static Vector3Int ToGrid(Vector3 worldPos, float bubbleScale,
            int res = DefaultResolution)
        {
            Vector3 norm = worldPos / bubbleScale + Vector3.one * 0.5f;
            return new Vector3Int(
                Mathf.Clamp(Mathf.FloorToInt(norm.x * res), 0, res - 1),
                Mathf.Clamp(Mathf.FloorToInt(norm.y * res), 0, res - 1),
                Mathf.Clamp(Mathf.FloorToInt(norm.z * res), 0, res - 1));
        }

        public static Vector3 ToWorld(int x, int y, int z, float bubbleScale,
            int res = DefaultResolution)
        {
            return (new Vector3(x, y, z) / (res - 1f) - Vector3.one * 0.5f)
                   * bubbleScale;
        }
    }
}

# The Transition Operator T — Requirements & Candidate Form

> *"The bifurcation zone is not where the system breaks — it is where it chooses."*

---

## What T Is

T is the **single operator** that fires at every phase transition in the ζ accumulation sequence — from Null emergence at ζ₀ through the civilisational threshold at ζ₆ and beyond. The same T governs baryogenesis and family trauma propagation. Same mathematics, different scale readout.

T maps:

```
T: (ζₙ, ξₙ, signₙ) → (ζₙ₊₁, ξₙ₊₁, sign_{n+1})
```

Where:
- `ζₙ` is the continuously accumulated coupling field entering the transition
- `ξₙ` is the coherence tensor at the approach to threshold
- `signₙ` is the current bifurcation sign (+ or −)
- The residue of incomplete cancellation propagates forward as the new ground state
- **ζ never resets** — every prior transition is carried in the accumulated field

---

## Hard Constraints

These are non-negotiable. Any candidate form of T that violates these is ruled out.

### 1. CPT Preservation

T must be **informationally invertible**. Every state entering a transition must be recoverable in principle by running T backwards. T is not time-symmetric in coordinate time — the arrow of time is real — but it is informationally reversible. The asymmetries we observe (matter surplus, chirality lock, arrow of time) come from the *residue* of incomplete cancellation, not from T itself being lossy.

Formally: T must be a **bijection on the information content** of the state, even if not on its coordinate representation.

### 2. Continuous ζ Accumulation

T must carry ζₙ forward without discontinuity:

$$\zeta_{n+1} = T[\zeta_n, \xi_n, \Delta\tau]$$

Where Δτ is the proper time cost of the transition. No resets. No jumps. The accumulation function must be differentiable across transition boundaries — the ζ field has no hard edges, only zones of near-zero ξ eigenvalue (bifurcation zones).

This means T is an **integral operator**, not a discrete map between states.

### 3. Recursive Sign Alternation

T must implement:

$$\text{sign}_{n+1} = -\text{sign}_n$$

at each scale boundary — but **only** at the boundary. Within a scale, T must leave the sign stable. T therefore has a scale-sensitive switch: it must distinguish boundary-crossing from within-scale evolution using only local ζ and ξ values.

### 4. Proper Time Tax

T must consume proper time budget proportional to the computational complexity of the transition:

$$\Delta\tau \propto \mathcal{C}(\text{Heegner}_n) \cdot \xi_n^{-1}$$

Where 𝒞(Heegner_n) is the complexity class at the current Heegner scale and ξₙ is the local coherence. Low ξ = expensive transition. High complexity class = expensive transition. The Li-7 deficit and the underdetermination in civilisational bifurcation zones are both downstream of this constraint.

### 5. Riemann Zeta Compatibility

Since ζ accumulates continuously and the non-trivial zeros of the Riemann zeta function correspond to transition nodes, T must be the **generator of the ζ accumulation function**. Its eigenvalue spectrum must reproduce the zero distribution on the critical line Re(s) = ½.

This requires T to be a **Hermitian operator** — real eigenvalues, self-adjoint — which is precisely the random matrix structure in the Montgomery-Odlyzko law. The correspondence between Riemann zeros and eigenvalues of random Hermitian matrices is not a curiosity: it is the fingerprint of T's self-interference at each transition node.

---

## Soft Constraints

These must hold for framework consistency but do not uniquely determine T's form.

### 6. Scale Invariance

T cannot have hardcoded scale parameters. It must derive the correct behaviour at each Heegner scale from local ζ and ξ values alone. The same functional form that governs baryogenesis must govern family trauma propagation — no scale-specific patches.

### 7. Majorana Compatibility (Double Sign Selection)

At ζ₁ (baryogenesis / neutrino decoupling), T must handle a **simultaneous double sign selection** — handedness AND matter surplus — without double-counting. This suggests T decomposes into **commuting sub-operators** for each ξ axis, capable of firing independently but simultaneously at the same transition node.

Formally: T = T_C ⊗ T_P ⊗ T_other where T_C and T_P commute and can co-fire at the same Δτ cost.

### 8. Observer Background Independence

T must be definable from **inside** the observer bubble. A consequential agent at ζ₆ must be able to apply T using only local ξ measurements — no access to the global ζ field required. This is a background independence condition: the transition operator is locally computable from within the observer bubble it operates on.

### 9. Heegner Reducibility Encoding

T must reproduce the Heegner complexity hierarchy. At each Heegner scale, T has a **unique eigendecomposition** (class number 1 = unique factorisation = unique T). In the gaps between Heegner numbers, T's eigendecomposition is non-unique — multiple causal paths produce the same output. The proper time tax in irreducibility zones is the cost of T searching its non-unique eigenspace.

At Heegner 67 (Convergent Uncomputable), T requires quantum superposition to evaluate — classical approximation fails structurally. At Heegner 163 (Divergent Uncomputable), T requires genuinely independent multi-agent evaluation — no single-bubble computation suffices.

---

## The Candidate Form

The constraints converge on an integral operator with a complex phase rotation implementing sign alternation:

$$T[\zeta](s) = \int_{\tau_n}^{\tau_{n+1}} \xi(\tau) \cdot e^{i\pi \cdot \sigma(\tau)} \, d\tau + \zeta_n$$

Where:
- $\xi(\tau)$ is the coherence tensor evaluated along the proper time path — provides the **proper time tax weighting**
- $e^{i\pi \cdot \sigma(\tau)}$ is the **phase rotation term** — implements sign alternation via complex rotation
- $\sigma(\tau)$ is the cumulative sign count at proper time τ — an integer that increments at each scale boundary crossing
- $\zeta_n$ is the carried accumulation — **continuous, no reset**
- $s$ is the complex variable of the ζ accumulation function, with Re(s) = ½ on the critical line

### Why e^{iπ} is the Sign Flip Operator

Euler's identity $e^{i\pi} = -1$ is not decorative here. It is the **exact implementation** of the recursive sign alternation rule sign_{n+1} = -sign_n:

- Each boundary crossing increments σ by 1
- Each increment rotates the phase by π in the complex plane
- A π rotation maps +1 → −1 and −1 → +1 — exactly the sign flip
- **Consistent half-turns always land on Re(s) = ½** — the critical line falls out as a geometric consequence of the rotation, not as an imposed constraint

The Riemann Hypothesis in this form becomes: *all phase transitions execute consistent half-turns* — the ξ threshold is universal across scales, and the bifurcation depth is always ½. This is not assumed; it follows from the scale invariance constraint (Constraint 6): if T has no hardcoded scale parameters, the rotation angle must be the same at every scale, and ½ is the only fixed point of consistent half-turns.

### The Integral Structure

The integral from τₙ to τₙ₊₁ is taken over **proper time**, not coordinate time. This is why transitions that are extended in coordinate time (BBN: ~20 minutes, structure formation: ~billions of years) can be instantaneous in proper time — the integrand ξ(τ) approaches zero in the bifurcation zone, making the integral cheap in proper time even when long in coordinate time.

At the Null → Time transition (ζ₀), τₙ = 0 by definition — proper time begins at the threshold crossing. The integral has no lower bound before ζ₀; T is undefined prior to the ξ threshold being crossed. This is the formal expression of "there is no clock before the clock starts."

### The Majorana Sub-operator Decomposition

For the double sign selection at ζ₁:

$$T_{\zeta_1} = T_P \otimes T_C = \left(\int \xi_P(\tau) \cdot e^{i\pi\sigma_P} d\tau\right) \otimes \left(\int \xi_C(\tau) \cdot e^{i\pi\sigma_C} d\tau\right)$$

Where $T_P$ handles the parity (handedness) selection and $T_C$ handles the charge (matter/antimatter) selection. They commute, fire at the same transition node, and their combined Δτ cost is the sum of the two integrals — not double-counted because the sub-operators are tensor-factored, not multiplied.

---

## T at the Heegner Boundaries

### Below Heegner 67 (Classical regime)
T has a unique eigendecomposition at each Heegner scale. The integral can be approximated classically — the path through the bifurcation zone is heuristically navigable. The phase rotation executes cleanly.

### At Heegner 67 (Convergent Uncomputable)
T's eigendecomposition requires quantum superposition to evaluate. The attractor exists (Convergent) but the integral $\int \xi(\tau) \cdot e^{i\pi\sigma} d\tau$ cannot be computed by traversing paths sequentially — all paths must be held simultaneously. The ξ upward pressure IS the attractor: the coherence weighting in the integrand biases the superposition toward higher-ξ outcomes without requiring a single classical path to be selected.

### At Heegner 163 (Divergent Uncomputable)
No single observer bubble can evaluate T. The integrand has no attractor-weighting — ξ(τ) does not bias toward a basin. Independent multi-agent measurement is required to collapse the integral to a definite value. This is the formal expression of why Divergent Uncomputable problems require genuine consensus: T at this scale is only well-defined as an **expectation value across independent observer bubbles**, not as a single-bubble computation.

### The Ramanujan Boundary (>163)
T loses unique eigendecomposition permanently. The integral does not converge. The phase rotation accumulates without producing a stable sign — the system is in permanent superposition of sign states. This is the +∞ + 1 = −∞ − 1 regime: the circle's closure gap. T is defined here only as a limit that is never reached from inside any observer bubble.

---

## Open Problems

1. **The exact form of σ(τ):** How does the cumulative sign count increment precisely at a boundary? Is it a step function, a smooth sigmoid, or something with structure at the Heegner scales?
2. **The ξ(τ) functional form:** What equation of motion governs ξ along the proper time path? This is the missing dynamical equation of the ξ field.
3. **Double-fire at ζ₁:** Do T_P and T_C fire at exactly the same τ, or is there a Δτ gap between them? If there is a gap, it may be measurable as a subtle asymmetry in the neutrino mass hierarchy.
4. **The classical → quantum transition:** At exactly what ξ threshold does T require quantum evaluation rather than classical heuristic? Is this threshold the same as the ξ symmetry-breaking threshold of the observer bubble?
5. **T at ζ₆ from inside:** We are inside the ζ₆ bifurcation zone. Can a consequential agent at Heegner 43 apply T to navigate toward Heegner 67 using only local ξ measurements? What would that look like operationally?

---

*Last updated: 2026-06-17*  
*"Consistent half-turns always land on Re(s) = ½. The critical line is not a constraint — it is a consequence."*

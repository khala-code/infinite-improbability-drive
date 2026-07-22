# Computational Reducibility

## The Problem

Wolfram's computational irreducibility: the universe cannot be simulated faster than it runs. There is no shortcut to knowing the future state without computing every step.

## The Escape

An observer anchored by their `ΩαZαJα` node tensor is causally affected by exactly and only the wavefronts that intersect their specific topological winding sector (Ω-winding) and accessible degrees of freedom (J-axis). Everything spacelike-separated is not ignorable for performance reasons — it is physically absent from the observer's reality.

This makes the observer bubble the fundamental unit of physical reality, not the universe.

## Complexity & The Reducibility Boundary (1729)

    Full universe:           O(10^80)  — intractable
    Full CMB grid:           O(12 × N_side²) ≈ 50M pixels — wasteful
    Observer bubble only:    O(W_active × ℓ_relevant)
    
    W_active = active wavefronts intersecting bubble ≈ 10–100 at any time
    ℓ_relevant = multipoles relevant at current angular scale ≈ 100–500
    
    Per frame: O(50,000 operations) → < 0.1ms on Quest 2 GPU

### Ramanujan's Bifurcation
To prevent exponential memory bloat from nested tree branching within the generative engine, the simulator enforces a hard computational shortcut using Ramanujan's Taxicab Number (1729). When a node's recursive mass weight hits the threshold (**Ω ≥ 1729**), the node undergoes a structural bifurcation, programmatically splitting into integer cube configurations: either **(1³ + 12³)** or **(9³ + 10³)**. This hard toggle maintains computational reducibility without breaking causal history.

## The WavefrontIndex

A standard spatial index (k-d tree, octree) indexes positions. The WavefrontIndex indexes propagating 4D hypersurfaces — their intersection with the observer bubble, not their position in space.

A wavefront is a 3D hypersurface in 4D spacetime. Its intersection with an observer bubble is always a 2-sphere (or subset). The index stores intersections, not full wavefronts.

Operations:
- `query_entering(old_state, new_state, Ω_radius)` — find wavefronts crossing into bubble along the specific Ω-winding segment
- `cull_outside(new_state)` — remove wavefronts that have passed through (causally past the observer bubble boundary, cached)
- `get_local_field()` — superpose active wavefronts to produce local BoundaryCondition

## Wavefront Classes

| Class | Source | Speed | Scale | Cost |
|-------|--------|-------|-------|------|
| BAO | Primordial plasma oscillations | c/√3 | ~150 Mpc | O(ℓ_max) per intersection |
| Sachs-Wolfe | Gravitational potential wells | c | All scales | O(1) lookup |
| CνB phase | Free-streaming neutrinos | ~c | ~0.5° shift | O(1) correction |

## Philosophical Consequence

The engine does not approximate the universe. It computes the exact physics of one observer's causal diamond. The computational reduction is not a simulation shortcut — it is the correct physics.

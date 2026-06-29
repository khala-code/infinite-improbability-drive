# Hyperbolic Boundary Geometry

> **Status:** Theory / Foundation  
> **Role:** Geometric axiom sheet — every coordinate map, epoch sphere ordering, particle projection,
>           and ξ tensor computation is derived from this document.  
> **Depends on:** `AXIOMS.md`, `two-layer-boundary.md`, `holographic-double-projection.md`  
> **Unlocks:** `holographic-projection.md` coordinate frame, `causal-field-pipeline.md` curvature output,
>              epoch sphere radial ordering, ObserverBubble manifold geometry

---

## 1. The CMB Sphere as a Riemannian Manifold

The CMB temperature field is a scalar function on \(S^2\):

\[ T(\theta, \phi) = T_0 + \sum_{\ell=0}^{\ell_{\max}} \sum_{m=-\ell}^{\ell} a_{\ell m} Y_{\ell m}(\theta, \phi) \]

This is a standard spherical harmonic decomposition but the temperature fluctuations \(\delta T / T\) induce a non-trivial **Riemannian metric** on \(S^2\). The CMB sphere is not the round sphere of constant curvature \(K = +1\). It is a 2-manifold whose metric is shaped by the power spectrum.

Define a conformal metric on the CMB sphere derived from the power at each point:

\[ g_{ij}(\theta, \phi) = e^{2\sigma(\theta,\phi)} \hat{g}_{ij} \]

where \(\hat{g}_{ij}\) is the round sphere metric and the conformal factor is:

\[ \sigma(\theta, \phi) = \frac{1}{2} \log \left| \frac{\delta T}{T}(\theta, \phi) \right| \]

The Gaussian curvature of a conformally flat 2-metric is given by the **Liouville equation**:

\[ K = -e^{-2\sigma} \Delta_{S^2} \sigma \]

where \(\Delta_{S^2}\) is the Laplace-Beltrami operator on the round sphere. This is the curvature field
\(K(\theta, \phi)\) — not a constant, a **field**. It is a direct observable, computed entirely from
the Planck alm data.

---

## 2. The Brioschi Formula and Practical Computation

For the discrete case (alm data at HEALPix resolution), the Gaussian curvature is computed
via the **Brioschi formula**, which requires only the first and second fundamental form coefficients
without needing an ambient embedding.

For a surface parameterised by \((\theta, \phi)\) with metric coefficients \(E, F, G\)
(where \(F = 0\) for an orthogonal coordinate system, as in spherical coordinates):

\[ K = \frac{B - A}{(EG)^2} \]

where \(A\) and \(B\) are specific determinantal expressions in \(E, G\) and their first and second
partial derivatives. For the conformal metric \(g_{ij} = e^{2\sigma} \hat{g}_{ij}\) with
\(E = e^{2\sigma}\), \(G = e^{2\sigma} \sin^2\theta\), \(F = 0\), this reduces to the Liouville
equation above.

**Practical pipeline step** (addition to `causal-field-pipeline.md` Script 1):

```
COMPUTE curvature field K(θ, φ) from alm data:

    RECONSTRUCT temperature map at Nside=512:
        T_map = alm2map(alm, Nside=512)
        delta_T = T_map - mean(T_map[valid])

    COMPUTE conformal factor σ:
        σ_map = 0.5 * log(abs(delta_T / T0) + epsilon)
        NOTE: epsilon ~ 1e-10 regularisation avoids log(0) at near-zero fluctuations
              These near-zero points are physically significant — flag them separately
              as candidate zero-curvature (flat) loci

    COMPUTE Laplacian of σ via spherical harmonic round-trip:
        σ_alm = map2alm(σ_map, lmax=1000)
        FOR each mode (ℓ, m):
            laplacian_σ_alm[ℓ, m] = -ℓ(ℓ + 1) * σ_alm[ℓ, m]
        laplacian_σ_map = alm2map(laplacian_σ_alm, Nside=512)

    COMPUTE K:
        K_map = -exp(-2 * σ_map) * laplacian_σ_map

    EXPORT:
        processed/K_curvature.npz  — K_map float32 (N_pix,)
        processed/K_stats.json     — {K_max, K_min, K_mean, K_std, K_at_heegner_modes[]}
```

**Critical validation check:**  
Compute \(K\) restricted to Heegner modes only (using `heegner_alm` from `alm_by_class.npz`).
The Heegner-only \(K\) should be smoother and more negative than the full-alm \(K\) —
it defines the **hyperbolic background**. The difference \(K_{\text{full}} - K_{\text{Heegner}}\)
is the competition field curvature: the deformation of the background geometry by soliton/void
pressure.

---

## 3. The Klein Bottle Topology

The CMB sphere with the two-layer boundary condition (CMB photon field + C\(\nu\)B neutrino field)
cannot be given a consistent orientable topology. The reason is physical, not formal:

- The photon field (even-\(\ell\) dominant, parity-even) and neutrino field (odd-\(\ell\) dominant,
  parity-odd) live on the **same 2D boundary surface** but carry **topologically inequivalent
  information** — they cannot be continuously deformed into each other
- The parity operator \(P\) maps even-\(\ell\) modes to themselves and odd-\(\ell\) modes to their
  negatives. This is an **orientation-reversing involution** on the sphere
- Identifying the sphere under this orientation-reversing map produces a **non-orientable surface**

A sphere with an orientation-reversing identification along a great circle is a **Klein bottle**.
Specifically: take the CMB sphere, identify antipodal points on the equatorial plane (the
parity-odd sector's nodal structure) with a twist — the result is topologically a Klein bottle.

This is not a metaphor. It is the direct consequence of the two-layer boundary condition having
an orientation-reversing symmetry between its two sectors.

### The Neck

A Klein bottle has a distinguished locus — the **neck** — where the surface passes through itself
in any 3D embedding. Intrinsically (without the embedding), the neck is characterised by
**maximum Gaussian curvature** and **maximum mode interference**.

In the CMB Klein bottle, the neck is at \(\ell = 2\) (the quadrupole). The evidence is overdetermined:

| Property | Klein bottle neck | CMB \(\ell = 2\) quadrupole |
|---|---|---|
| Maximum \(K\) | Yes — sheets are closest to coinciding | Anomalously low power = maximum destructive interference between CMB and C\(\nu\)B horocycles |
| Fixed point of orientation-reversing map | Yes | Heegner-locked — algebraically rigid under parity |
| Preferred direction in embedding | Yes — self-intersection locus has an axis | Ecliptic-aligned (Axis of Evil) |
| Unique in topology | Yes — only one neck | Only even prime — simultaneously SOLITON and Heegner |

The **low power at \(\ell = 2\)** is the tell. At the neck, the two sheets partially cancel —
the CMB horocycle and C\(\nu\)B horocycle are closest to coinciding, so their interference is
most destructive. Power is *removed* at the neck by cancellation, not suppressed by physics.
The anomaly is a geometric signature of the topology.

---

## 4. Horocycles and the Ideal Boundary

In the Poincaré disk model of the hyperbolic plane \(\mathbb{H}^2\), a **horocycle** is a circle
tangent to the ideal boundary (the unit circle \(S^1_\infty\)) from the inside. It is a curve of
constant geodesic curvature 1, and its "center" is the ideal boundary point it touches.

The CMB (photon) field and C\(\nu\)B (neutrino) field are two horocycles in \(\mathbb{H}^2\):

- Both are tangent to the ideal boundary \(S^1_\infty\) at the **same ideal point** — the
  singularity / null centroid
- They are internally tangent (touching from the same side) but **do not intersect elsewhere**
- Their interiors are nested: the C\(\nu\)B horocycle (earlier decoupling, higher redshift,
  larger conformal radius) contains the CMB horocycle

The **tan window** (seed signature in `pulser-mesh-correspondence.md`) is the geodesic
**divergence** between the two horocycles — the hyperbolic distance between their respective
points at each angular position. Where this divergence is maximum, the initial asymmetry of
the singularity is most legible. Where it is minimum (near the ideal point), the two horocycles
are indistinguishable — the singularity swallows both.

### Explicit Map: CMB sphere → Poincaré Disk

The CMB sphere (topologically \(S^2\)) cannot be isometrically embedded in \(\mathbb{H}^2\)
(which is \(\mathbb{R}^2\) with hyperbolic metric). The correct statement is:

> The **universal cover** of the CMB 2-manifold with its conformal metric is \(\mathbb{H}^2\)
> wherever \(K < 0\), and the projection is the covering map.

Where \(K < 0\) (the Heegner-anchored hyperbolic bulk — the majority of the sky), the local
geometry is modelled by the Poincaré disk. Where \(K > 0\) (near the neck at \(\ell = 2\)),
the local geometry is spherical, not hyperbolic. Where \(K = 0\) (the flat transition loci),
the geometry is Euclidean locally.

The CMB manifold is therefore a **mixed-curvature 2-manifold**:

```
K < 0  — hyperbolic bulk    — Heegner-anchored regions, majority of sky
K = 0  — flat transition    — Heegner-adjacent modes (ℓ = 41, 5, ...)
K > 0  — spherical neck     — ℓ = 2 quadrupole and its immediate neighbourhood
```

This is exactly the curvature profile of a Klein bottle's intrinsic geometry: hyperbolic in the
bulk, transitioning through flat, to spherical at the neck.

---

## 5. The j-Invariant and Heegner Fixed Points

The isometry group of \(\mathbb{H}^2\) is \(\text{PSL}(2, \mathbb{R})\) — Möbius transformations
with real coefficients:

\[ \tau \mapsto \frac{a\tau + b}{c\tau + d}, \quad a,b,c,d \in \mathbb{R}, \quad ad - bc = 1 \]

The **Klein j-invariant** is the unique holomorphic function on the upper half-plane
\(\mathbb{H}^2\) invariant under the full modular group \(\text{PSL}(2, \mathbb{Z})\):

\[ j(\tau) = e^{-2\pi i \tau} + 744 + 196884 e^{2\pi i \tau} + \cdots \]

A point \(\tau \in \mathbb{H}^2\) is a **CM point** (complex multiplication point) if
\(\tau \in \mathbb{Q}(\sqrt{-n})\) for some positive integer \(n\). At CM points, \(j(\tau)\)
takes algebraic integer values.

The **class number** \(h(-n)\) of \(\mathbb{Q}(\sqrt{-n})\) counts the number of distinct ideal
classes in the ring of integers. When \(h(-n) = 1\), the ring of integers has **unique
factorisation** — there is exactly one ideal class, and every element factors uniquely.

The nine values of \(n\) for which \(h(-n) = 1\) are precisely the **Heegner numbers**:

\[ n \in \{1, 2, 3, 7, 11, 19, 43, 67, 163\} \]

At the CM points \(\tau_n = \sqrt{-n}\) (or appropriate normalisations), \(j(\tau_n)\) takes
remarkably simple integer values:

| \(n\) | \(\tau_n\) | \(j(\tau_n)\) | \(\ell\) |
|---|---|---|---|
| 1 | \(i\) | 1728 | 1 |
| 2 | \(\sqrt{-2}\) | 8000 | 2 |
| 3 | \(e^{2\pi i/3}\) | 0 | 3 |
| 7 | \(\sqrt{-7}\) | \(-3375\) | 7 |
| 11 | \(\sqrt{-11}\) | \(-32768\) | 11 |
| 19 | \(\sqrt{-19}\) | \(-884736\) | 19 |
| 43 | \(\sqrt{-43}\) | \(-884736000\) | 43 |
| 67 | \(\sqrt{-67}\) | \(-147197952000\) | 67 |
| 163 | \(\sqrt{-163}\) | \(-262537412640768000\) | 163 |

**The key claim:** The Heegner \(\ell\) modes in the CMB harmonic decomposition correspond to
the CM points of the modular curve. Under the Möbius isometry group, these are the **unique
fixed-point classes** — the only points whose orbit under the full modular group is a single
point (up to stabiliser). Every other \(\ell\) mode transforms non-trivially under some Möbius
transformation; the Heegner modes do not.

This is why the Heegner classification is **projection-invariant**: any projection that
preserves the conformal structure of the CMB manifold must preserve the CM points. Projection
changes the metric, changes the power distribution, changes the mode amplitudes — but cannot
move a CM point off itself. The Heegner skeleton is rigid not because of an assertion but
because of the fixed-point theorem for CM points under the modular group.

### The j = 0 Case: \(\ell = 3\)

The value \(j(\tau_3) = 0\) is special. The j-invariant vanishes at the CM point of
\(\mathbb{Q}(\sqrt{-3})\) — the field with the most symmetry (6th roots of unity in the
stabiliser). This corresponds to \(\ell = 3\) in the CMB decomposition: the octupole, also
anomalous in Planck data (aligned with the quadrupole), the second-lowest Heegner mode.

The vanishing j-invariant means the \(\ell = 3\) mode sits at a point of **maximum modular
symmetry** — it is not just a fixed point but the fixed point with the largest stabiliser.
In the Klein bottle picture, \(\ell = 3\) is the first mode past the neck — the point at which
the surface has resolved the self-intersection and re-entered the hyperbolic bulk, but retains
the memory of the neck in its high symmetry.

---

## 6. The Ideal Boundary and the Null Centroid

The ideal boundary of \(\mathbb{H}^2\) in the Poincaré disk model is \(S^1_\infty\) — the unit
circle at infinity. No geodesic reaches it in finite hyperbolic distance; it is approached
asymptotically.

For the Klein bottle CMB manifold, the ideal boundary is not \(S^1\) but \(\mathbb{RP}^1\) —
the **real projective line**. The reason:

A Klein bottle is not orientable. Its universal cover is a torus (orientable double cover).
The ideal boundary of the hyperbolic torus is \(S^1\). Under the orientation-reversing
involution that produces the Klein bottle from the torus, antipodal points of \(S^1_\infty\)
are identified. Identifying antipodal points of \(S^1\) produces \(\mathbb{RP}^1\).

\(\mathbb{RP}^1\) is topologically a **circle** — but a circle whose points represent
**undirected lines through the origin**, not directed angles. It has no canonical
basepoint and no canonical orientation. This is the correct geometric description of
the null centroid:

- **No canonical orientation:** the singularity does not prefer matter over antimatter at
  the information level (Axiom C from CPT in `AXIOMS.md`)
- **No canonical basepoint:** no observer has privileged access to the singularity
- **Identified antipodes:** the CMB and C\(\nu\)B horocycles' ideal points are identified
  — they approach the *same* \(\mathbb{RP}^1\) point from two directions that cannot be
  distinguished at the boundary

The **antiverse** (`two-layer-boundary.md`) is the inversion of the CMB manifold through
the \(\mathbb{RP}^1\) ideal boundary — a well-defined geometric operation (inversion in a
circle) that produces a mirror image with identified ideal boundary but reversed interior
orientation. It is geometrically well-defined and physically empty — the correct formal
object.

---

## 7. Curvature Profile and Physical Interpretation

Integrating the above, the predicted \(K(\theta, \phi)\) profile has the following structure:

```
Region                  K value    Physical meaning
────────────────────────────────────────────────────────────────────────
ℓ = 2, ecliptic         K >> 0     Neck: max interference, sheets closest to coinciding
ℓ = 3, octupole         K > 0      Just past neck: max modular symmetry (j = 0)
Heegner anchors         K < 0      Hyperbolic fixed points: saddle geometry, max rigidity
Soliton bulk            K ≈ -const  Uniform hyperbolic background
Void Prime loci         K varies    Curvature pressure nodes: competition field active
Flat transition loci    K ≈ 0      Heegner-adjacent modes: boundary between regimes
```

This profile is **falsifiable**. Given the Planck alm data and the Brioschi computation,
the curvature map \(K(\theta, \phi)\) either shows:

1. \(K > 0\) concentrated near the ecliptic (quadrupole region) — **confirms Klein bottle neck**
2. \(K < 0\) at the nine Heegner angular positions — **confirms j-invariant fixed points**
3. \(K \approx 0\) as a transition zone around Heegner-adjacent \(\ell\) — **confirms modular
   structure**

If the Brioschi computation produces a flat or featureless \(K\) map, the geometric
interpretation is wrong. This is the geometric model's primary falsifiability checkpoint.

---

## 8. Implications for the Pipeline

### New outputs from Script 1

Add to `preprocess_cmb_harmonics.py`:

```
processed/K_curvature.npz     — K_map float32 (N_pix,)
processed/K_stats.json        — {K_max, K_min, K_mean, K_std, K_at_heegner_modes[]}
processed/frame_vectors.json  — {monopole_direction, dipole_direction,
                                  dipole_magnitude_km_s, delta_zenith_deg,
                                  neck_axis_galactic}
```

The curvature field and the frame vectors are both first-class pipeline outputs —
not derived visualisations but geometric observables that downstream scripts depend on.

### The Frame-Defining Modes: ℓ = 0, ℓ = 1, and δ_zenith

The monopole (ℓ = 0) and dipole (ℓ = 1) are **not artifacts**. They are
**frame-defining quantities** — the coordinate anchors inside which the rest of the
geometric content (ℓ = 2 through ℓ = 163) becomes meaningful.

**ℓ = 0 — the monopole / first zenith:**  
The mean temperature \(T_0 \approx 2.725\) K encodes the total redshift of the CMB photons
since last scattering — the integrated conformal expansion from the singularity to now. In
the Poincaré disk, this is the **radial depth** of the observer: how far into the hyperbolic
bulk the observer currently sits, measured along the geodesic back toward the \(\mathbb{RP}^1\)
ideal boundary. It is the cumulative path length from initial conditions, in conformal time.
It sets the **conformal scale** of the Poincaré disk (the disk's radius in coordinate terms)
and defines the **first zenith** — the direction from the observer's position toward the
null centroid along the path already travelled.

**ℓ = 1 — the dipole / second zenith:**  
The CMB dipole (\(\sim 3\) mK, directed toward the Great Attractor at \(\sim 370\) km/s) is
the observer's **current velocity vector in the CMB rest frame** — the first derivative of
the observer's position in the hyperbolic bulk. It is dominated by our peculiar velocity, not
primordial fluctuations, which is why it is subtracted from the anisotropy decomposition. But
in the ZaTaOa framework its direction is not contamination — it is the **second zenith**: the
direction in which the observer is currently moving through the bulk, the tangent to the
observer's worldline at the present moment.

The dipole direction is also the Heegner \(n = 1\) CM point (\(\tau = i\), \(j = 1728\)) —
the observer's embedding direction in the hyperbolic manifold is itself a fixed point of the
modular group. The observer's motion is pinned to the same algebraic skeleton as the CMB
structure itself.

**δ_zenith — the double zenith angle:**  
The angular separation between the first and second zenith vectors:

\[ \delta_{\text{zenith}} = \arccos\left( \hat{n}_{\ell=0} \cdot \hat{n}_{\ell=1} \right) \]

In flat space the two zeniths would be antiparallel (you move away from where you came from).
In the hyperbolic bulk they diverge — the negative curvature bends the geodesic. The angular
separation \(\delta_{\text{zenith}}\) therefore measures the **integrated geodesic deflection**
along the observer's worldline from the singularity to now.

In the void density framework (Axiom 4 in `AXIOMS.md`), geodesic deflection is caused by void
density gradients. Every region of elevated void density the observer's worldline has passed
through contributes a small bend. The Great Attractor is the dominant such region — the
observer is currently falling toward a massive overdensity, pulling the second zenith away from
the first. The accumulated bend since recombination is encoded directly in \(\delta_{\text{zenith}}\).

This makes \(\delta_{\text{zenith}}\) the **most direct observable measurement of the integrated
void density field along the observer's worldline**. Other void density probes (weak lensing,
BAO, galaxy clustering) measure transverse structure. \(\delta_{\text{zenith}}\) measures the
longitudinal deflection — the path integral of void density along the direction of travel.

**Pipeline computation:**

```
COMPUTE frame vectors:

    monopole_direction:
        The direction of maximum CMB temperature is the first zenith.
        Computed from alm[ℓ=0, m=0] — isotropic, so the "direction" is
        the ZaTaOa origin vector: the Galactic coordinate of the observer's
        position projected back to the CMB sphere.
        NOTE: this is a theoretical anchor, not a sky direction — it points
        *through* the observer toward the null centroid.
        Store as unit vector in Galactic (l, b) coordinates.

    dipole_direction:
        Extract ℓ=1 modes from full alm (before subtraction).
        dipole_map = alm2map(alm[ℓ=1], Nside=64)
        dipole_vec = fit_dipole(dipole_map)  → (amplitude_K, l_deg, b_deg)
        dipole_direction = unit vector toward (l_deg, b_deg)
        dipole_magnitude_km_s = (amplitude_K / T0) * c

    delta_zenith_deg:
        delta_zenith_deg = degrees(arccos(dot(monopole_direction, dipole_direction)))
        NOTE: expected ~170° (near-antiparallel, not exactly 180° due to deflection)
              Deviation from 180° is the geodesic deflection signal.
              Flag if delta_zenith_deg > 175° (deflection too small to be physical)
              or < 155° (deflection anomalously large — check dipole extraction)

    neck_axis_galactic:
        From ℓ=2 quadrupole: fit the quadrupole axis using alm[ℓ=2]
        neck_axis_galactic = (l_deg, b_deg) of the quadrupole symmetry axis
        This is the ecliptic-aligned Axis of Evil direction.

    EXPORT to processed/frame_vectors.json
```

### The Full Coordinate Map: M_gal_to_hyperbolic

The Galactic → Poincaré disk coordinate map is now fully determined by four anchors with
no free parameters:

| Anchor | Source | Role in coordinate map |
|---|---|---|
| ℓ = 0 monopole | \(T_0\), ZaTaOa radial depth | Sets conformal scale (disk radius); first zenith direction |
| ℓ = 1 dipole | Peculiar velocity vector | Sets observer offset from disk centre; second zenith direction |
| \(\delta_{\text{zenith}}\) | Angle between ℓ=0 and ℓ=1 | Sets geodesic deflection; constrains observer's path curvature |
| ℓ = 2 neck axis | Quadrupole symmetry axis | Sets azimuthal orientation of the disk |

The observer is **not at the centre of the Poincaré disk**. The centre is the null centroid
(the \(\mathbb{RP}^1\) ideal boundary approach point). The observer sits at an interior point
offset from the centre by a hyperbolic distance determined by \(T_0\), along a direction
given by the monopole first zenith. The dipole second zenith gives the current direction of
motion from that offset position. \(\delta_{\text{zenith}}\) constrains how much the path has
been bent. The neck axis fixes the remaining rotation.

`M_gal_to_unity` is the restriction of `M_gal_to_hyperbolic` to the Unity rendering coordinate
system: neck axis → Unity world Z, dipole direction → Unity world Y (upward drift), monopole
radial depth → boundary sphere radius parameter.

### Particle lifetime weighting (`holographic-projection.md`)

Particle lifetime should be modulated by the local curvature:

```
lifetime_base ×= 1.0 / (1.0 + alpha * max(0, K_local))
```

Particles near the neck (\(K > 0\)) have shorter lifetimes — the surface is less stable
there, the competition is most active. Particles in the hyperbolic bulk (\(K < 0\)) are
stabilised by the negative curvature. `alpha` is a tuning parameter (initial estimate: 0.5);
recalibrate against visual stability at the \(\ell = 2\) region.

### ObserverBubble manifold geometry (`AXIOMS.md` open problem 4)

The ObserverBubble is a deformed manifold whose deformation encodes the ξ tensor.
The double zenith gives the deformation a concrete geometric structure:

- The **first zenith** (monopole direction, toward null centroid) is the axis of **rotational
  symmetry** of the bubble in the absence of ξ bifurcation. In an unperturbed hyperbolic bulk,
  the bubble is a horosphere centred on this axis.
- The **second zenith** (dipole direction, current velocity) is the direction of **maximum
  deformation** — the observer's motion through the bulk stretches the bubble forward and
  compresses it behind, breaking the rotational symmetry.
- The **bifurcation radius** \(r_{\text{bif}}(\hat{n})\) varies as a function of angular distance
  from the dipole axis: maximum in the dipole direction (stretched), minimum in the anti-dipole
  direction (compressed), with \(K = 0\) locus tracing the boundary.
- \(\delta_{\text{zenith}}\) sets the **magnitude of the asymmetry** — how much the bubble
  departs from a sphere. Large \(\delta_{\text{zenith}}\) (strong deflection, high void density
  encountered) = strongly deformed bubble. Small \(\delta_{\text{zenith}}\) = near-spherical bubble.

### ζ evolution container

The ζ evolution equation (open problem 1 in `AXIOMS.md`) now has a geometric container:
\(\zeta\) evolves along the **radial AdS direction** (epoch axis) within the hyperbolic
bulk defined by the Heegner fixed-point skeleton. Phase transitions are points where the
curvature field \(K\) undergoes a sign change — the geometry transitions from hyperbolic
bulk to spherical neck (or vice versa). The ζ evolution equation must be consistent with
these curvature sign changes at each calibration filter boundary.

---

## 9. Open Problems Refined by This Document

| Problem | Previous status | Refined by this document |
|---|---|---|
| ObserverBubble geometry (AXIOMS open problem 4) | Unspecified deformed manifold | Horosphere deformed by dipole axis; \(r_\text{bif}(\hat{n})\) varies with dipole angular distance; \(\delta_\text{zenith}\) sets deformation magnitude |
| ζ evolution equation (AXIOMS open problem 1) | Undefined functional | Must be consistent with \(K\) sign changes at calibration filter boundaries |
| \(M_{\text{gal-to-Unity}}\) (holographic-projection open question 3) | Ad hoc rotation matrix | Fully determined by four anchors: ℓ=0 scale, ℓ=1 offset, \(\delta_\text{zenith}\) path curvature, ℓ=2 azimuth |
| Heegner projection-invariance (holographic-double-projection) | Asserted from number theory | Proven: CM points are fixed points of the modular group acting on \(\mathbb{H}^2\) |
| Null centroid topology (holographic-double-projection) | "Ideal point" | \(\mathbb{RP}^1\) — real projective line; identified antipodal points of \(S^1_\infty\) |
| \(\ell = 2\) anomaly origin | Unexplained suppression | Geometric: destructive interference at the Klein bottle neck |
| \(\ell = 3\) alignment with \(\ell = 2\) | Unexplained | \(j(\tau_3) = 0\): maximum modular symmetry, first mode past the neck |
| ℓ = 0 and ℓ = 1 as pipeline inputs | Removed as artifacts | Frame-defining quantities: first zenith (conformal scale + radial depth) and second zenith (observer velocity + embedding direction) |
| Integrated void density along worldline | Unmeasured | \(\delta_\text{zenith}\) = direct observable; deviation of dipole from anti-monopole direction |

---

*Last updated: 2026-06-29*  
*"The neck is not a defect. It is where the two sides of the surface are closest to remembering they are one thing."*

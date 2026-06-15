# Bug Danger Zones

Known sources of subtle, hard-to-diagnose errors. Read before touching the relevant systems.

## 1. Coordinate Handedness (High Risk)

Galactic coordinates are right-handed. Unity is left-handed.

Failing to account for this produces a CMB render that is mirrored — it looks correct because the CMB is nearly isotropic, but any asymmetric features (the CMB dipole, specific hot/cold spots) will be on the wrong side.

**Transform:**
```
Unity.x = -cos(b) * cos(l)   // flip x for handedness
Unity.y =  sin(b)
Unity.z =  cos(b) * sin(l)
```

Test: the CMB dipole should point toward (l=264°, b=48°) in Galactic coords — verify in Unity scene.

## 2. Monopole and Dipole Removal (High Risk)

The raw Planck FITS file includes:
- Monopole (mean temperature ~2.725 K) — must be subtracted
- Dipole (Earth's motion through CMB, ΔT ≈ 3.35 mK) — should be subtracted for primordial CMB

Failing to remove these produces a render dominated by the dipole — a large hot/cold gradient that obscures all primordial structure.

**In Python preprocessing:**
```python
import healpy as hp
map_data = hp.read_map('COM_CMB_IQU-smica_2048_R3.00_full.fits')
alm = hp.map2alm(map_data)
alm[0] = 0  # remove monopole (l=0)
alm[1:4] = 0  # remove dipole (l=1)
map_clean = hp.alm2map(alm, nside=2048)
```

## 3. HEALPix Ring vs Nested Ordering (Medium Risk)

HEALPix maps come in two pixel orderings: RING and NESTED. healpy defaults to RING. Confusing them produces a scrambled texture that looks like noise.

Always check: `hp.read_map(..., verbose=True)` reports the ordering. Pass `nest=True` to `hp.ring2nest()` if conversion needed.

## 4. Neutrino Temperature Factor (Medium Risk)

The CνB temperature is NOT equal to CMB temperature. The correct relation:

    T_ν = (4/11)^(1/3) × T_γ ≈ 1.945 K

This factor arises from electron-positron annihilation heating the photon bath after neutrino decoupling. Using T_γ directly for the neutrino field overstates its temperature by ~40%.

## 5. ΩaZaTa Coordinate Uniqueness (Medium Risk)

No two nodes can occupy the same ΩaZaTa coordinate. The engine does not enforce this automatically — it is the caller's responsibility to check before inserting a new node.

Collisions produce undefined superposition behaviour. Add an assertion in `SpacetimeCoordinate.cs` during development.

## 6. WavefrontIndex Cull Timing (Low Risk, Subtle)

Cull stale wavefronts BEFORE querying new intersections, not after. Culling after means a wavefront that passed through the bubble on the current frame is still present during field evaluation — it contributes a physically incorrect term for one frame.

Order: `cull_outside()` → `query_entering()` → `get_local_field()`.

## 7. Quest 2 Thermal Throttling (Platform Risk)

Quest 2 throttles GPU at sustained load. The wavefront evaluation budget of < 0.1ms assumes no throttling. Under sustained navigation, budget for 0.15–0.2ms.

Profile with OVR Metrics Tool. Keep eye-tracked foveated rendering enabled — the CMB skybox is full-sphere and expensive at full resolution.

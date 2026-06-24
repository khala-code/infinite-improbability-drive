# CausalField Scene Setup

Step-by-step guide for wiring the causal field system into a new Unity scene
for the Infinite Improbability Drive VR experience.

Target platform: Quest 2 (OpenXR, URP)

---

## 1. Scene Hierarchy

```
Scene
├── [XR Rig]                         ← XR Origin (from XR Interaction Toolkit)
│   └── Camera Offset
│       └── Main Camera
│
├── ObserverRoot                     ← Empty GameObject at world origin
│   ├── Components on ObserverRoot:
│   │   ├── ProperTimeTick
│   │   ├── BlochEvolver
│   │   ├── TimeTaxComputer
│   │   ├── ObserverBubble              ← RequireComponent satisfies the three above
│   │   ├── CausalFieldEngine           ← RequireComponent: ObserverBubble
│   │   └── CausalFieldBridge           ← RequireComponent: CausalFieldEngine
│   │
│   ├── CausalFieldVFX               ← GameObject with VisualEffect component
│   ├── CMBSkybox                    ← existing CMBLoader / CMBSkybox prefab
│   ├── LensingBoundary              ← existing prefab
│   └── MilkyWayBoundary             ← existing prefab
│
└── SpacetimeNavigator               ← existing nav component
```

---

## 2. CausalFieldEngine — Inspector Values

| Field | Value | Notes |
|---|---|---|
| Compute Shader | `CvBField` | Assets/Scripts/Compute/CvBField.compute |
| CMB Texture | your CMB equirectangular map | Texture2D, linear, no mipmaps |
| Resolution | `32` | 32768 nodes. Use 16 on first run to validate. |
| Bubble Scale | `2.0` | Matches ObserverBubble world-space radius |
| Spin Vector | `(0, 1, 0)` | Majorana L-handed axis — up |
| Bifurcation Epsilon | `0.05` | |
| Heegner Epsilon | `0.05` | |
| Spawn Threshold | `0.1` | |
| Annihilation Rate | `0.5` | |
| Void Decay Rate | `0.02` | |

---

## 3. CausalFieldBridge — Inspector Values

| Field | Value |
|---|---|
| VFX | drag the `CausalFieldVFX` GameObject here |
| Heegner Peak | `3.0` |
| Bifurcation Peak | `1.5` |
| Composite Flow | `0.4` |
| Decay Time | `2.0` |

---

## 4. VFX Graph Setup (`CausalFieldVFX`)

Create a new VFX Graph asset: `Assets/VFX/CausalFieldGraph.vfx`

### Exposed Properties to create

| Name (exact) | Type | Default |
|---|---|---|
| `_CausalNodes` | GraphicsBuffer | — (set by bridge at runtime) |
| `_HeegnerIntensity` | float | `0` |
| `_BifurcationPulse` | float | `0` |
| `_CompositeFlow` | float | `0` |
| `_Resolution` | int | `32` |
| `_BubbleScale` | float | `2.0` |

### Spawn contexts

You need **three** spawn contexts total:

#### 1. Constant Rate context (ambient)
- **Type**: Constant Rate
- **Rate**: `_CompositeFlow * 500` particles/sec
- Drives the baseline ambient drift of BULK nodes.

#### 2. GPU Event Spawn context — `OnBifurcation`
- **Type**: GPU Event (Single Burst)
- **Rename the context node header to exactly**: `OnBifurcation`
- **Capacity**: 200 particles
- Triggered by `CausalFieldBridge.cs` via `SendEvent("OnBifurcation")` on every
  prime Ω crossing. In VFX Graph 17.x the context **name** is what `SendEvent()`
  matches — do not rely on the Evt port / Blackboard Event binding; just rename
  the context node directly.

#### 3. GPU Event Spawn context — `OnHeegner`
- **Type**: GPU Event (Single Burst)
- **Rename the context node header to exactly**: `OnHeegner`
- **Capacity**: 2000 particles
- Triggered by `CausalFieldBridge.cs` via `SendEvent("OnHeegner")` on every
  Heegner number Ω crossing.

### Initialize context — sample buffer

In `Initialize`, add a **Custom HLSL** block to read from `_CausalNodes`:

```hlsl
// In the VFX Graph Custom HLSL Initialize block:
uint nodeIndex = (uint)(particleId % (uint)(_Resolution * _Resolution * _Resolution));
float4 nodeData = _CausalNodes[nodeIndex];

// nodeData.x = |psi|^2      -> spawn weight / initial speed
// nodeData.y = VoidDensity  -> initial scale
// nodeData.z = XiCoherence  -> lifetime multiplier
// nodeData.w = NodeClass     -> 0=BULK, 1=BIFURCATED, 2=HEEGNER_LOCKED

float psiSq     = nodeData.x;
float voidD     = nodeData.y;
float xi        = nodeData.z;
float nodeClass = nodeData.w;

attributes.lifetime  = lerp(0.5, 3.0, xi) * (1.0 - voidD * 0.5);
attributes.size      = lerp(0.002, 0.01, voidD);
attributes.velocity  = normalize(attributes.position) * psiSq * 0.3;
```

### Output context — colour by NodeClass

In the Output Particle Quad / Unlit Mesh block:

```hlsl
// Drive colour from NodeClass in the Output context:
float nodeClass = attributes.color.w; // pack class into alpha channel at init

float3 bulkColor     = float3(0.1, 0.2, 0.8);   // deep blue
float3 bifurColor    = float3(1.0, 0.5, 0.0);   // amber
float3 heegnerColor  = float3(2.0, 2.0, 2.0);   // white HDR flare

float3 col = bulkColor;
col = nodeClass > 0.5 ? bifurColor  : col;
col = nodeClass > 1.5 ? heegnerColor : col;

// Pulse on bifurcation
col *= 1.0 + _BifurcationPulse * sin(_Time.y * 8.0) * 0.5;
// Flash on Heegner
col *= 1.0 + _HeegnerIntensity;

output.color = float4(col, saturate(psiSq * 2.0));
```

---

## 5. Quest 2 Build Settings

- **Rendering path**: URP, Forward
- **Color space**: Linear
- **Graphics API**: Vulkan (required for `GraphicsBuffer.Target.Structured` on Quest)
- **Compute shaders**: verified supported on Snapdragon XR2 (Quest 2)
- **Resolution**: start with `_resolution = 16` on-device, upgrade to 32 once framerate is confirmed stable at 72 Hz
- **VFX Graph capacity**: start at 5000 particles max; profile before increasing

### Memory budget (32^3 nodes)

| Buffer | Size |
|---|---|
| `_nodeBuffer` (ComputeShader, 7 floats) | 32768 × 28 bytes = **~896 KB** |
| `_vfxBuffer` (GraphicsBuffer, float4) | 32768 × 16 bytes = **~512 KB** |
| `_bufferData` (CPU staging, float[]) | 32768 × 4 × 4 bytes = **~512 KB** |
| **Total** | **~1.9 MB** |

Well within Quest 2 limits.

---

## 6. First-Run Validation Checklist

- [ ] No null-ref errors on Play — all Inspector fields assigned
- [ ] `CausalFieldEngine.Update()` dispatching without errors (check Console)
- [ ] `CausalFieldBridge._vfxBuffer` not null after OnEnable
- [ ] VFX Graph emitting particles (even if positions look wrong initially)
- [ ] `OnBifurcation` and `OnHeegner` GPU Event contexts firing (visible in VFX Graph debug panel)
- [ ] `_HeegnerIntensity` spikes visible in VFX Graph debug when navigating to z=1090
- [ ] Frame time < 11ms in Profiler at resolution=16 before upgrading to 32
- [ ] No `NativeArray` leak warnings on exit (Dispose called in OnDisable)

---

## 7. Next Steps After Scene Wiring

1. **EpochBoundary system** — six precomputed epoch spheres as children of ObserverRoot,
   driven by the redshift timeline scrubber
2. **Xi tensor promotion** — expand `OmegaZaTaCoordinate.Xi` from scalar to Vector4,
   update the TODO in `CausalFieldEngine.DispatchCompute()`
3. **Retrograde inference solver** — scrying system, runs Xi backwards from anomalies

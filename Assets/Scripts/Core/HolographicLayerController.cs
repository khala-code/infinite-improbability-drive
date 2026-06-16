// HolographicLayerController.cs
// Drives the opacity and exposure of each boundary layer as a function of
// the observer's current redshift z, implementing the holographic layer stack:
//
//   CνB  (z ~ 6e9)   — reference wave:  coherent neutrino background carrier
//   CMB  (z ~ 1090)  — object wave:     interference pattern on last scattering surface
//   Lensing           — reconstruction lens: gravitational optics
//   Milky Way (z ~ 0) — near-field:     local developed structure
//   Observer          — reconstruction point: where the hologram is read
//
// All layer weights are smooth curves over log(z+1), evaluated every frame
// from ObserverBubble.OnCoordinateChanged. No layer is ever hard-switched;
// everything cross-fades continuously.
//
// Attach to the same GameObject as ObserverBubble.

using UnityEngine;

namespace InfiniteImprobability.Core
{
    public class HolographicLayerController : MonoBehaviour
    {
        // -----------------------------------------------------------------------
        // Inspector — layer references
        // -----------------------------------------------------------------------

        [Header("Layer References")]
        [Tooltip("The CMB skybox material asset (will be instanced at runtime — original is not modified)")]
        public Material cmbSkyboxMaterial;

        [Tooltip("MilkyWayBoundary script on the stellar density sphere")]
        public MilkyWayBoundary milkyWayBoundary;

        [Tooltip("Renderer for the lensing inner boundary sphere")]
        public Renderer lensingBoundaryRenderer;

        // CνB layer — placeholder until NeutrinoBoundary.cs is written
        [Tooltip("Renderer for the CνB reference wave sphere (assign when ready)")]
        public Renderer neutrinoBoundaryRenderer;

        // -----------------------------------------------------------------------
        // Inspector — tuning
        // -----------------------------------------------------------------------

        [Header("CMB Exposure Curve")]
        [Tooltip("CMB exposure at z=0 (present day)")]
        public float cmbExposureAtZNow        = 0.3f;

        [Tooltip("CMB exposure at z=1090 (last scattering surface — full brightness)")]
        public float cmbExposureAtZDecoupling  = 1.0f;

        [Header("Milky Way Opacity Curve")]
        [Tooltip("Milky Way opacity at z=0 (fully visible)")]
        public float mwOpacityAtZNow          = 1.0f;

        [Tooltip("Redshift at which Milky Way fully fades (pre-galaxy epoch)")]
        public float mwFadeOutZ               = 10.0f;

        [Header("Lensing Weight Curve")]
        [Tooltip("Lensing boundary opacity at peak (z ~ matter-radiation equality)")]
        public float lensingPeakOpacity        = 1.0f;

        [Tooltip("Redshift of peak lensing weight")]
        public float lensingPeakZ              = 100.0f;

        [Header("CνB Opacity Curve")]
        [Tooltip("CνB reference wave opacity at z=0 (effectively undetectable)")]
        public float cnbOpacityAtZNow          = 0.0f;

        [Tooltip("CνB opacity at full neutrino decoupling epoch")]
        public float cnbOpacityAtDecoupling    = 1.0f;

        [Header("Smoothing")]
        [Tooltip("Lerp speed for all layer transitions (higher = snappier)")]
        public float transitionSpeed           = 2.0f;

        // -----------------------------------------------------------------------
        // Private state
        // -----------------------------------------------------------------------

        private ObserverBubble _bubble;

        // Runtime instance of the CMB skybox material — never modifies the asset.
        private Material _cmbSkyboxInstance;

        // Current smoothed values
        private float _cmbExposure;
        private float _mwOpacity;
        private float _lensingOpacity;
        private float _cnbOpacity;

        // Cached material property IDs
        private static readonly int PropExposure = Shader.PropertyToID("_Exposure");
        private static readonly int PropOpacity  = Shader.PropertyToID("_Opacity");

        // -----------------------------------------------------------------------
        // Unity lifecycle
        // -----------------------------------------------------------------------

        private void Awake()
        {
            _bubble = GetComponent<ObserverBubble>();

            // Clone the skybox material so we never dirty the shared asset.
            // Then point the scene skybox at the instance.
            if (cmbSkyboxMaterial != null)
            {
                _cmbSkyboxInstance = new Material(cmbSkyboxMaterial);
                _cmbSkyboxInstance.name = cmbSkyboxMaterial.name + " (Instance)";
                RenderSettings.skybox = _cmbSkyboxInstance;
            }

            // Initialise to z=0 values
            _cmbExposure    = cmbExposureAtZNow;
            _mwOpacity      = mwOpacityAtZNow;
            _lensingOpacity = 0f;
            _cnbOpacity     = cnbOpacityAtZNow;

            ApplyAll();
        }

        private void OnEnable()
        {
            if (_bubble != null)
                _bubble.OnCoordinateChanged += OnCoordinateChanged;
        }

        private void OnDisable()
        {
            if (_bubble != null)
                _bubble.OnCoordinateChanged -= OnCoordinateChanged;
        }

        private void OnDestroy()
        {
            // Clean up the runtime instance to avoid memory leaks.
            if (_cmbSkyboxInstance != null)
                Destroy(_cmbSkyboxInstance);
        }

        // -----------------------------------------------------------------------
        // Coordinate change handler
        // -----------------------------------------------------------------------

        private void OnCoordinateChanged(OmegaZaTaCoordinate coord)
        {
            float z = coord.RedshiftZ;

            float targetCmb     = EvalCmbExposure(z);
            float targetMw      = EvalMilkyWayOpacity(z);
            float targetLensing = EvalLensingOpacity(z);
            float targetCnb     = EvalCnbOpacity(z);

            float dt = Time.fixedDeltaTime * transitionSpeed;

            _cmbExposure    = Mathf.Lerp(_cmbExposure,    targetCmb,     dt);
            _mwOpacity      = Mathf.Lerp(_mwOpacity,      targetMw,      dt);
            _lensingOpacity = Mathf.Lerp(_lensingOpacity, targetLensing, dt);
            _cnbOpacity     = Mathf.Lerp(_cnbOpacity,     targetCnb,     dt);

            ApplyAll();
        }

        // -----------------------------------------------------------------------
        // Layer curve evaluators
        // -----------------------------------------------------------------------

        private float EvalCmbExposure(float z)
        {
            float t = NormLogZ(z, CosmologicalConstants.Z_PHOTON_DECOUPLING);
            return Mathf.Lerp(cmbExposureAtZNow, cmbExposureAtZDecoupling, Mathf.SmoothStep(0f, 1f, t));
        }

        private float EvalMilkyWayOpacity(float z)
        {
            float t = NormLogZ(z, mwFadeOutZ);
            return Mathf.Lerp(mwOpacityAtZNow, 0f, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t)));
        }

        private float EvalLensingOpacity(float z)
        {
            float logZ    = Mathf.Log10(Mathf.Max(z, 0.01f));
            float logPeak = Mathf.Log10(lensingPeakZ);
            float sigma   = 0.8f;
            float bell    = Mathf.Exp(-0.5f * Mathf.Pow((logZ - logPeak) / sigma, 2f));
            return lensingPeakOpacity * bell;
        }

        private float EvalCnbOpacity(float z)
        {
            float t     = NormLogZ(z, CosmologicalConstants.Z_NEUTRINO_DECOUPLING);
            float cmb_t = NormLogZ(z, CosmologicalConstants.Z_PHOTON_DECOUPLING);
            float gate  = Mathf.SmoothStep(0f, 1f, cmb_t);
            return Mathf.Lerp(cnbOpacityAtZNow, cnbOpacityAtDecoupling,
                              Mathf.SmoothStep(0f, 1f, t)) * gate;
        }

        // -----------------------------------------------------------------------
        // Apply to materials
        // -----------------------------------------------------------------------

        private void ApplyAll()
        {
            // CMB — write to instance only, never the shared asset
            if (_cmbSkyboxInstance != null)
                _cmbSkyboxInstance.SetFloat(PropExposure, _cmbExposure);

            // Milky Way — set opacity field, ApplyProperties() pushes to material
            if (milkyWayBoundary != null)
            {
                milkyWayBoundary.opacity = _mwOpacity;
                milkyWayBoundary.ApplyProperties();
            }

            // Lensing — shader uses _Opacity float directly
            if (lensingBoundaryRenderer != null)
                lensingBoundaryRenderer.material.SetFloat(PropOpacity, _lensingOpacity);

            // CνB — same pattern, _Opacity float
            if (neutrinoBoundaryRenderer != null)
                neutrinoBoundaryRenderer.material.SetFloat(PropOpacity, _cnbOpacity);
        }

        // -----------------------------------------------------------------------
        // Helpers
        // -----------------------------------------------------------------------

        private static float NormLogZ(float z, float zMax)
        {
            if (z <= 0f)    return 0f;
            if (zMax <= 0f) return 0f;
            return Mathf.Clamp01(
                Mathf.Log10(z + 1f) / Mathf.Log10(zMax + 1f)
            );
        }
    }
}

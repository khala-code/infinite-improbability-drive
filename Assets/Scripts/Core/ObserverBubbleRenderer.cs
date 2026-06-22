// ObserverBubbleRenderer.cs
// Bridges ObserverBubble runtime state → ObserverBubble.shader material properties.
//
// Responsibilities:
//   • Subscribes to ObserverBubble events (OnCoordinateChanged, OnCoherenceChanged)
//   • Pushes Xi, SpinVector, CoherenceAxis, XiCritical, and deformation params
//     to the material each frame via MaterialPropertyBlock (zero GC allocation)
//   • Scales the bubble mesh transform by BubbleVolume^(1/3) so the physical
//     size of the rendered surface matches the causal diamond volume
//   • Exposes BifurcationRadius(Vector3 direction) for Gizmo drawing and
//     any external systems that need the horizon surface geometry
//
// Setup:
//   1. Attach to the same GameObject as ObserverBubble (or a child).
//   2. Assign a sphere / icosphere mesh as _bubbleMeshFilter.
//   3. Assign the ObserverBubble material (using CMB/ObserverBubble shader).
//   4. Optionally assign an ObserverBubble reference; if null, finds one on Awake.

using UnityEngine;

namespace InfiniteImprobability.Core
{
    [RequireComponent(typeof(MeshRenderer))]
    [RequireComponent(typeof(MeshFilter))]
    public class ObserverBubbleRenderer : MonoBehaviour
    {
        // -----------------------------------------------------------------------
        // Inspector
        // -----------------------------------------------------------------------

        [Header("Dependencies")]
        [Tooltip("Leave null to auto-find on same GameObject or parent.")]
        [SerializeField] private ObserverBubble _bubble;

        [Header("Geometry")]
        [Tooltip("Base radius of the bubble in world units. Shader _RadiusBase mirrors this.")]
        [SerializeField] private float _baseRadius = 1.0f;

        [Tooltip("Deformation strength passed to shader (how much the manifold deforms from sphere).")]
        [SerializeField, Range(0f, 1f)] private float _deformStrength = 0.35f;

        [Header("Visual")]
        [SerializeField, Range(0f, 1f)] private float _globalOpacity = 0.65f;
        [SerializeField, Range(0f, 0.5f)] private float _fluctuationScale = 0.12f;
        [SerializeField, Range(0f, 5f)]   private float _fluctuationSpeed = 1.4f;

        [Header("Debug")]
        [SerializeField] private bool _drawGizmos = true;
        [SerializeField] private int  _gizmoRings = 3;

        // -----------------------------------------------------------------------
        // Shader property IDs — cached to avoid string lookup each frame
        // -----------------------------------------------------------------------

        private static readonly int ID_Xi               = Shader.PropertyToID("_Xi");
        private static readonly int ID_XiCritical       = Shader.PropertyToID("_XiCritical");
        private static readonly int ID_SpinVector       = Shader.PropertyToID("_SpinVector");
        private static readonly int ID_CoherenceAxis    = Shader.PropertyToID("_CoherenceAxis");
        private static readonly int ID_RadiusBase       = Shader.PropertyToID("_RadiusBase");
        private static readonly int ID_DeformStrength   = Shader.PropertyToID("_DeformStrength");
        private static readonly int ID_GlobalOpacity    = Shader.PropertyToID("_GlobalOpacity");
        private static readonly int ID_FluctuationScale = Shader.PropertyToID("_FluctuationScale");
        private static readonly int ID_FluctuationSpeed = Shader.PropertyToID("_FluctuationSpeed");

        // -----------------------------------------------------------------------
        // Internal
        // -----------------------------------------------------------------------

        private MeshRenderer          _renderer;
        private MaterialPropertyBlock _props;

        // Cached values from last coordinate update
        private float   _xiCurrent;
        private Vector3 _spinVec;
        private Vector3 _coherenceAxis;
        private float   _bubbleVolume;
        private bool    _isCoherent;

        // -----------------------------------------------------------------------
        // Unity lifecycle
        // -----------------------------------------------------------------------

        private void Awake()
        {
            _renderer = GetComponent<MeshRenderer>();
            _props    = new MaterialPropertyBlock();

            if (_bubble == null)
                _bubble = GetComponentInParent<ObserverBubble>();

            if (_bubble == null)
                Debug.LogError("[ObserverBubbleRenderer] No ObserverBubble found.");
        }

        private void OnEnable()
        {
            if (_bubble == null) return;
            _bubble.OnCoordinateChanged += OnCoordinateChanged;
            _bubble.OnCoherenceChanged  += OnCoherenceChanged;
        }

        private void OnDisable()
        {
            if (_bubble == null) return;
            _bubble.OnCoordinateChanged -= OnCoordinateChanged;
            _bubble.OnCoherenceChanged  -= OnCoherenceChanged;
        }

        private void Start()
        {
            // Seed from current coordinate so the shader has values before the
            // first event fires
            if (_bubble != null)
                PushToMaterial(_bubble.Coordinate);
        }

        // -----------------------------------------------------------------------
        // Event handlers
        // -----------------------------------------------------------------------

        private void OnCoordinateChanged(OmegaZaTaCoordinate coord)
        {
            PushToMaterial(coord);
        }

        private void OnCoherenceChanged(bool coherent)
        {
            _isCoherent = coherent;
            // Opacity will be re-derived on the next PushToMaterial call;
            // we don't need a separate push here because OnCoordinateChanged
            // fires on the same frame.
        }

        // -----------------------------------------------------------------------
        // Material update — zero GC via MaterialPropertyBlock
        // -----------------------------------------------------------------------

        private void PushToMaterial(OmegaZaTaCoordinate coord)
        {
            // Cache for Gizmo drawing
            _xiCurrent    = coord.Xi;
            _bubbleVolume = coord.BubbleVolume;

            // Derive SpinVector from BlochEvolver (accessed via bubble's cached component)
            BlochEvolver bloch = _bubble.GetComponent<BlochEvolver>();
            _spinVec      = bloch != null ? bloch.SpinVector : Vector3.up;

            // Derive CoherenceAxis û — bisector of outer + inner zenith directions
            Vector3 outer = SphericalToCartesian(coord.Za_outer, coord.Az_outer);
            Vector3 inner = SphericalToCartesian(coord.Za_inner, coord.Az_inner);
            _coherenceAxis = (outer + inner).normalized;
            if (_coherenceAxis.sqrMagnitude < 0.001f)
                _coherenceAxis = Vector3.up; // fallback when outer == inner exactly

            // Push all properties
            _renderer.GetPropertyBlock(_props);

            _props.SetFloat(ID_Xi,               _xiCurrent);
            _props.SetFloat(ID_XiCritical,        CosmologicalConstants.XI_CRITICAL);
            _props.SetVector(ID_SpinVector,       _spinVec);
            _props.SetVector(ID_CoherenceAxis,    _coherenceAxis);
            _props.SetFloat(ID_RadiusBase,        _baseRadius);
            _props.SetFloat(ID_DeformStrength,    _deformStrength);
            _props.SetFloat(ID_GlobalOpacity,     _globalOpacity);
            _props.SetFloat(ID_FluctuationScale,  _fluctuationScale);
            _props.SetFloat(ID_FluctuationSpeed,  _fluctuationSpeed);

            _renderer.SetPropertyBlock(_props);

            // Scale the bubble transform so its visual size matches BubbleVolume
            // r ∝ V^(1/3)  — keep the base radius anchored at Ω default
            float r = _baseRadius * Mathf.Pow(
                Mathf.Max(_bubbleVolume, 0.001f) /
                Mathf.Max(CosmologicalConstants.OMEGA_DEFAULT * CosmologicalConstants.OMEGA_DEFAULT, 1f),
                1f / 3f);
            transform.localScale = Vector3.one * r;
        }

        // -----------------------------------------------------------------------
        // Public API — horizon geometry for external queries
        // -----------------------------------------------------------------------

        /// <summary>
        /// Returns the bifurcation radius in a given world direction.
        /// Matches the vertex deformation in ObserverBubble.shader.
        /// Use for gizmo drawing, raycasting against the horizon surface,
        /// or the EpochBoundary crossfade trigger.
        /// </summary>
        public float BifurcationRadius(Vector3 worldDirection)
        {
            Vector3 n    = worldDirection.normalized;
            Vector3 S    = _spinVec;
            Vector3 uHat = _coherenceAxis;
            float   xi   = _xiCurrent;

            Vector3 crossSU = Vector3.Cross(S, uHat);
            if (crossSU.sqrMagnitude < 1e-6f) crossSU = Vector3.right;
            crossSU = crossSU.normalized;

            Vector3 sumSU = (S + uHat);
            if (sumSU.sqrMagnitude < 1e-6f) sumSU = Vector3.up;
            sumSU = sumSU.normalized;

            float ax0 = Vector3.Dot(S,       n);
            float ax1 = Vector3.Dot(uHat,    n);
            float ax2 = Vector3.Dot(crossSU, n);
            float ax3 = Vector3.Dot(sumSU,   n);

            float dilation = 1f - Mathf.Clamp01((S - uHat).magnitude * 0.5f);
            float ax4      = dilation * ax0;

            float xiProj = (ax0 * 0.30f + ax1 * 0.30f + ax2 * 0.15f + ax3 * 0.15f + ax4 * 0.10f) * xi;
            return _baseRadius * (1f + _deformStrength * xiProj);
        }

        // -----------------------------------------------------------------------
        // Gizmos — draw the bifurcation horizon in the editor
        // -----------------------------------------------------------------------

        private void OnDrawGizmos()
        {
            if (!_drawGizmos || _xiCurrent <= 0f) return;

            // Draw rings in XY, XZ, YZ planes to approximate the deformed manifold
            DrawHorizonRing(Vector3.forward,  Vector3.right,   Color.cyan);
            DrawHorizonRing(Vector3.up,       Vector3.right,   Color.yellow);
            DrawHorizonRing(Vector3.up,       Vector3.forward, Color.magenta);
        }

        private void DrawHorizonRing(Vector3 axis1, Vector3 axis2, Color colour)
        {
            int steps    = 64;
            Gizmos.color = colour;
            Vector3 prev = Vector3.zero;

            for (int i = 0; i <= steps; i++)
            {
                float   angle = (i / (float)steps) * 2f * Mathf.PI;
                Vector3 dir   = (Mathf.Cos(angle) * axis1 + Mathf.Sin(angle) * axis2).normalized;
                float   r     = BifurcationRadius(dir);
                Vector3 pt    = transform.position + dir * r;

                if (i > 0) Gizmos.DrawLine(prev, pt);
                prev = pt;
            }
        }

        // -----------------------------------------------------------------------
        // Utility
        // -----------------------------------------------------------------------

        private static Vector3 SphericalToCartesian(float theta, float phi)
        {
            return new Vector3(
                Mathf.Sin(theta) * Mathf.Cos(phi),
                Mathf.Cos(theta),
                Mathf.Sin(theta) * Mathf.Sin(phi)
            );
        }
    }
}

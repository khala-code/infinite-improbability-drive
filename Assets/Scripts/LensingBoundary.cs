using UnityEngine;

/// <summary>
/// LensingBoundary — renders the gravitational lensing kappa map as the
/// intermediate boundary layer, sitting between the Milky Way and the CMB.
///
/// Acts as the reconstruction lens in the holographic layer stack:
/// gravitational lensing = optical element that focuses the CMB object wave.
///
/// Coordinate frame: Galactic (l, b) — matches CMB and Milky Way cubemaps.
/// Texture: lensing_inner_boundary_masked.png imported as Cubemap,
///          Mapping → Latitude-Longitude Layout, Wrap Mode → Clamp.
/// </summary>
[RequireComponent(typeof(MeshRenderer))]
public class LensingBoundary : MonoBehaviour
{
    [Header("Textures")]
    [Tooltip("Gravitational lensing kappa map cubemap (masked, alpha = kappa)")]
    public Cubemap lensingMap;

    [Header("Appearance")]
    [Range(0f, 1f)]
    [Tooltip("Overall opacity — driven by HolographicLayerController at runtime")]
    public float opacity = 1.0f;

    [Tooltip("Brightness multiplier for the kappa map")]
    public float brightnessScale = 1.0f;

    [Tooltip("Tint colour — cool blue-white suits gravitational lensing")]
    public Color tint = new Color(0.7f, 0.85f, 1.0f, 1f);

    [Header("Rotation — Galactic Alignment")]
    [Tooltip("Should match MilkyWayBoundary alignment offset")]
    public Vector3 galacticAlignmentOffset = new Vector3(0f, -90f, 0f);

    [Header("Double-Zenith Blend")]
    [Range(0f, 1f)]
    [Tooltip("Softens the seam near Galactic poles")]
    public float poleBlendWidth = 0.05f;

    // ── Private ──────────────────────────────────────────────────────────────
    private Material _mat;
    private static readonly int PropLensingMap      = Shader.PropertyToID("_LensingMap");
    private static readonly int PropOpacity         = Shader.PropertyToID("_Opacity");
    private static readonly int PropBrightness      = Shader.PropertyToID("_BrightnessScale");
    private static readonly int PropTint            = Shader.PropertyToID("_Tint");
    private static readonly int PropPoleBlend       = Shader.PropertyToID("_PoleBlendWidth");
    private static readonly int PropAlignmentOffset = Shader.PropertyToID("_GalacticAlignmentOffset");

    // ── Unity Lifecycle ───────────────────────────────────────────────────────
    private void Awake()
    {
        _mat = GetComponent<MeshRenderer>().material;
        ApplyProperties();
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (_mat != null)
            ApplyProperties();
    }
#endif

    // ── Property Push ─────────────────────────────────────────────────────────
    public void ApplyProperties()
    {
        if (lensingMap != null)
            _mat.SetTexture(PropLensingMap, lensingMap);

        _mat.SetFloat(PropOpacity,          opacity);
        _mat.SetFloat(PropBrightness,       brightnessScale);
        _mat.SetColor(PropTint,             tint);
        _mat.SetFloat(PropPoleBlend,        poleBlendWidth);
        _mat.SetVector(PropAlignmentOffset, galacticAlignmentOffset);
    }

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Fade the lensing layer in or out over <paramref name="duration"/> seconds.
    /// </summary>
    public void FadeTo(float targetOpacity, float duration)
    {
        StopAllCoroutines();
        StartCoroutine(FadeCoroutine(targetOpacity, duration));
    }

    private System.Collections.IEnumerator FadeCoroutine(float target, float duration)
    {
        float start   = opacity;
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            opacity  = Mathf.Lerp(start, target, elapsed / duration);
            _mat.SetFloat(PropOpacity, opacity);
            yield return null;
        }

        opacity = target;
        _mat.SetFloat(PropOpacity, opacity);
    }
}

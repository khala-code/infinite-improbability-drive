using UnityEngine;

/// <summary>
/// MilkyWayBoundary — renders the Gaia DR3 stellar density map as the innermost
/// boundary layer of the observer bubble, sitting between the lensing inner boundary
/// and the observer. Samples a cubemap texture in Galactic coordinates and blends
/// with the lensing boundary via alpha compositing.
///
/// Coordinate frame: Galactic (l, b) — matches CMB and lensing cubemaps.
/// Texture: milkyway_stellar_density_masked.png imported as Cubemap,
///          Mapping → Latitude-Longitude Layout, Wrap Mode → Clamp.
/// </summary>
[RequireComponent(typeof(MeshRenderer))]
public class MilkyWayBoundary : MonoBehaviour
{
    [Header("Textures")]
    [Tooltip("Gaia DR3 stellar density cubemap (masked, alpha = density)")]
    public Cubemap stellarDensityMap;

    [Tooltip("Lensing inner boundary cubemap for blending reference")]
    public Cubemap lensingBoundaryMap;

    [Header("Appearance")]
    [Range(0f, 1f)]
    [Tooltip("Overall opacity of the Milky Way layer")]
    public float opacity = 1.0f;

    [Tooltip("Brightness multiplier for the stellar density")]
    public float brightnessScale = 1.5f;

    [Tooltip("Tint colour applied to the stellar density map")]
    public Color tint = new Color(0.9f, 0.85f, 0.7f, 1f); // warm starfield tone

    [Header("Rotation — Galactic Alignment")]
    [Tooltip("Offset rotation to align Galactic centre (l=0, b=0) with scene forward")]
    public Vector3 galacticAlignmentOffset = new Vector3(0f, -90f, 0f);

    [Header("Double-Zenith Blend")]
    [Range(0f, 1f)]
    [Tooltip("Blend weight where the Milky Way meets the lensing boundary at the double-zenith poles")]
    public float poleBlendWidth = 0.05f;

    // ── Private ──────────────────────────────────────────────────────────────
    private Material _mat;
    private static readonly int PropStellarMap     = Shader.PropertyToID("_StellarDensityMap");
    private static readonly int PropLensingMap      = Shader.PropertyToID("_LensingBoundaryMap");
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
    private void ApplyProperties()
    {
        if (stellarDensityMap != null)
            _mat.SetTexture(PropStellarMap, stellarDensityMap);

        if (lensingBoundaryMap != null)
            _mat.SetTexture(PropLensingMap, lensingBoundaryMap);

        _mat.SetFloat(PropOpacity,         opacity);
        _mat.SetFloat(PropBrightness,      brightnessScale);
        _mat.SetColor(PropTint,            tint);
        _mat.SetFloat(PropPoleBlend,       poleBlendWidth);
        _mat.SetVector(PropAlignmentOffset, galacticAlignmentOffset);
    }

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Fade the Milky Way layer in or out over <paramref name="duration"/> seconds.
    /// </summary>
    public void FadeTo(float targetOpacity, float duration)
    {
        StopAllCoroutines();
        StartCoroutine(FadeCoroutine(targetOpacity, duration));
    }

    private System.Collections.IEnumerator FadeCoroutine(float target, float duration)
    {
        float start    = opacity;
        float elapsed  = 0f;

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

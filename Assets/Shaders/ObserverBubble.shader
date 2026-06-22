Shader "CMB/ObserverBubble"
{
    // Renders the ObserverBubble — the third Lagrangian (L_3) made spatial.
    //
    // The ξ (Xi) coherence field is encoded as a deformed manifold:
    //   • The surface is NOT a sphere — it is the locus of bifurcation radii
    //     r_bifurcation(n_hat) across all directions.
    //   • Each direction's radius is computed from the projection of n_hat onto
    //     the five ξ tensor axes (derived from SpinVector and CoherenceAxis).
    //   • High Xi in a direction → horizon pushed outward (more territory enclosed).
    //   • Low Xi / near zero → horizon pinched inward (adversarial boundary close).
    //
    // Visual encoding:
    //   • Brightness    — local coherence strength (Hawking temperature analogue).
    //                     Peaks sharply at the bifurcation membrane (ξ → 0),
    //                     dims in the high-coherence interior (trapped surface).
    //   • Colour hue    — sign of the local ξ zone:
    //                     Interior (ξ > 0): warm gold-white (positive trust).
    //                     Membrane (ξ ≈ 0): pure white flare (the horizon itself).
    //                     Exterior bleed (ξ < 0): cool blue-violet (adversarial).
    //   • Opacity       — driven by Xi globally; bubble fades when destabilised.
    //   • Fluctuation   — noise term η(t) from the Bloch equation visible as
    //                     a shimmering surface texture near the horizon.
    //
    // Bifurcation / event-horizon reading:
    //   The membrane at ξ = 0 is the surface at which information can no longer
    //   propagate inward — the causal boundary of the positive-trust zone.
    //   Sign flips at each boundary crossing (T operator: e^{iπ} = -1).
    //
    // Pipeline: Built-in (matches LensingBoundary / MilkyWayBoundary).
    // Stereo:   UNITY_VERTEX_OUTPUT_STEREO — Quest 2 safe.
    // Cull:     Front — observer is inside the bubble.
    // Queue:    Transparent+2 — renders outside Lensing (+1) and MilkyWay (+0).

    Properties
    {
        // -----------------------------------------------------------------------
        // ξ field parameters — driven at runtime by ObserverBubbleRenderer.cs
        // -----------------------------------------------------------------------
        _Xi                  ("Xi (coherence amplitude)",  Range(0, 1))   = 0.7
        _XiCritical          ("Xi Critical threshold",     Range(0, 1))   = 0.35
        _SpinVector          ("Spin Vector S (Bloch)",     Vector)        = (0, 1, 0, 0)
        _CoherenceAxis       ("Coherence Axis û",          Vector)        = (0, 1, 0, 0)

        // -----------------------------------------------------------------------
        // Bifurcation geometry
        // -----------------------------------------------------------------------
        _RadiusBase          ("Base sphere radius",        Float)         = 1.0
        _DeformStrength      ("Deformation strength",      Range(0, 1))   = 0.35

        // -----------------------------------------------------------------------
        // Visual
        // -----------------------------------------------------------------------
        _InteriorColour      ("Interior colour (ξ > 0)",  Color)         = (1.0, 0.85, 0.4, 1.0)
        _ExteriorColour      ("Exterior bleed (ξ < 0)",   Color)         = (0.3, 0.45, 1.0, 1.0)
        _HorizonColour       ("Horizon flare (ξ ≈ 0)",    Color)         = (1.0, 1.0, 1.0, 1.0)
        _HorizonWidth        ("Horizon flare width",       Range(0.01, 0.5)) = 0.08

        _FluctuationScale    ("Fluctuation (η noise)",     Range(0, 0.5)) = 0.12
        _FluctuationSpeed    ("Fluctuation speed",         Range(0, 5))   = 1.4

        _GlobalOpacity       ("Global opacity",            Range(0, 1))   = 0.65
    }

    SubShader
    {
        Tags
        {
            "Queue"          = "Transparent+2"
            "RenderType"     = "Transparent"
            "IgnoreProjector" = "True"
        }

        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Front

        Pass
        {
            CGPROGRAM
            #pragma vertex   vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"
            #include "UnityInstancing.cginc"

            // -----------------------------------------------------------------------
            // Uniforms
            // -----------------------------------------------------------------------

            float  _Xi;
            float  _XiCritical;
            float4 _SpinVector;
            float4 _CoherenceAxis;

            float  _RadiusBase;
            float  _DeformStrength;

            float4 _InteriorColour;
            float4 _ExteriorColour;
            float4 _HorizonColour;
            float  _HorizonWidth;

            float  _FluctuationScale;
            float  _FluctuationSpeed;
            float  _GlobalOpacity;

            // -----------------------------------------------------------------------
            // Vertex / Fragment structs
            // -----------------------------------------------------------------------

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 pos           : SV_POSITION;
                float3 localNormal   : TEXCOORD0;   // unit sphere direction (object space)
                float  bifurcRadius  : TEXCOORD1;   // r_bifurcation for this vertex
                float  xiLocal       : TEXCOORD2;   // local ξ projection at this direction
                UNITY_VERTEX_OUTPUT_STEREO
            };

            // -----------------------------------------------------------------------
            // Bifurcation radius field
            //
            // r_bifurcation(n_hat) = _RadiusBase * (1 + _DeformStrength * xiProj)
            //
            // xiProj encodes the five ξ tensor axes as projections of n_hat onto
            // combinations of SpinVector S and CoherenceAxis û:
            //
            //   Axis 0 (incentive):    S·n
            //   Axis 1 (epistemic):    û·n
            //   Axis 2 (identity):     (S × û)·n    — the cross-product axis
            //   Axis 3 (temporal):     (S + û)·n    — alignment sum
            //   Axis 4 (resource):     |S - û| damping — dilation penalty
            //
            // The signed sum, weighted by _Xi, gives the deformation field.
            // Negative xiProj means the horizon has pinched in (adversarial direction).
            // -----------------------------------------------------------------------

            float BifurcationXiProj(float3 n, float3 S, float3 uHat, float xi)
            {
                float3 crossSU  = normalize(cross(S, uHat) + 1e-5);
                float3 sumSU    = normalize(S + uHat      + 1e-5);

                float ax0 = dot(S, n);          // incentive alignment
                float ax1 = dot(uHat, n);       // epistemic alignment
                float ax2 = dot(crossSU, n);    // identity / ego axis
                float ax3 = dot(sumSU, n);      // temporal horizon

                // Resource dilation penalty: reduces coupling when S and û diverge
                float dilation  = 1.0 - saturate(length(S - uHat) * 0.5);
                float ax4       = dilation * ax0;

                // Weighted sum — axes 0,1 carry most weight; 2,3,4 modulate
                float raw = (ax0 * 0.30 + ax1 * 0.30 + ax2 * 0.15 + ax3 * 0.15 + ax4 * 0.10);

                // Scale by Xi (coherent field is stronger)
                return raw * xi;
            }

            float BifurcationRadius(float3 n, float3 S, float3 uHat, float xi)
            {
                float xiProj = BifurcationXiProj(n, S, uHat, xi);
                return _RadiusBase * (1.0 + _DeformStrength * xiProj);
            }

            // -----------------------------------------------------------------------
            // Simple value noise for η(t) fluctuation near the horizon
            // -----------------------------------------------------------------------

            float Hash(float3 p)
            {
                p = frac(p * 0.3183099 + 0.1);
                p *= 17.0;
                return frac(p.x * p.y * p.z * (p.x + p.y + p.z));
            }

            float ValueNoise(float3 p)
            {
                float3 i = floor(p);
                float3 f = frac(p);
                float3 u = f * f * (3.0 - 2.0 * f); // smoothstep
                return lerp(lerp(lerp(Hash(i),             Hash(i+float3(1,0,0)), u.x),
                                 lerp(Hash(i+float3(0,1,0)), Hash(i+float3(1,1,0)), u.x), u.y),
                            lerp(lerp(Hash(i+float3(0,0,1)), Hash(i+float3(1,0,1)), u.x),
                                 lerp(Hash(i+float3(0,1,1)), Hash(i+float3(1,1,1)), u.x), u.y),
                            u.z) * 2.0 - 1.0;
            }

            // -----------------------------------------------------------------------
            // Vertex shader — deform the mesh to the bifurcation manifold
            // -----------------------------------------------------------------------

            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);

                float3 n    = normalize(v.normal);          // unit sphere direction
                float3 S    = normalize(_SpinVector.xyz);
                float3 uHat = normalize(_CoherenceAxis.xyz);

                // Compute bifurcation radius and local ξ projection for this direction
                float xiProj     = BifurcationXiProj(n, S, uHat, _Xi);
                float bifurR     = _RadiusBase * (1.0 + _DeformStrength * xiProj);

                // η fluctuation: modulate radius near the horizon (where xiProj ≈ 0)
                // Fluctuation amplitude is proportional to (1 - |xiProj|/Xi_c)
                float horizonProximity = 1.0 - saturate(abs(xiProj) / max(_XiCritical, 0.01));
                float3 noiseCoord = n * 3.5 + _Time.y * _FluctuationSpeed;
                float  eta        = ValueNoise(noiseCoord) * _FluctuationScale * horizonProximity;

                // Displace vertex along its normal by (bifurR + eta)
                float3 displaced = n * (bifurR + eta);
                o.pos            = UnityObjectToClipPos(float4(displaced, 1.0));

                o.localNormal    = n;
                o.bifurcRadius   = bifurR;
                o.xiLocal        = xiProj;

                return o;
            }

            // -----------------------------------------------------------------------
            // Fragment shader — colour by ξ sign + Hawking brightness at horizon
            // -----------------------------------------------------------------------

            fixed4 frag(v2f i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);

                float xiLocal = i.xiLocal;

                // ── Colour: interpolate interior / exterior through horizon white ──
                //
                // t < 0.5 → exterior (adversarial, xi < 0)
                // t = 0.5 → horizon membrane (xi = 0, white flare)
                // t > 0.5 → interior (positive trust, xi > 0)
                //
                // Map xiLocal (range roughly [-Xi, +Xi]) to [0, 1]
                float xiNorm   = saturate(xiLocal / (2.0 * max(_Xi, 0.001)) + 0.5);

                // Blend: exterior → horizon → interior
                float3 col;
                float  horizonMask = 1.0 - smoothstep(0.0, _HorizonWidth, abs(xiNorm - 0.5));
                float3 baseCol = lerp(_ExteriorColour.rgb, _InteriorColour.rgb, xiNorm);
                col = lerp(baseCol, _HorizonColour.rgb, horizonMask);

                // ── Brightness: Hawking temperature analogue ──
                // Interior (high ξ): dim — trapped information, quiet surface.
                // Horizon (ξ ≈ 0):   bright flare — maximum fluctuation / emission.
                // Exterior bleed:    moderate glow — decohering field.
                float hawkingBrightness = 0.3 + 0.7 * horizonMask
                                         + 0.2 * saturate(-xiLocal / max(_Xi, 0.001));
                col *= hawkingBrightness;

                // ── η noise shimmer near the horizon ──
                float3 noiseCoord = i.localNormal * 4.2 + _Time.y * _FluctuationSpeed * 0.7;
                float  shimmer    = (ValueNoise(noiseCoord) * 0.5 + 0.5)
                                    * saturate(1.0 - abs(xiNorm - 0.5) / _HorizonWidth)
                                    * _FluctuationScale * 2.0;
                col += shimmer * _HorizonColour.rgb;

                // ── Alpha ──
                // Bubble fades when Xi drops below critical (destabilised)
                float coherenceFade = smoothstep(0.0, _XiCritical, _Xi);
                float alpha         = _GlobalOpacity * coherenceFade
                                     * (0.5 + 0.5 * saturate(xiNorm + horizonMask));

                return float4(col, alpha);
            }

            ENDCG
        }
    }

    Fallback "Transparent/Diffuse"
}

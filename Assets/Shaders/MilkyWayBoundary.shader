Shader "CMB/MilkyWayBoundary"
{
    // Samples the Gaia DR3 stellar density cubemap and blends with the lensing
    // inner boundary. Rendered on an inverted sphere (observer inside), so culling
    // is set to Front. Blends additively over whatever is behind it.
    //
    // Coordinate frame: Galactic — matches CMB and lensing cubemaps.
    // Pipeline: Built-in (not URP/HDRP).

    Properties
    {
        _StellarDensityMap  ("Stellar Density Map (Masked Cubemap)", Cube) = "black" {}
        _LensingBoundaryMap ("Lensing Boundary Map (Cubemap)",       Cube) = "black" {}

        _Opacity            ("Opacity",          Range(0,1))  = 1.0
        _BrightnessScale    ("Brightness Scale", Float)       = 1.5
        _Tint               ("Tint",             Color)       = (0.9, 0.85, 0.7, 1.0)
        _PoleBlendWidth     ("Pole Blend Width", Range(0,1))  = 0.05

        // Euler angles (degrees) applied to the sample direction to align
        // Galactic centre with scene forward.
        _GalacticAlignmentOffset ("Galactic Alignment Offset", Vector) = (0, -90, 0, 0)
    }

    SubShader
    {
        // Render after opaque geometry, before transparent.
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" }

        // Additive blend — stars glow over the scene without darkening it.
        Blend One OneMinusSrcAlpha
        ZWrite Off
        Cull Front          // Observer is INSIDE the sphere.

        Pass
        {
            CGPROGRAM
            #pragma vertex   vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            // ── Uniforms ───────────────────────────────────────────
            samplerCUBE _StellarDensityMap;
            samplerCUBE _LensingBoundaryMap;

            float  _Opacity;
            float  _BrightnessScale;
            float4 _Tint;
            float  _PoleBlendWidth;
            float4 _GalacticAlignmentOffset;

            // ── Structs ─────────────────────────────────────────────
            struct appdata
            {
                float4 vertex   : POSITION;
                float3 normal   : NORMAL;
            };

            struct v2f
            {
                float4 pos      : SV_POSITION;
                float3 worldDir : TEXCOORD0;    // direction from origin in world space
            };

            // ── Helpers ─────────────────────────────────────────────

            // Rotate a direction vector by Euler angles (degrees) in YXZ order.
            float3 RotateByEuler(float3 dir, float3 eulerDeg)
            {
                float3 rad = radians(eulerDeg);

                // Yaw (Y)
                float cy = cos(rad.y), sy = sin(rad.y);
                dir = float3(cy * dir.x + sy * dir.z,
                             dir.y,
                            -sy * dir.x + cy * dir.z);

                // Pitch (X)
                float cx = cos(rad.x), sx = sin(rad.x);
                dir = float3(dir.x,
                             cx * dir.y - sx * dir.z,
                             sx * dir.y + cx * dir.z);

                // Roll (Z)
                float cz = cos(rad.z), sz = sin(rad.z);
                dir = float3(cz * dir.x - sz * dir.y,
                             sz * dir.x + cz * dir.y,
                             dir.z);
                return dir;
            }

            // ── Vertex ───────────────────────────────────────────────
            v2f vert(appdata v)
            {
                v2f o;
                o.pos      = UnityObjectToClipPos(v.vertex);
                // World-space direction from sphere centre to vertex.
                o.worldDir = normalize(mul((float3x3)unity_ObjectToWorld, v.normal));
                return o;
            }

            // ── Fragment ─────────────────────────────────────────────
            fixed4 frag(v2f i) : SV_Target
            {
                // Apply Galactic alignment rotation to sample direction.
                float3 sampleDir = RotateByEuler(
                    normalize(i.worldDir),
                    _GalacticAlignmentOffset.xyz
                );

                // Sample stellar density (RGBA: density in all channels, alpha = density).
                float4 stellar = texCUBE(_StellarDensityMap, sampleDir);

                // Brightness + tint.
                float3 colour = stellar.rgb * _BrightnessScale * _Tint.rgb;

                // Pole blend — fade out near Galactic poles (|b| → 90°)
                // to soften the double-zenith seam.
                float absSinB  = abs(sampleDir.y);              // 0 at equator, 1 at poles
                float poleFade = 1.0 - smoothstep(
                    1.0 - _PoleBlendWidth,
                    1.0,
                    absSinB
                );

                float alpha = stellar.a * _Opacity * poleFade;

                return float4(colour * alpha, alpha);           // pre-multiplied alpha
            }
            ENDCG
        }
    }

    Fallback "Transparent/Diffuse"
}

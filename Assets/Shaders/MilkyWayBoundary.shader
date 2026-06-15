Shader "CMB/MilkyWayBoundary"
{
    // Samples the Gaia DR3 stellar density cubemap and composites OVER the CMB.
    // Physically correct — the Milky Way sits between the observer and the CMB,
    // partially occluding it where stellar density is high (galactic plane/core).
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
        // Render after skybox (Queue 1000), before other transparent objects.
        // This ensures we composite over the CMB skybox cleanly.
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" }

        // Standard alpha composite — galaxy occludes CMB where stars are dense.
        Blend SrcAlpha OneMinusSrcAlpha
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
                o.worldDir = normalize(mul((float3x3)unity_ObjectToWorld, v.normal));
                return o;
            }

            // ── Fragment ─────────────────────────────────────────────
            fixed4 frag(v2f i) : SV_Target
            {
                float3 sampleDir = RotateByEuler(
                    normalize(i.worldDir),
                    _GalacticAlignmentOffset.xyz
                );

                // Sample stellar density (alpha = density, 0 where no stars).
                float4 stellar = texCUBE(_StellarDensityMap, sampleDir);

                // Brightness + tint.
                float3 colour = stellar.rgb * _BrightnessScale * _Tint.rgb;

                // Pole blend — soften seam near Galactic poles.
                float absSinB  = abs(sampleDir.y);
                float poleFade = 1.0 - smoothstep(
                    1.0 - _PoleBlendWidth,
                    1.0,
                    absSinB
                );

                // Alpha composite — galaxy occludes CMB proportionally to density.
                float alpha = stellar.a * _Opacity * poleFade;

                return float4(colour, alpha);   // standard (non-premultiplied) alpha
            }
            ENDCG
        }
    }

    Fallback "Transparent/Diffuse"
}

Shader "CMB/MilkyWayBoundary"
{
    // Samples the Gaia DR3 stellar density cubemap and composites OVER the CMB.
    // Physically correct — the Milky Way sits between the observer and the CMB,
    // partially occluding it where stellar density is high.
    //
    // Coordinate frame: Galactic — matches CMB and lensing cubemaps.
    // Pipeline: Built-in (not URP/HDRP).

    Properties
    {
        _StellarDensityMap       ("Stellar Density Map (Masked Cubemap)", Cube) = "black" {}

        _Opacity                 ("Opacity",          Range(0,1)) = 1.0
        _BrightnessScale         ("Brightness Scale", Float)      = 1.5
        _Tint                    ("Tint",             Color)      = (0.9, 0.85, 0.7, 1.0)
        _PoleBlendWidth          ("Pole Blend Width", Range(0,1)) = 0.05
        _GalacticAlignmentOffset ("Galactic Alignment Offset", Vector) = (0, -90, 0, 0)
    }

    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" }

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

            samplerCUBE _StellarDensityMap;
            float  _Opacity;
            float  _BrightnessScale;
            float4 _Tint;
            float  _PoleBlendWidth;
            float4 _GalacticAlignmentOffset;

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 pos      : SV_POSITION;
                float3 worldDir : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            float3 RotateByEuler(float3 dir, float3 eulerDeg)
            {
                float3 rad = radians(eulerDeg);
                float cy = cos(rad.y), sy = sin(rad.y);
                dir = float3(cy*dir.x + sy*dir.z, dir.y, -sy*dir.x + cy*dir.z);
                float cx = cos(rad.x), sx = sin(rad.x);
                dir = float3(dir.x, cx*dir.y - sx*dir.z, sx*dir.y + cx*dir.z);
                float cz = cos(rad.z), sz = sin(rad.z);
                dir = float3(cz*dir.x - sz*dir.y, sz*dir.x + cz*dir.y, dir.z);
                return dir;
            }

            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.pos      = UnityObjectToClipPos(v.vertex);
                o.worldDir = normalize(mul((float3x3)unity_ObjectToWorld, v.normal));
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float3 sampleDir = RotateByEuler(normalize(i.worldDir), _GalacticAlignmentOffset.xyz);
                float4 stellar   = texCUBE(_StellarDensityMap, sampleDir);
                float3 colour    = stellar.rgb * _BrightnessScale * _Tint.rgb;
                float  absSinB   = abs(sampleDir.y);
                float  poleFade  = 1.0 - smoothstep(1.0 - _PoleBlendWidth, 1.0, absSinB);
                float  alpha     = stellar.a * _Opacity * poleFade;
                return float4(colour, alpha);
            }
            ENDCG
        }
    }
    Fallback "Transparent/Diffuse"
}

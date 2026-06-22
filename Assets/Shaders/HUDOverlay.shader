Shader "CMB/HUDOverlay"
{
    // Minimal unlit transparent shader for World Space VR HUD elements.
    //
    // Guaranteed to render above ALL world-space geometry, including the
    // lensing boundary sphere (Queue=Transparent+1) and any other transparent
    // layers in the holographic stack.
    //
    // Key properties:
    //   Queue   = Overlay (4000) — drawn last, after all Transparent geometry
    //   ZTest   = Always         — ignores depth buffer, draws unconditionally
    //   ZWrite  = Off            — does not write depth (doesn't occlude anything)
    //   Cull    = Off            — visible from both sides
    //
    // Stereo: full Quest 2 stereo support via UNITY_VERTEX_OUTPUT_STEREO.
    // Pipeline: Built-in (matches rest of project).
    //
    // Usage: assign this material to the Canvas Renderer material on the
    // EpochHUD and EpochScrubber World Space canvases.

    Properties
    {
        _MainTex ("Texture",       2D)    = "white" {}
        _Color   ("Tint",         Color)  = (1, 1, 1, 1)
        _Opacity ("Opacity", Range(0,1))  = 1.0
    }

    SubShader
    {
        Tags
        {
            "Queue"           = "Overlay"
            "RenderType"      = "Transparent"
            "IgnoreProjector" = "True"
        }

        Blend SrcAlpha OneMinusSrcAlpha
        ZTest  Always
        ZWrite Off
        Cull   Off

        Pass
        {
            CGPROGRAM
            #pragma vertex   vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"
            #include "UnityInstancing.cginc"

            sampler2D _MainTex;
            float4    _MainTex_ST;
            fixed4    _Color;
            float     _Opacity;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv     : TEXCOORD0;
                fixed4 color  : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv  : TEXCOORD0;
                fixed4 color : COLOR;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.pos   = UnityObjectToClipPos(v.vertex);
                o.uv    = TRANSFORM_TEX(v.uv, _MainTex);
                o.color = v.color * _Color;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                fixed4 tex = tex2D(_MainTex, i.uv);
                fixed4 col = tex * i.color;
                col.a *= _Opacity;
                return col;
            }
            ENDCG
        }
    }

    // No fallback — if this shader fails to compile the HUD simply won't render,
    // which is preferable to falling back to an opaque or depth-writing shader.
    Fallback Off
}

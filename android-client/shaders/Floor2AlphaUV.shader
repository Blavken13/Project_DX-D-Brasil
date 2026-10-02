// Unity 6 equivalent of the original floor shader: color uses UV0 and opacity
// uses the red channel of AlphaTex sampled with UV1. Keep original render state.
Shader "Durango/Building/Floor2AlphaUV"
{
    Properties
    {
        _Color ("Main Color", Color) = (1,1,1,1)
        _MainTex ("Base (RGB)", 2D) = "white" {}
        _AlphaTex ("Alpha (Red)", 2D) = "white" {}
    }
    SubShader
    {
        Tags { "Queue"="Background+2" "RenderType"="Transparent" }
        Pass
        {
            ZWrite Off
            Blend SrcAlpha OneMinusSrcAlpha, One One
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"
            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; float2 alphaUV : TEXCOORD1; };
            struct v2f { float4 position : SV_POSITION; float2 uv : TEXCOORD0; float2 alphaUV : TEXCOORD1; };
            sampler2D _MainTex, _AlphaTex;
            float4 _MainTex_ST, _AlphaTex_ST;
            fixed4 _Color;
            v2f vert(appdata v)
            {
                v2f o;
                o.position = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.alphaUV = TRANSFORM_TEX(v.alphaUV, _AlphaTex);
                return o;
            }
            fixed4 frag(v2f i) : SV_Target
            {
                return fixed4(tex2D(_MainTex, i.uv).rgb, tex2D(_AlphaTex, i.alphaUV).r) * _Color;
            }
            ENDCG
        }
    }
    Fallback Off
}

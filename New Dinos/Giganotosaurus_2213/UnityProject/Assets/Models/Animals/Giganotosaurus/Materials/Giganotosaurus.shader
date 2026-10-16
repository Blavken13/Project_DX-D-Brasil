// Authoring preview. Packaging references the real LitSphere shader in preload.
Shader "Durango/Animals/GiganotosaurusAuthoring"
{
    Properties
    {
        _MainTex ("Original skin", 2D) = "white" {}
        _LitSphereTex ("litsphere_dino_giant", 2D) = "white" {}
        _Color ("Color", Color) = (1,1,1,1)
        _LitAmount ("Matcap intensity", Float) = 2
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        Cull Back
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            sampler2D _MainTex, _LitSphereTex;
            float4 _MainTex_ST, _Color;
            float _LitAmount;
            struct Vertex { float4 vertex : POSITION; float3 normal : NORMAL; float2 uv : TEXCOORD0; };
            struct Interpolator { float4 position : SV_POSITION; float2 uv : TEXCOORD0; float3 viewNormal : TEXCOORD1; };
            Interpolator vert(Vertex input)
            {
                Interpolator output;
                output.position = UnityObjectToClipPos(input.vertex);
                output.uv = TRANSFORM_TEX(input.uv, _MainTex);
                output.viewNormal = mul((float3x3)UNITY_MATRIX_V, UnityObjectToWorldNormal(input.normal));
                return output;
            }
            fixed4 frag(Interpolator input) : SV_Target
            {
                float2 sphereUV = normalize(input.viewNormal).xy * .5 + .5;
                fixed3 albedo = tex2D(_MainTex, input.uv).rgb;
                fixed3 lighting = tex2D(_LitSphereTex, sphereUV).rgb * _LitAmount;
                return fixed4(albedo * lighting * _Color.rgb, _Color.a);
            }
            ENDCG
        }
    }
}

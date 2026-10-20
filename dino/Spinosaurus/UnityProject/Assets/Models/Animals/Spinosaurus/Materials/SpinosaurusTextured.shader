Shader "Spinosaurus/TexturedAnimal"
{
    Properties
    {
        _SpinoAlbedo ("Baked Unreal albedo", 2D) = "white" {}
        _SpinoNormal ("Normal map", 2D) = "bump" {}
        _SpinoTint ("Preview tint", Color) = (1,1,1,1)
        _SpinoNormalStrength ("Normal strength", Range(0,1)) = 0.65
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
        LOD 200
        Pass
        {
            Tags { "LightMode"="ForwardBase" }
            ZWrite On
            Cull Back
            CGPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #include "UnityCG.cginc"
            sampler2D _SpinoAlbedo;
            sampler2D _SpinoNormal;
            float4 _SpinoAlbedo_ST;
            float4 _SpinoTint;
            float _SpinoNormalStrength;
            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                float4 tangent : TANGENT;
                float2 uv : TEXCOORD0;
            };
            struct v2f
            {
                float4 position : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 normal : TEXCOORD1;
                float3 tangent : TEXCOORD2;
                float3 bitangent : TEXCOORD3;
                UNITY_FOG_COORDS(4)
            };
            v2f vert(appdata input)
            {
                v2f output;
                output.position = UnityObjectToClipPos(input.vertex);
                output.uv = TRANSFORM_TEX(input.uv, _SpinoAlbedo);
                output.normal = UnityObjectToWorldNormal(input.normal);
                output.tangent = UnityObjectToWorldDir(input.tangent.xyz);
                output.bitangent = cross(output.normal, output.tangent) * input.tangent.w * unity_WorldTransformParams.w;
                UNITY_TRANSFER_FOG(output, output.position);
                return output;
            }
            fixed4 frag(v2f input) : SV_Target
            {
                float3 normalTS = UnpackNormal(tex2D(_SpinoNormal, input.uv));
                normalTS.xy *= _SpinoNormalStrength;
                normalTS = normalize(normalTS);
                float3 normal = normalize(input.tangent * normalTS.x + input.bitangent * normalTS.y + input.normal * normalTS.z);
                float3 albedo = tex2D(_SpinoAlbedo, input.uv).rgb * _SpinoTint.rgb;
                // The client lights animals with custom matcaps instead of Unity
                // lights. A stable hemisphere keeps this baked albedo readable
                // under that camera without the native recolor-mask contract.
                float light = .55 + .45 * saturate(dot(normal, normalize(float3(-.4,.8,-.45))));
                fixed4 color = fixed4(albedo * light, 1);
                UNITY_APPLY_FOG(input.fogCoord, color);
                return color;
            }
            ENDCG
        }
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            ZWrite On
            CGPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_shadowcaster
            #include "UnityCG.cginc"
            struct v2f { V2F_SHADOW_CASTER; };
            v2f vert(appdata_base v)
            {
                v2f o;
                TRANSFER_SHADOW_CASTER_NORMALOFFSET(o)
                return o;
            }
            float4 frag(v2f i) : SV_Target { SHADOW_CASTER_FRAGMENT(i) }
            ENDCG
        }
    }
    Fallback Off
}

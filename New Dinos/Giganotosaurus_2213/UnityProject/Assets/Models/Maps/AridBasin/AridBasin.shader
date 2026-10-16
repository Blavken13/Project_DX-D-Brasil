Shader "LostHorizon/AridBasin"
{
    Properties { _SandTex ("Desert sand", 2D) = "white" {} _RockTex ("Desert rock", 2D) = "white" {} }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
        Pass
        {
            Cull Back
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #pragma multi_compile_fog
            #include "UnityCG.cginc"
            sampler2D _SandTex, _RockTex;
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; float3 normal : TEXCOORD1; UNITY_FOG_COORDS(2) };
            v2f vert(appdata_base v) { v2f o; o.pos=UnityObjectToClipPos(v.vertex); o.uv=v.texcoord.xy; o.normal=UnityObjectToWorldNormal(v.normal); UNITY_TRANSFER_FOG(o,o.pos); return o; }
            fixed4 frag(v2f i) : SV_Target
            {
                float3 n=normalize(i.normal);
                float slope=saturate((1-abs(n.y))*2.5);
                fixed3 sand=tex2D(_SandTex,i.uv).rgb;
                fixed3 rock=tex2D(_RockTex,i.uv).rgb;
                float lighting=.68+.32*saturate(dot(n,normalize(float3(.3,.8,-.4))));
                fixed4 c=fixed4(lerp(sand,rock,slope)*lighting,1);
                UNITY_APPLY_FOG(i.fogCoord,c);
                return c;
            }
            ENDCG
        }
    }
}

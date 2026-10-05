// Water, stained glass, ice: blended after opaque geometry.
Shader "OuterCraft/BlockTranslucent"
{
    Properties
    {
        _MainTex ("Atlas", 2D) = "white" {}
        _BlockLightColor ("Block light colour", Color) = (1, 0.85, 0.65, 1)
        _BlockLightStrength ("Block light strength", Float) = 1.2
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" }
        ZWrite Off
        Cull Off
        CGPROGRAM
        #pragma surface surf Standard alpha:fade vertex:vert
        #pragma target 3.0
        sampler2D _MainTex;
        fixed4 _BlockLightColor;
        float _BlockLightStrength;

        struct Input
        {
            float2 uv_MainTex;
            float4 color : COLOR;
            float3 light;
        };

        void vert(inout appdata_full v, out Input o)
        {
            UNITY_INITIALIZE_OUTPUT(Input, o);
            o.light = v.texcoord1.xyz;
        }

        float Curve(float l) { return l / (4.0 - 3.0 * l); }

        void surf(Input i, inout SurfaceOutputStandard o)
        {
            fixed4 c = tex2D(_MainTex, i.uv_MainTex) * i.color;
            float sky = Curve(saturate(i.light.y));
            o.Albedo = c.rgb * lerp(0.45, 1.0, sky);
            o.Emission = c.rgb * Curve(saturate(i.light.x)) * _BlockLightColor.rgb * _BlockLightStrength;
            o.Smoothness = 0.6;
            o.Alpha = c.a;
        }
        ENDCG
    }
}

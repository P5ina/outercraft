// Minecraft blocks/entities in Outer Wilds' lighting.
// Vertex data from SkyCraft's RenVertex: atlas uv, colour = tint * AO, uv2 = (block light, sky light, flags).
Shader "OuterCraft/Block"
{
    Properties
    {
        _MainTex ("Atlas", 2D) = "white" {}
        _Cutoff ("Cutoff", Range(0,1)) = 0.5
        _BlockLightColor ("Block light colour", Color) = (1, 0.85, 0.65, 1)
        _BlockLightStrength ("Block light strength", Float) = 1.2
    }
    SubShader
    {
        Tags { "RenderType"="TransparentCutout" "Queue"="AlphaTest" }
        Cull Back
        CGPROGRAM
        #pragma surface surf Standard fullforwardshadows addshadow vertex:vert alphatest:_Cutoff
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

        float Curve(float l) { return l / (4.0 - 3.0 * l); } // Minecraft's light falloff

        void surf(Input i, inout SurfaceOutputStandard o)
        {
            // flags bit 2 (value 4): untextured, colour only. No integer ops on shader model 3.
            float untextured = fmod(floor((i.light.z + 0.5) / 4.0), 2.0);
            fixed4 t = lerp(tex2D(_MainTex, i.uv_MainTex), fixed4(1, 1, 1, 1), untextured);
            fixed4 c = t * i.color;
            // Under Minecraft roofs (low sky light) ambient fades; Unity's shadows do the sun.
            float sky = Curve(saturate(i.light.y));
            o.Albedo = c.rgb * lerp(0.45, 1.0, sky);
            o.Emission = c.rgb * Curve(saturate(i.light.x)) * _BlockLightColor.rgb * _BlockLightStrength;
            o.Metallic = 0;
            o.Smoothness = 0;
            o.Alpha = c.a;
        }
        ENDCG
    }
    FallBack "Legacy Shaders/Transparent/Cutout/VertexLit"
}

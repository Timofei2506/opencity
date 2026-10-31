Shader "OpenCity/SkyboxSky"
{
    Properties
    {
        _ZenithColor ("Zenith", Color) = (0.20, 0.40, 0.72, 1)
        _HorizonColor ("Horizon", Color) = (0.72, 0.80, 0.88, 1)
        _GroundColor ("Ground", Color) = (0.18, 0.17, 0.16, 1)
        _SunColor ("Sun Color", Color) = (1.0, 0.95, 0.85, 1)
        _SunDir ("Sun Direction", Vector) = (0, 1, 0, 0)
        _SunSize ("Sun Size", Range(0.0005, 0.2)) = 0.012
        _SunGlow ("Sun Glow", Range(0, 6)) = 1.6
        _Night ("Night Amount", Range(0, 1)) = 0
        _StarStrength ("Star Strength", Range(0, 2)) = 1
        _CloudStrength ("Cloud Strength", Range(0, 1)) = 0.55
        _CloudColor ("Cloud Color", Color) = (0.95, 0.96, 1.0, 1)
    }

    SubShader
    {
        Tags { "Queue" = "Background" "RenderType" = "Background" "PreviewType" = "Skybox" }
        Cull Off
        ZWrite Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 dir : TEXCOORD0;
            };

            fixed4 _ZenithColor;
            fixed4 _HorizonColor;
            fixed4 _GroundColor;
            fixed4 _SunColor;
            fixed4 _CloudColor;
            float4 _SunDir;
            float _SunSize;
            float _SunGlow;
            float _Night;
            float _StarStrength;
            float _CloudStrength;

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.dir = v.vertex.xyz;
                return o;
            }

            float hash21(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            float valueNoise(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                float a = hash21(i);
                float b = hash21(i + float2(1, 0));
                float c = hash21(i + float2(0, 1));
                float d = hash21(i + float2(1, 1));
                return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
            }

            float fbm(float2 p)
            {
                float v = 0.0;
                float amp = 0.5;
                for (int i = 0; i < 5; i++)
                {
                    v += amp * valueNoise(p);
                    p *= 2.03;
                    amp *= 0.5;
                }
                return v;
            }

            float3 starField(float3 dir)
            {
                // cell based stars
                float2 uv = float2(atan2(dir.z, dir.x) * 1.6, asin(clamp(dir.y, -1, 1)) * 2.2);
                float2 cell = floor(uv * 90.0);
                float r = hash21(cell);
                float3 col = 0;
                if (r > 0.986)
                {
                    float2 f = frac(uv * 90.0) - 0.5;
                    float d = length(f);
                    float brightness = (r - 0.986) / 0.014;
                    col = float3(smoothstep(0.34, 0.0, d) * brightness, 0, 0);
                    col *= lerp(float3(0.85, 0.9, 1.0), float3(1.0, 0.92, 0.8), hash21(cell + 3.1));
                }
                return col;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                float3 dir = normalize(i.dir);
                float3 sunDir = normalize(_SunDir.xyz);

                float up = dir.y;
                float3 sky = lerp(_HorizonColor.rgb, _ZenithColor.rgb, pow(saturate(up), 0.55));

                // below the horizon fades into a dark ground haze
                float3 ground = _GroundColor.rgb;
                sky = lerp(sky, ground, saturate(-up * 6.0));

                // sun disk + glow
                float sunDot = saturate(dot(dir, sunDir));
                float disk = smoothstep(1.0 - _SunSize, 1.0 - _SunSize * 0.35, sunDot);
                float glow = pow(sunDot, 220.0) * 0.8 + pow(sunDot, 12.0) * 0.28 + pow(sunDot, 3.0) * 0.10;
                float horizonBoost = pow(saturate(1.0 - abs(up)), 6.0) * pow(saturate(sunDot), 3.0) * 0.5;
                sky += _SunColor.rgb * (disk * 1.5 + glow * _SunGlow + horizonBoost) * saturate(1.0 - _Night * 0.85);

                // clouds (only above the horizon)
                if (up > 0.01)
                {
                    float2 cuv = dir.xz / max(up, 0.06) * 0.55;
                    float c = fbm(cuv * 0.85 + float2(_Time.y * 0.004, _Time.y * 0.002));
                    float c2 = fbm(cuv * 2.1 - float2(_Time.y * 0.006, 0.0));
                    float cloudMask = smoothstep(0.52, 0.86, c * 0.75 + c2 * 0.35) * saturate(up * 3.5);
                    float3 cloudCol = lerp(_CloudColor.rgb * 0.55, _CloudColor.rgb, saturate(c * 1.2));
                    cloudCol *= lerp(0.55, 1.0, saturate(1.0 - _Night));
                    sky = lerp(sky, cloudCol, cloudMask * _CloudStrength);
                }

                // stars at night
                if (_Night > 0.02 && up > -0.05)
                {
                    float3 stars = starField(dir) * _Night * _StarStrength * saturate(up * 2.5 + 0.2);
                    sky += stars;
                }

                // moon
                float3 moonDir = -sunDir;
                float moonDot = saturate(dot(dir, moonDir));
                float moon = smoothstep(0.9994, 0.9997, moonDot);
                sky += float3(0.85, 0.88, 1.0) * moon * _Night * 0.9;

                return fixed4(sky, 1.0);
            }
            ENDCG
        }
    }
    Fallback Off
}

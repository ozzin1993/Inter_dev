Shader "StrategyCore/HealthBar" {
    Properties{
        _MainTex("Texture", 2D) = "black" {}
        _Fill("Fill", float) = 0
        _OutlineThickness("Outline Thickness", Float) = 0.05
        // Смещение полоски вверх-вниз в единицах меша (высота меша = 2, поэтому 2 = ровно высота полоски).
        // Считается ЗДЕСЬ, а не сдвигом объекта в мире: полоска — билборд, её высота на экране постоянна,
        // а мировой сдвиг ужимался бы косинусом наклона камеры (у нас 65 градусов) и зазор пропадал бы.
        // По умолчанию 0 — полоска здоровья ведёт себя как раньше.
        _YOffset("Смещение по вертикали (единицы меша)", Float) = 0
    }
        SubShader{
            Tags { "Queue" = "Overlay" }
            LOD 100

            Pass {
                ZTest Off
                Cull Front

                CGPROGRAM
                #pragma vertex vert
                #pragma fragment frag
            // make fog work
            #pragma multi_compile_fog

            #pragma multi_compile_instancing


            #include "UnityCG.cginc"

            struct appdata {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f {
                float2 uv : TEXCOORD0;
                float4 vertex : SV_POSITION;
                // If you need instance data in the fragment shader, uncomment next line
                //UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;

            UNITY_INSTANCING_BUFFER_START(Props)
            UNITY_DEFINE_INSTANCED_PROP(float, _Fill)
            UNITY_DEFINE_INSTANCED_PROP(float, _YOffset)
            UNITY_INSTANCING_BUFFER_END(Props)

            v2f vert(appdata v) {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                // If you need instance data in the fragment shader, uncomment next line
                // UNITY_TRANSFER_INSTANCE_ID(v, o);

                float fill = UNITY_ACCESS_INSTANCED_PROP(Props, _Fill);

                float2 scale = float2(length(unity_ObjectToWorld._m00_m10_m20), length(unity_ObjectToWorld._m01_m11_m21));
                float4 viewSpaceOrigin = mul( UNITY_MATRIX_MV, float4( 0.0, 0.0, 0.0, 1.0));
                float yOffset = UNITY_ACCESS_INSTANCED_PROP(Props, _YOffset);
                float4 scaledVertexLocalPos = float4( v.vertex.x * scale.x, (v.vertex.y + yOffset) * scale.y, 0.0, 0.0);
                o.vertex = mul( UNITY_MATRIX_P, viewSpaceOrigin + scaledVertexLocalPos);

                // generate UVs from fill level (assumed texture is clamped)
                o.uv = v.uv;
                //o.uv.x += -0.5 + (0.1f + (fill * 0.8f));
                o.uv.x += -0.5 + (0.0175 + (fill * 0.965)); // No idea why this numbers exactly, scale is X - 1, Y - 0.1
                return o;
            }

            fixed4 frag(v2f i) : SV_Target {

                // Could access instanced data here too like:
                // UNITY_SETUP_INSTANCE_ID(i);
                // UNITY_ACCESS_INSTANCED_PROP(Props, _Foo);
                // But, remember to uncomment lines flagged above

                // Scale down the UV to fit within the outline area
                float fill = UNITY_ACCESS_INSTANCED_PROP(Props, _Fill);
                float2 outlineUV = float2(1 - 0.5 + (0.0175 + (fill * 0.965)) + 0.155, 1); // No idea why this numbers exactly, scale is X - 1, Y - 0.1

                float2 uvmasks = min(i.uv, outlineUV - i.uv);
                float mask = min(uvmasks.x, uvmasks.y);

                fixed4 color = tex2D(_MainTex, i.uv);
                color = mask < 0.175 ? fixed4(0.03, 0.03, 0.03, 1.0) : color;
                //color = mask < 0.1 ? fixed4(1, 1, 1, 1.0) : color;

                return color;
            }
            ENDCG
        }
        }
}
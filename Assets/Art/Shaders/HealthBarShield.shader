Shader "StrategyCore/HealthBarShield" {
    // Серый сегмент поглощающего щита ПОВЕРХ полоски здоровья.
    // Вершинная математика скопирована из StrategyCore/HealthBar (билборд вокруг origin с масштабом
    // объекта): любой другой способ не лёг бы на полоску-билборд при поворотах камеры.
    // Сегмент задаётся долями ширины полоски: _SegStart (левый край) и _SegWidth (ширина), 0..1.
    // Константы 0.0175 и 0.965 — из шейдера полоски (его внутренняя зона под рамкой).
    Properties{
        _Color("Цвет сегмента", Color) = (0.55, 0.55, 0.55, 1)
        _HeightScale("Доля высоты полоски", Float) = 0.65
        _SegStart("Начало сегмента (0..1)", Float) = 0
        _SegWidth("Ширина сегмента (0..1)", Float) = 0
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
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"

            struct appdata {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f {
                float4 vertex : SV_POSITION;
            };

            fixed4 _Color;
            float _HeightScale;

            UNITY_INSTANCING_BUFFER_START(Props)
            UNITY_DEFINE_INSTANCED_PROP(float, _SegStart)
            UNITY_DEFINE_INSTANCED_PROP(float, _SegWidth)
            UNITY_INSTANCING_BUFFER_END(Props)

            v2f vert(appdata v) {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);

                float segStart = UNITY_ACCESS_INSTANCED_PROP(Props, _SegStart);
                float segWidth = UNITY_ACCESS_INSTANCED_PROP(Props, _SegWidth);

                float2 scale = float2(length(unity_ObjectToWorld._m00_m10_m20), length(unity_ObjectToWorld._m01_m11_m21));
                float4 viewSpaceOrigin = mul(UNITY_MATRIX_MV, float4(0.0, 0.0, 0.0, 1.0));

                // Квад 1x1 переносится в сегмент внутренней зоны полоски по её же константам.
                float xNorm = -0.5 + 0.0175 + (segStart + v.uv.x * segWidth) * 0.965;
                float4 scaledVertexLocalPos = float4(xNorm * scale.x, v.vertex.y * scale.y * _HeightScale, 0.0, 0.0);
                o.vertex = mul(UNITY_MATRIX_P, viewSpaceOrigin + scaledVertexLocalPos);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target {
                return _Color;
            }
            ENDCG
        }
    }
}

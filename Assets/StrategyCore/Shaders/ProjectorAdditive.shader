Shader "StrategyCore/ProjectorAdditive" {

	Properties{
		_Color("Tint Color", Color) = (1,1,1,1)
		_ShadowTex("Cookie", 2D) = "gray" {}
		_Attenuation("Attenuation", Float) = 0.75 // Cuts off projection above unit
	}

		Subshader{
			Tags {"Queue" = "Transparent"}
			Pass {
				ZWrite Off
				ColorMask RGB
				Blend SrcAlpha One // Additive blending
				Offset -1, -1

				CGPROGRAM
				#pragma vertex vert
				#pragma fragment frag

				#include "UnityCG.cginc"

				struct v2f {
					float4 uvShadow : TEXCOORD0;
					float4 pos : SV_POSITION;
				};
				
				float4x4 unity_Projector;

				v2f vert(float4 vertex : POSITION)
				{
					v2f o;
					o.pos = UnityObjectToClipPos(vertex);
					o.uvShadow = mul(unity_Projector, vertex);

					return o;
				}

				fixed4 _Color;
				sampler2D _ShadowTex;
				fixed _Attenuation;

				fixed4 frag(v2f i) : SV_Target
				{
					float2 uv = i.uvShadow.xz / i.uvShadow.w;
					uv.y = uv.y * 0.5;
					uv = uv + float2(0, 0.5);
					fixed4 texCookie = tex2D(_ShadowTex, uv);

					// Apply tint &amp; alpha mask
					fixed4 outColor = _Color * texCookie.a;
					// Distance attenuation (_Attenuation = 1.0 works well)
					float depth = i.uvShadow.z; // [-1(near), 1(far)]
					return outColor * clamp(1.0 - abs(depth) + _Attenuation, 0.0, 1.0);
				}
				ENDCG
			}
	}
}
Shader "StrategyCore/FoW" {

	Properties{
		_Color("Tint Color", Color) = (1,1,1,1)
		_ShadowTex("Cookie", 2D) = "gray" {}
		_ShadowTex2("Cookie", 2D) = "gray" {}
		_BlurOffset("BlurOffset", Range(0, 10)) = 1
		_LerpTime("LerpTime", Float) = 0.05
	}

		Subshader{
			Tags {"Queue" = "Transparent"}
			Pass {
				ZWrite Off
				ZTest Equal
				ColorMask RGB
				Blend DstColor Zero
				Offset 0, 0

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

				float _LerpTime;
				float _BlurOffset;
				float4 _ShadowTex_TexelSize;

				fixed4 _Color;
				sampler2D _ShadowTex;
				sampler2D _ShadowTex2;
				half fogAlpha; // Set by FogOfWar.cs

				fixed4 frag(v2f i) : SV_Target
				{
					// Projector
					float2 uv = i.uvShadow.xz / i.uvShadow.w;
					uv.y = uv.y * 0.5;
					uv = uv + float2(0, 0.5);

					float offset = _BlurOffset * _ShadowTex_TexelSize;

					// 3x3 gaussian kernel
					// https://homepages.inf.ed.ac.uk/rbf/HIPR2/gsmooth.htm
					// Above link may be a good reference of what is going on
					half GaussianKernel[9] =
					{
					 1,2,1,
					 2,4,2,
					 1,2,1
					};

					// Color accumulator
					half col = fixed4(0, 0, 0, 0);
					
					for (int x = 0; x < 3; x++)
					{
						for (int y = 0; y < 3; y++)
						{
							col += lerp(tex2D(_ShadowTex, uv + fixed2(x - 1, y - 1) * offset).a, tex2D(_ShadowTex2, uv + fixed2(x - 1, y - 1) * offset).a, _LerpTime) * GaussianKernel[x * 1 + y * 3];
						}
					}

					// Adding up all elements in the 3x3 kernel results in 16
					col /= 16;

					// Edge
					float2 maskUV = 1.0 - abs(uv * 2.0 - 1.0);
                    maskUV = saturate(maskUV / fwidth(uv * 2.0));

					float edgeFactor = maskUV.x * maskUV.y; // Factor for edge
					float edgeMask = 1.0 - edgeFactor;      // Inverse for the edge-only region

                    col = lerp(fogAlpha, col, edgeFactor);
					
					return _Color *= (1 - col);
				}
				ENDCG
			}
		}
}
void FowBlur_float(UnityTexture2D ShadowTex, UnityTexture2D ShadowTex2, float2 uv, float BlurOffset, float fogAlpha, float LerpTime, UnitySamplerState Sampler, out float Out_Alpha)
{
    float offset = BlurOffset * _ShadowTex_TexelSize;

    // 3x3 gaussian kernel
    // https://homepages.inf.ed.ac.uk/rbf/HIPR2/gsmooth.htm
    // Above link may be a good reference of what is going on
    half GaussianKernel[9] =
    {
        1, 2, 1,
        2, 4, 2,
        1, 2, 1
    };

    // Color accumulator
    float col = 0;
					
    for (int x = 0; x < 3; x++)
    {
        for (int y = 0; y < 3; y++)
        {
            float tex1 = ShadowTex.Sample(Sampler, uv + float2(x - 1, y - 1) * offset).a;
            float tex2 = ShadowTex2.Sample(Sampler, uv + float2(x - 1, y - 1) * offset).a;
            
            col += lerp(tex1, tex2, LerpTime) * GaussianKernel[x * 1 + y * 3];
        }
    }

    // Adding up all elements in the 3x3 kernel results in 16
    col /= 16;

    // Edge
    // float2 maskUV = 1.0 - abs(uv * 2.0 - 1.0);
    // maskUV = saturate(maskUV / fwidth(uv * 2.0));
    // 
    // float edgeFactor = maskUV.x * maskUV.y; // Factor for edge
    // float edgeMask = 1.0 - edgeFactor; // Inverse for the edge-only region
    // 
    // col = lerp(fogAlpha, col, edgeFactor);
    
    Out_Alpha = col;
}
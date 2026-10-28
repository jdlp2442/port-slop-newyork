using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace GunnellSmoke
{
    /// <summary>Persistent GPU growth resources; no texture upload per growth frame.</summary>
    internal sealed class GunnellSmokeGrowth : IDisposable
    {
        private readonly ComputeShader shader;
        private readonly Texture3D arrival;
        private readonly RenderTexture density, a, b;
        private readonly Vector3Int size;
        private readonly int[] dimensions=new int[3];
        private readonly int prepare, blur, resolve;
        public RTHandle Handle { get; }
        public Texture Texture => density;

        public static bool Supported => SystemInfo.supportsComputeShaders &&
            SystemInfo.SupportsRandomWriteOnRenderTextureFormat(RenderTextureFormat.RGFloat) &&
            SystemInfo.SupportsRandomWriteOnRenderTextureFormat(RenderTextureFormat.RFloat) &&
            Resources.Load<ComputeShader>("GunnellSmokeGrowth") != null;

        public GunnellSmokeGrowth(Vector3Int size, float[] distance, float[] delays, byte[] solids, float[] contacts)
        {
            this.size = size;
            dimensions[0]=size.x;dimensions[1]=size.y;dimensions[2]=size.z;
            shader = Resources.Load<ComputeShader>("GunnellSmokeGrowth");
            prepare = shader.FindKernel("Prepare"); blur = shader.FindKernel("Blur"); resolve = shader.FindKernel("Resolve");
            var field = new Vector4[distance.Length];
            for (int i=0;i<field.Length;i++)
            {
                bool unreachable = float.IsPositiveInfinity(distance[i]);
                float mask = solids[i] == 2 ? contacts[i] : unreachable ? 0f : solids[i] == 3 ? contacts[i] : 1f;
                field[i] = new Vector4(unreachable ? 1e20f : distance[i], delays[i],mask,solids[i] == 2 ? 1f : 0f);
            }
            arrival = new Texture3D(size.x,size.y,size.z,TextureFormat.RGBAFloat,false)
            { name="Gunnell Smoke Static Arrival",filterMode=FilterMode.Point,wrapMode=TextureWrapMode.Clamp };
            arrival.SetPixelData(field,0); arrival.Apply(false,true);
            density = Create("Gunnell Smoke Growing Density",RenderTextureFormat.RGFloat);
            a = Create("Gunnell Smoke Growth A",RenderTextureFormat.RFloat);
            b = Create("Gunnell Smoke Growth B",RenderTextureFormat.RFloat);
            Handle = RTHandles.Alloc(density);
        }

        private RenderTexture Create(string name, RenderTextureFormat format)
        {
            var texture = new RenderTexture(size.x,size.y,0,format)
            { name=name,dimension=TextureDimension.Tex3D,volumeDepth=size.z,enableRandomWrite=true,
                filterMode=FilterMode.Bilinear,wrapMode=TextureWrapMode.Clamp };
            texture.Create(); return texture;
        }

        private void Dispatch(int kernel)
        {
            shader.SetTexture(kernel,"_Arrival",arrival);
            shader.Dispatch(kernel,(size.x+3)/4,(size.y+3)/4,(size.z+3)/4);
        }

        public void Update(float distance, float releaseTail)
        {
            shader.SetInts("_Size",dimensions);
            shader.SetFloat("_Distance",distance); shader.SetFloat("_ReleaseTail",releaseTail);
            shader.SetTexture(prepare,"_Output",a); Dispatch(prepare);
            for (int axis=0;axis<3;axis++)
            {
                shader.SetInt("_Axis",axis);
                shader.SetTexture(blur,"_Input",axis % 2 == 0 ? a : b);
                shader.SetTexture(blur,"_Output",axis % 2 == 0 ? b : a);
                Dispatch(blur);
            }
            shader.SetTexture(resolve,"_Input",b); shader.SetTexture(resolve,"_Density",density); Dispatch(resolve);
        }

        public void Dispose()
        {
            Handle?.Release();
            foreach (var texture in new[]{density,a,b}) { texture.Release(); CoreUtils.Destroy(texture); }
            CoreUtils.Destroy(arrival);
        }
    }
}

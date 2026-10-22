// Stubs for Collections / Jobs / Burst + instanced rendering, used only by the type-check harness.
using System;

namespace UnityEngine
{
    public struct Matrix4x4
    {
        public static Matrix4x4 TRS(Vector3 pos, Quaternion rot, Vector3 scale) => new Matrix4x4();
        public static Matrix4x4 identity => new Matrix4x4();
    }

    public struct Bounds
    {
        public Vector3 center, size;
        public Bounds(Vector3 center, Vector3 size) { this.center = center; this.size = size; }
    }

    public struct RenderParams
    {
        public RenderParams(Material m)
        {
            material = m;
            worldBounds = default;
            shadowCastingMode = default;
            receiveShadows = false;
            matProps = null;
        }
        public Material material;
        public Bounds worldBounds;
        public Rendering.ShadowCastingMode shadowCastingMode;
        public bool receiveShadows;
        public MaterialPropertyBlock matProps;
    }

    public enum ComputeBufferType { Default, Structured, IndirectArguments }

    public class ComputeBuffer : IDisposable
    {
        public ComputeBuffer(int count, int stride) { }
        public ComputeBuffer(int count, int stride, ComputeBufferType type) { }
        public void SetData(Array data) { }
        public void SetData(Array data, int managedBufferStartIndex, int computeBufferStartIndex, int count) { }
        public void Release() { }
        public void Dispose() { }
    }

    public static class Graphics
    {
        public static void RenderMeshInstanced(in RenderParams rparams, Mesh mesh, int submeshIndex,
                                               Unity.Collections.NativeArray<Matrix4x4> instanceData,
                                               int instanceCount = -1, int startInstance = 0) { }

        public static void RenderMeshInstanced(in RenderParams rparams, Mesh mesh, int submeshIndex,
                                               Matrix4x4[] instanceData,
                                               int instanceCount = -1, int startInstance = 0) { }

        public static void RenderMeshInstanced(in RenderParams rparams, Mesh mesh, int submeshIndex,
                                               System.Collections.Generic.List<Matrix4x4> instanceData,
                                               int instanceCount = -1, int startInstance = 0) { }

        public static void DrawMeshInstancedIndirect(Mesh mesh, int submeshIndex, Material material,
                                                     Bounds bounds, ComputeBuffer argsBuffer) { }
    }
}

namespace Unity.Collections
{
    public enum Allocator { Invalid, None, Temp, TempJob, Persistent }

    public class ReadOnlyAttribute : Attribute { }

    public struct NativeArray<T> : IDisposable where T : struct
    {
        T[] _data;

        public NativeArray(int length, Allocator allocator)
        {
            _data = new T[length];
            Length = length;
        }

        public NativeArray(T[] array, Allocator allocator)
        {
            _data = (T[])array.Clone();
            Length = array.Length;
        }

        public int Length { get; private set; }
        public bool IsCreated => _data != null;

        public T this[int index]
        {
            get => _data[index];
            set => _data[index] = value;
        }

        public void Dispose()
        {
            _data = null;
            Length = 0;
        }
    }
}

namespace Unity.Jobs
{
    public interface IJobParallelFor
    {
        void Execute(int index);
    }

    public struct JobHandle
    {
        public void Complete() { }
    }

    public static class IJobParallelForExtensions
    {
        public static JobHandle Schedule<T>(this T job, int length, int batchCount, JobHandle dependsOn = default)
            where T : struct, IJobParallelFor
        {
            for (int i = 0; i < length; i++) job.Execute(i);
            return default;
        }
    }
}

namespace Unity.Burst
{
    public class BurstCompileAttribute : Attribute { }
}

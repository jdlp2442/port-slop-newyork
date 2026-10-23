// Stubs for Collections / Jobs / Burst + instanced rendering, used only by the type-check harness.
using System;

namespace UnityEngine
{
    public struct Matrix4x4
    {
        public float m00, m01, m02, m03;
        public float m10, m11, m12, m13;
        public float m20, m21, m22, m23;
        public float m30, m31, m32, m33;

        public static Matrix4x4 TRS(Vector3 pos, Quaternion rot, Vector3 scale) => new Matrix4x4();
        public static Matrix4x4 identity => new Matrix4x4();
        public Matrix4x4 inverse => this;
    }

    public struct Bounds
    {
        public Vector3 center, size;
        public Vector3 extents => size * 0.5f;
        public Bounds(Vector3 center, Vector3 size) { this.center = center; this.size = size; }
        public void Encapsulate(Bounds b) { }
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

        public unsafe void* GetUnsafePtr()
        {
            if (_data == null || _data.Length == 0) return null;
            return (void*)System.Runtime.InteropServices.Marshal.UnsafeAddrOfPinnedArrayElement(_data, 0);
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

namespace Unity.Collections.LowLevel.Unsafe
{
    public static class NativeArrayUnsafeUtility
    {
        public static unsafe void* GetUnsafePtr<T>(this NativeArray<T> nativeArray) where T : struct => nativeArray.GetUnsafePtr();
        public static unsafe void* GetUnsafeReadOnlyPtr<T>(this NativeArray<T> nativeArray) where T : struct => nativeArray.GetUnsafePtr();
    }

    public static class UnsafeUtility
    {
        public static int AlignOf<T>() where T : struct => 16;
        public static int SizeOf<T>() where T : struct => System.Runtime.InteropServices.Marshal.SizeOf<T>();
        public static unsafe void* Malloc(long size, int alignment, Allocator allocator) => (void*)System.Runtime.InteropServices.Marshal.AllocHGlobal((int)size);
        public static unsafe void Free(void* memory, Allocator allocator) => System.Runtime.InteropServices.Marshal.FreeHGlobal((IntPtr)memory);
    }
}

namespace UnityEngine
{
    public struct GraphicsBufferHandle { public uint value; }

    public class GraphicsBuffer : IDisposable
    {
        public enum Target { Vertex = 1, Index = 2, CopySource = 4, CopyDestination = 8, Structured = 16, Raw = 32 }
        public enum UsageFlags { None = 0, LockBufferForWrite = 1 }

        public GraphicsBuffer(Target target, int count, int stride) { }
        public GraphicsBufferHandle bufferHandle => default;
        public void SetData(Array data, int managedBufferStartIndex, int graphicsBufferStartIndex, int count) { }
        public void Dispose() { }
    }
}

namespace UnityEngine.Rendering
{
    public struct BatchID { public uint value; }
    public struct BatchMeshID { public uint value; }
    public struct BatchMaterialID { public uint value; }

    public struct MetadataValue
    {
        public int NameID;
        public uint Value;
    }

    public enum BatchDrawCommandFlags { None = 0, HasMotion = 1 }
    public enum BatchDrawCommandType { Direct = 0, Indirect = 1 }

    public struct BatchFilterSettings
    {
        public uint renderingLayerMask;
        public byte layer;
    }

    public struct BatchDrawCommand
    {
        public uint visibleOffset;
        public uint visibleCount;
        public BatchID batchID;
        public BatchMaterialID materialID;
        public BatchMeshID meshID;
        public ushort submeshIndex;
        public ushort splitVisibilityMask;
        public BatchDrawCommandFlags flags;
        public int sortingPosition;
    }

    public struct BatchDrawRange
    {
        public BatchDrawCommandType drawCommandsType;
        public uint drawCommandsBegin;
        public uint drawCommandsCount;
        public BatchFilterSettings filterSettings;
    }

    public unsafe struct BatchCullingOutputDrawCommands
    {
        public BatchDrawCommand* drawCommands;
        public int* visibleInstances;
        public BatchDrawRange* drawRanges;
        public int drawCommandCount;
        public int visibleInstanceCount;
        public int drawRangeCount;
    }

    public struct BatchCullingContext { }

    public struct BatchCullingOutput
    {
        public Unity.Collections.NativeArray<BatchCullingOutputDrawCommands> drawCommands;
    }

    public class BatchRendererGroup : IDisposable
    {
        public delegate Unity.Jobs.JobHandle OnPerformCulling(BatchRendererGroup rendererGroup, BatchCullingContext cullingContext, BatchCullingOutput cullingOutput, IntPtr userContext);

        public BatchRendererGroup(OnPerformCulling callback, IntPtr userContext) { }
        public void SetGlobalBounds(Bounds bounds) { }
        public BatchMeshID RegisterMesh(Mesh mesh) => default;
        public BatchMaterialID RegisterMaterial(Material material) => default;
        public BatchID AddBatch(Unity.Collections.NativeArray<MetadataValue> metadata, GraphicsBufferHandle buffer) => new BatchID { value = 1 };
        public BatchID AddBatch(Unity.Collections.NativeArray<MetadataValue> metadata, GraphicsBufferHandle buffer, uint offset, uint windowSize) => new BatchID { value = 1 };
        public void Dispose() { }
    }
}

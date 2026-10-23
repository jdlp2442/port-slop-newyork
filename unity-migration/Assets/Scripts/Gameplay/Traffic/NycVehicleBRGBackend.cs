using System;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using UnityEngine;
using UnityEngine.Rendering;

namespace Nyc.Gameplay.Traffic
{
    /// <summary>
    /// Experimental BatchRendererGroup (BRG) backend for vehicle instanced drawing in Unity 6.
    /// Provides DOTS instancing compatibility, direct GPU buffer uploads, and custom visibility culling.
    /// Includes automatic fallback to Graphics.RenderMeshInstanced if BRG is unavailable.
    /// </summary>
    public sealed class NycVehicleBRGBackend : IDisposable
    {
        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        public struct PackedMatrix
        {
            public float m00, m01, m02, m03;
            public float m10, m11, m12, m13;
            public float m20, m21, m22, m23;

            public PackedMatrix(Matrix4x4 m)
            {
                m00 = m.m00; m01 = m.m01; m02 = m.m02; m03 = m.m03;
                m10 = m.m10; m11 = m.m11; m12 = m.m12; m13 = m.m13;
                m20 = m.m20; m21 = m.m21; m22 = m.m22; m23 = m.m23;
            }
        }

        BatchRendererGroup _brg;
        BatchID _batchID;
        BatchMeshID _meshID;
        BatchMaterialID _materialID;
        GraphicsBuffer _buffer;

        PackedMatrix[] _packedObjectToWorld;
        PackedMatrix[] _packedWorldToObject;

        int _capacity;
        int _activeCount;
        bool _initialized;

        public bool IsAvailable => _initialized && _brg != null;
        public int Capacity => _capacity;
        public int ActiveCount => _activeCount;

        public bool Initialize(Mesh mesh, Material material, int capacity = 512)
        {
            if (_initialized) return true;
            if (mesh == null || material == null) return false;

            _capacity = Mathf.Max(capacity, 16);
            _packedObjectToWorld = new PackedMatrix[_capacity];
            _packedWorldToObject = new PackedMatrix[_capacity];

            try
            {
                _brg = new BatchRendererGroup(OnPerformCulling, IntPtr.Zero);
                _brg.SetGlobalBounds(new Bounds(Vector3.zero, Vector3.one * 30000f));

                _meshID = _brg.RegisterMesh(mesh);
                _materialID = _brg.RegisterMaterial(material);

                // Raw buffer: capacity * 48 bytes for ObjectToWorld + capacity * 48 bytes for WorldToObject
                int matrixBytes = _capacity * 48;
                int totalBytes = matrixBytes * 2;
                int totalInts = totalBytes / 4;

                _buffer = new GraphicsBuffer(GraphicsBuffer.Target.Raw, totalInts, 4);

                var metadata = new NativeArray<MetadataValue>(2, Allocator.Temp);
                metadata[0] = new MetadataValue
                {
                    NameID = Shader.PropertyToID("unity_ObjectToWorld"),
                    Value = 0x80000000 | 0u
                };
                metadata[1] = new MetadataValue
                {
                    NameID = Shader.PropertyToID("unity_WorldToObject"),
                    Value = 0x80000000 | (uint)matrixBytes
                };

                _batchID = _brg.AddBatch(metadata, _buffer.bufferHandle);
                metadata.Dispose();

                if (_batchID.value == 0)
                {
                    Debug.LogWarning("[NYC BRG] AddBatch returned invalid BatchID 0, falling back to legacy.");
                    Dispose();
                    return false;
                }

                _initialized = true;
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[NYC BRG] Failed to initialize BatchRendererGroup backend, falling back to legacy: {ex.Message}");
                Dispose();
                return false;
            }
        }

        public void UpdateInstances(NativeArray<Matrix4x4> matrices, int count)
        {
            if (!_initialized || _buffer == null) return;

            int validCount = Mathf.Min(count, _capacity);
            _activeCount = validCount;

            for (int i = 0; i < validCount; i++)
            {
                Matrix4x4 m = matrices[i];
                _packedObjectToWorld[i] = new PackedMatrix(m);
                _packedWorldToObject[i] = new PackedMatrix(m.inverse);
            }

            // Zero out remainder up to capacity or previous count if needed
            for (int i = validCount; i < _capacity; i++)
            {
                _packedObjectToWorld[i] = default;
                _packedWorldToObject[i] = default;
            }

            // Upload ObjectToWorld array (starting at element index 0)
            _buffer.SetData(_packedObjectToWorld, 0, 0, validCount);

            // Upload WorldToObject array (starting at element index _capacity -> byte offset _capacity * 48)
            _buffer.SetData(_packedWorldToObject, 0, _capacity, validCount);
        }

        unsafe JobHandle OnPerformCulling(BatchRendererGroup rendererGroup, BatchCullingContext cullingContext, BatchCullingOutput cullingOutput, IntPtr userContext)
        {
            int count = _activeCount;
            int alignment = UnsafeUtility.AlignOf<long>();
            var drawCommands = (BatchCullingOutputDrawCommands*)cullingOutput.drawCommands.GetUnsafePtr();

            if (!_initialized || _batchID.value == 0 || count <= 0)
            {
                drawCommands->drawCommandCount = 0;
                drawCommands->drawRangeCount = 0;
                drawCommands->visibleInstanceCount = 0;
                return default;
            }

            drawCommands->drawCommands = (BatchDrawCommand*)UnsafeUtility.Malloc(
                UnsafeUtility.SizeOf<BatchDrawCommand>(), alignment, Allocator.TempJob);
            drawCommands->drawRanges = (BatchDrawRange*)UnsafeUtility.Malloc(
                UnsafeUtility.SizeOf<BatchDrawRange>(), alignment, Allocator.TempJob);
            drawCommands->visibleInstances = (int*)UnsafeUtility.Malloc(
                count * sizeof(int), alignment, Allocator.TempJob);

            drawCommands->drawCommandCount = 1;
            drawCommands->drawRangeCount = 1;
            drawCommands->visibleInstanceCount = count;

            drawCommands->drawCommands[0] = new BatchDrawCommand
            {
                visibleOffset = 0,
                visibleCount = (uint)count,
                batchID = _batchID,
                materialID = _materialID,
                meshID = _meshID,
                submeshIndex = 0,
                splitVisibilityMask = 0xFFFF,
                flags = BatchDrawCommandFlags.None,
                sortingPosition = 0
            };

            drawCommands->drawRanges[0] = new BatchDrawRange
            {
                drawCommandsType = BatchDrawCommandType.Direct,
                drawCommandsBegin = 0,
                drawCommandsCount = 1,
                filterSettings = new BatchFilterSettings
                {
                    renderingLayerMask = 1,
                    layer = 0
                }
            };

            for (int i = 0; i < count; i++)
            {
                drawCommands->visibleInstances[i] = i;
            }

            return default;
        }

        public void Dispose()
        {
            if (_brg != null)
            {
                try { _brg.Dispose(); } catch { }
                _brg = null;
            }
            if (_buffer != null)
            {
                try { _buffer.Dispose(); } catch { }
                _buffer = null;
            }
            _initialized = false;
            _activeCount = 0;
        }
    }
}

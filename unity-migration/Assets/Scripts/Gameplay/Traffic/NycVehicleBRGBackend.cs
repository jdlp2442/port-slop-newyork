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
            // Unity's DOTS instancing contract is column-major float3x4, not a row-major
            // Matrix4x4 dump. Keep the field order identical to the official BRG sample.
            public float c0x, c0y, c0z;
            public float c1x, c1y, c1z;
            public float c2x, c2y, c2z;
            public float c3x, c3y, c3z;

            public PackedMatrix(Matrix4x4 m)
            {
                c0x = m.m00; c0y = m.m10; c0z = m.m20;
                c1x = m.m01; c1y = m.m11; c1z = m.m21;
                c2x = m.m02; c2y = m.m12; c2z = m.m22;
                c3x = m.m03; c3y = m.m13; c3z = m.m23;
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

        const int PackedMatrixBytes = 48;
        const int InstanceDataPreambleBytes = 96;

        /// <summary>Validates the raw-buffer contract without requiring a graphics device.</summary>
        public static bool ValidateMetadataLayout(int capacity, out string error)
        {
            capacity = Mathf.Max(capacity, 16);
            int objectOffset = InstanceDataPreambleBytes;
            int worldOffset = objectOffset + capacity * PackedMatrixBytes;
            int totalBytes = worldOffset + capacity * PackedMatrixBytes;
            if (System.Runtime.InteropServices.Marshal.SizeOf<PackedMatrix>() != PackedMatrixBytes)
            {
                error = "PackedMatrix must remain a 48-byte float3x4 layout.";
                return false;
            }
            if ((objectOffset % PackedMatrixBytes) != 0 || (worldOffset % PackedMatrixBytes) != 0)
            {
                error = "Matrix metadata offsets are not aligned to the packed matrix stride.";
                return false;
            }
            if ((totalBytes & 3) != 0 || totalBytes <= worldOffset)
            {
                error = "Raw instance buffer size is invalid.";
                return false;
            }
            error = string.Empty;
            return true;
        }

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

            if (!ValidateMetadataLayout(_capacity, out string layoutError))
            {
                Debug.LogWarning($"[NYC BRG] Invalid metadata layout: {layoutError}");
                Dispose();
                return false;
            }

            // Keep a 96-byte zero/padding prefix, then ObjectToWorld and WorldToObject packed
            // float3x4 arrays. The prefix guarantees safe zero loads for unspecified DOTS props.
            int matrixBytes = _capacity * PackedMatrixBytes;
            int objectOffsetBytes = InstanceDataPreambleBytes;
            int worldOffsetBytes = objectOffsetBytes + matrixBytes;
            int totalBytes = worldOffsetBytes + matrixBytes;
                int totalInts = totalBytes / 4;

                _buffer = new GraphicsBuffer(GraphicsBuffer.Target.Raw, totalInts, 4);

                // Address 0 is the default value for any DOTS property not supplied in metadata.
                // Explicitly clear the 96-byte prefix so that fallback loads are deterministic.
                _buffer.SetData(new Vector4[InstanceDataPreambleBytes / 16], 0, 0, InstanceDataPreambleBytes / 16);

                var metadata = new NativeArray<MetadataValue>(2, Allocator.Temp);
                metadata[0] = new MetadataValue
                {
                    NameID = Shader.PropertyToID("unity_ObjectToWorld"),
                    Value = 0x80000000u | (uint)objectOffsetBytes
                };
                metadata[1] = new MetadataValue
                {
                    NameID = Shader.PropertyToID("unity_WorldToObject"),
                    Value = 0x80000000u | (uint)worldOffsetBytes
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

            // Upload the two packed arrays at their byte-addressed metadata offsets.
            _buffer.SetData(_packedObjectToWorld, 0, InstanceDataPreambleBytes / PackedMatrixBytes, validCount);

            _buffer.SetData(_packedWorldToObject, 0,
                (InstanceDataPreambleBytes / PackedMatrixBytes) + _capacity, validCount);
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

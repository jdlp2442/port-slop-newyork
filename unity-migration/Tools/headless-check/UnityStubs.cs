// Minimal Unity API stubs used only to type-check the migration scripts.
// NOT part of the deliverable.
using System;
using System.Collections.Generic;

namespace UnityEngine
{
    public class Object
    {
        public string name;
        public static implicit operator bool(Object o) => o != null;
        public static void Destroy(Object o) { }
        public static void DestroyImmediate(Object o) { }
        public static T FindFirstObjectByType<T>() where T : Object => null;
    }

    public struct Vector2
    {
        public float x, y;
        public Vector2(float x, float y) { this.x = x; this.y = y; }
        public float magnitude => (float)Math.Sqrt(x * x + y * y);
        public float sqrMagnitude => x * x + y * y;
        public Vector2 normalized { get { float m = magnitude; return m > 1e-9f ? new Vector2(x / m, y / m) : zero; } }
        public static Vector2 zero => new Vector2(0, 0);
        public static Vector2 one => new Vector2(1, 1);
        public static Vector2 up => new Vector2(0, 1);
        public static float Dot(Vector2 a, Vector2 b) => a.x * b.x + a.y * b.y;
        public static float Distance(Vector2 a, Vector2 b) => (a - b).magnitude;
        public static Vector2 Lerp(Vector2 a, Vector2 b, float t) => new Vector2(a.x + (b.x - a.x) * t, a.y + (b.y - a.y) * t);
        public static Vector2 operator +(Vector2 a, Vector2 b) => new Vector2(a.x + b.x, a.y + b.y);
        public static Vector2 operator -(Vector2 a, Vector2 b) => new Vector2(a.x - b.x, a.y - b.y);
        public static Vector2 operator *(Vector2 a, float s) => new Vector2(a.x * s, a.y * s);
        public static Vector2 operator *(float s, Vector2 a) => new Vector2(a.x * s, a.y * s);
        public static Vector2 operator /(Vector2 a, float s) => new Vector2(a.x / s, a.y / s);
        public static Vector2 operator -(Vector2 a) => new Vector2(-a.x, -a.y);
    }

    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
        public static Vector3 zero => new Vector3(0, 0, 0);
        public static Vector3 one => new Vector3(1, 1, 1);
        public static Vector3 up => new Vector3(0, 1, 0);
        public static Vector3 down => new Vector3(0, -1, 0);
        public static Vector3 forward => new Vector3(0, 0, 1);
        public static Vector3 back => new Vector3(0, 0, -1);
        public static Vector3 left => new Vector3(-1, 0, 0);
        public static Vector3 right => new Vector3(1, 0, 0);
        public float sqrMagnitude => x * x + y * y + z * z;
        public float magnitude => (float)Math.Sqrt(sqrMagnitude);
        public Vector3 normalized { get { float m = magnitude; return m > 1e-9f ? new Vector3(x / m, y / m, z / m) : zero; } }
        public static Vector3 Slerp(Vector3 a, Vector3 b, float t) => a;
        public static Vector3 Lerp(Vector3 a, Vector3 b, float t) => new Vector3(a.x + (b.x - a.x) * t, a.y + (b.y - a.y) * t, a.z + (b.z - a.z) * t);
        public static Vector3 Cross(Vector3 a, Vector3 b) =>
            new Vector3(a.y * b.z - a.z * b.y, a.z * b.x - a.x * b.z, a.x * b.y - a.y * b.x);
        public static float Dot(Vector3 a, Vector3 b) => a.x * b.x + a.y * b.y + a.z * b.z;
        public static Vector3 operator +(Vector3 a, Vector3 b) => new Vector3(a.x + b.x, a.y + b.y, a.z + b.z);
        public static Vector3 operator -(Vector3 a, Vector3 b) => new Vector3(a.x - b.x, a.y - b.y, a.z - b.z);
        public static Vector3 operator *(Vector3 a, float s) => new Vector3(a.x * s, a.y * s, a.z * s);
        public static Vector3 operator /(Vector3 a, float s) => new Vector3(a.x / s, a.y / s, a.z / s);
        public static Vector3 operator *(float s, Vector3 a) => new Vector3(a.x * s, a.y * s, a.z * s);
        public static Vector3 operator -(Vector3 a) => new Vector3(-a.x, -a.y, -a.z);
        public static bool operator ==(Vector3 a, Vector3 b) => a.x == b.x && a.y == b.y && a.z == b.z;
        public static bool operator !=(Vector3 a, Vector3 b) => !(a == b);
        public static implicit operator Vector2(Vector3 v) => new Vector2(v.x, v.y);
        public override bool Equals(object o) => o is Vector3 v && v == this;
        public override int GetHashCode() => 0;
    }

    public struct Vector4
    {
        public float x, y, z, w;
        public Vector4(float x, float y, float z, float w) { this.x = x; this.y = y; this.z = z; this.w = w; }
        public static Vector4 zero => new Vector4(0, 0, 0, 0);
        public static implicit operator Vector4(Color c) => new Vector4(c.r, c.g, c.b, c.a);
        public static implicit operator Vector4(Vector3 v) => new Vector4(v.x, v.y, v.z, 0f);
        public static bool operator ==(Vector4 a, Vector4 b) => a.x == b.x && a.y == b.y && a.z == b.z && a.w == b.w;
        public static bool operator !=(Vector4 a, Vector4 b) => !(a == b);
        public override bool Equals(object o) => o is Vector4 v && v == this;
        public override int GetHashCode() => 0;
    }

    public struct Vector2Int
    {
        public int x, y;
        public Vector2Int(int x, int y) { this.x = x; this.y = y; }
    }

    public struct Color
    {
        public float r, g, b, a;
        public Color(float r, float g, float b, float a = 1f) { this.r = r; this.g = g; this.b = b; this.a = a; }
        public static Color white => new Color(1, 1, 1, 1);
        public Color linear => this;
        public static Color Lerp(Color x, Color y, float t) =>
            new Color(x.r + (y.r - x.r) * t, x.g + (y.g - x.g) * t, x.b + (y.b - x.b) * t, x.a + (y.a - x.a) * t);
        public static Color operator *(Color c, float s) => new Color(c.r * s, c.g * s, c.b * s, c.a * s);
    }

    public struct Color32
    {
        public byte r, g, b, a;
        public Color32(byte r, byte g, byte b, byte a) { this.r = r; this.g = g; this.b = b; this.a = a; }
        public static implicit operator Color32(Color c) =>
            new Color32((byte)(c.r * 255), (byte)(c.g * 255), (byte)(c.b * 255), (byte)(c.a * 255));
    }

    public struct Quaternion
    {
        public static Quaternion Euler(float x, float y, float z) => new Quaternion();
        public static Quaternion LookRotation(Vector3 forward, Vector3 up) => new Quaternion();
        public static Vector3 operator *(Quaternion q, Vector3 v) => v;
        public static Quaternion operator *(Quaternion a, Quaternion b) => new Quaternion();
    }

    public static class Mathf
    {
        public const float PI = (float)Math.PI;
        public const float Rad2Deg = 57.29578f;
        public const float Deg2Rad = 0.017453292f;
        public static float Min(float a, float b) => Math.Min(a, b);
        public static int Min(int a, int b) => Math.Min(a, b);
        public static float Max(float a, float b) => Math.Max(a, b);
        public static int Max(int a, int b) => Math.Max(a, b);
        public static float Abs(float a) => Math.Abs(a);
        public static float Sqrt(float a) => (float)Math.Sqrt(a);
        public static float Pow(float a, float b) => (float)Math.Pow(a, b);
        public static float Cos(float a) => (float)Math.Cos(a);
        public static float Sin(float a) => (float)Math.Sin(a);
        public static float Atan2(float y, float x) => (float)Math.Atan2(y, x);
        public static float Clamp(float v, float a, float b) => v < a ? a : v > b ? b : v;
        public static int Clamp(int v, int a, int b) => v < a ? a : v > b ? b : v;
        public static float Ceil(float v) => (float)Math.Ceiling(v);
        public static float Clamp01(float v) => v < 0 ? 0 : v > 1 ? 1 : v;
        public static float Lerp(float a, float b, float t) => a + (b - a) * t;
        public static float InverseLerp(float a, float b, float v) => (a != b) ? Clamp01((v - a) / (b - a)) : 0f;
        public static float Log(float v, float b) => (float)(Math.Log(v) / Math.Log(b));
        public static float Exp(float v) => (float)Math.Exp(v);
        public static float Sign(float v) => v >= 0f ? 1f : -1f;
        public static float Repeat(float t, float length) => t - (float)Math.Floor(t / length) * length;
        public static float Round(float v) => (float)Math.Round(v);
        public static float Floor(float v) => (float)Math.Floor(v);
        public static int RoundToInt(float v) => (int)Math.Round(v);
        public static int FloorToInt(float v) => (int)Math.Floor(v);
        public static int CeilToInt(float v) => (int)Math.Ceiling(v);
        public static bool Approximately(float a, float b) => Math.Abs(a - b) < 1e-6f;
    }

    public class Component : Object
    {
        public Transform transform;
        public GameObject gameObject;
        public T GetComponent<T>() where T : Component => gameObject != null ? gameObject.GetComponent<T>() : null;
        public T GetComponentInChildren<T>(bool includeInactive) where T : Component => GetComponent<T>();
        public T[] GetComponentsInChildren<T>(bool includeInactive) => Array.Empty<T>();
    }

    public class Transform : Component
    {
        public Transform parent;
        public Vector3 position;
        public Quaternion rotation;
        public Vector3 localPosition;
        public Quaternion localRotation;
        public Vector3 localScale = Vector3.one;
        public List<Transform> children = new List<Transform>();
        public void SetParent(Transform p, bool worldPositionStays) { parent = p; p?.children.Add(this); }
        public void SetPositionAndRotation(Vector3 pos, Quaternion rot) { position = pos; rotation = rot; }
        public Vector3 forward => new Vector3(0, 0, 1);
        public Transform Find(string n) => children.Find(c => c.gameObject != null && c.gameObject.name == n);
    }

    public class GameObject : Object
    {
        public static readonly List<GameObject> All = new List<GameObject>();
        public Transform transform;
        public readonly List<Component> components = new List<Component>();
        public GameObject() { transform = new Transform { gameObject = this }; All.Add(this); }
        public GameObject(string name) : this() { this.name = name; }
        public T AddComponent<T>() where T : Component, new()
        {
            var c = new T { gameObject = this, transform = transform };
            components.Add(c);
            return c;
        }
        public T GetComponent<T>() where T : Component
        {
            foreach (var c in components) if (c is T t) return t;
            return null;
        }
        public void SetActive(bool value) { }
        public static GameObject Find(string name) => All.Find(g => g.name == name);
    }

    public class Coroutine { }

    public class MonoBehaviour : Component
    {
        public bool enabled;
        public bool isActiveAndEnabled => true;
        public Coroutine StartCoroutine(System.Collections.IEnumerator routine) => null;
        public void StopCoroutine(System.Collections.IEnumerator routine) { }
    }

    public class Mesh : Object
    {
        public Rendering.IndexFormat indexFormat;
        public int subMeshCount = 1;
        public List<Vector3> verts = new List<Vector3>();
        public List<Vector3> normals = new List<Vector3>();
        public List<Vector2>[] uvs = { new List<Vector2>(), new List<Vector2>(), new List<Vector2>(), new List<Vector2>() };
        public List<Color32> colors = new List<Color32>();
        public List<int>[] tris = Array.Empty<List<int>>();

        public void SetVertices(List<Vector3> v) { verts = new List<Vector3>(v); }
        public void SetNormals(List<Vector3> n) { normals = new List<Vector3>(n); }
        public void SetUVs(int channel, List<Vector2> uv) { uvs[channel] = new List<Vector2>(uv); }
        public void SetColors(List<Color32> c) { colors = new List<Color32>(c); }
        public void SetTriangles(List<int> t, int submesh, bool calculateBounds = true)
        {
            if (tris.Length != subMeshCount) tris = new List<int>[subMeshCount];
            tris[submesh] = new List<int>(t);
        }
        public uint GetIndexCount(int submesh) => 0;
        public void RecalculateBounds() { }
        public void RecalculateNormals() { }

        public Vector3[] vertices { get => verts.ToArray(); set => verts = new List<Vector3>(value); }
        public int[] triangles { get => tris.Length > 0 ? tris[0].ToArray() : Array.Empty<int>(); set { if (tris.Length == 0) tris = new List<int>[1]; tris[0] = new List<int>(value); } }
    }

    public class MeshFilter : Component
    {
        Mesh m;
        public Mesh sharedMesh
        {
            get => m;
            set { m = value; if (value != null) global::Capture.Meshes.Add(value); }
        }
    }
    public class Renderer : Component
    {
        public bool enabled;
        public Material[] sharedMaterials;
        public Material sharedMaterial;
        public Rendering.ShadowCastingMode shadowCastingMode;
        public bool receiveShadows;
    }

    public class MeshRenderer : Renderer { }

    public struct Plane
    {
        public Vector3 normal;
        public float distance;
    }

    public static class GeometryUtility
    {
        public static Plane[] CalculateFrustumPlanes(Camera c) => Array.Empty<Plane>();
        public static bool TestPlanesAABB(Plane[] planes, Bounds bounds) => true;
    }

    public class Shader : Object
    {
        public static Shader Find(string name) => null;
        public static int PropertyToID(string name) => 0;
        public static void SetGlobalVector(int id, Vector4 v) { }
        public static void SetGlobalFloat(int id, float v) { }
    }

    public class Material : Object
    {
        public bool enableInstancing;
        public Color color;
        public Material(Shader s) { }
        public void SetColor(string id, Color c) { }
        public void SetColor(int id, Color c) { }
        public void SetFloat(string id, float v) { }
        public void SetFloat(int id, float v) { }
        public void SetVector(string id, Vector4 v) { }
        public void SetVector(int id, Vector4 v) { }
        public void SetBuffer(string id, ComputeBuffer buffer) { }
        public void SetTexture(string id, Texture t) { }
        public void SetTexture(int id, Texture t) { }
        public bool HasProperty(string id) => true;
        public void EnableKeyword(string keyword) { }
        public void DisableKeyword(string keyword) { }
    }

    public class MaterialPropertyBlock
    {
        public void SetColor(string name, Color value) { }
        public void SetColor(int nameID, Color value) { }
        public void SetVector(string name, Vector4 value) { }
        public void SetVector(int nameID, Vector4 value) { }
        public void SetVectorArray(string name, Vector4[] values) { }
        public void SetVectorArray(int nameID, Vector4[] values) { }
        public void SetVectorArray(string name, List<Vector4> values) { }
        public void SetVectorArray(int nameID, List<Vector4> values) { }
        public void SetFloat(string name, float value) { }
        public void SetFloat(int nameID, float value) { }
        public void Clear() { }
    }

    public static class Application
    {
        public static bool isPlaying => false;
        public static string streamingAssetsPath => "unity-migration/Assets/StreamingAssets";
        public static string dataPath => "unity-migration/Assets";
    }

    public enum KeyCode { W, S, A, D, UpArrow, DownArrow, LeftArrow, RightArrow, Space, E, Q, C, LeftShift, RightShift }

    public enum TextureFormat { RGBA32, ARGB32, RGB24 }
    public enum FilterMode { Point, Bilinear, Trilinear }
    public enum TextureWrapMode { Repeat, Clamp, Mirror }

    public class Texture : Object { }

    public class Texture2D : Texture
    {
        public FilterMode filterMode;
        public TextureWrapMode wrapMode;
        public Texture2D(int width, int height) { }
        public Texture2D(int width, int height, TextureFormat format, bool mipChain) { }
        public Texture2D(int width, int height, TextureFormat format, bool mipChain, bool linear) { }
        public void SetPixels32(Color32[] colors) { }
        public void Apply(bool updateMipmaps = true, bool makeNoLongerReadable = false) { }
    }

    public static class Input
    {
        public static bool GetKey(KeyCode k) => false;
        public static bool touchSupported => false;
    }

    public static class Debug
    {
        public static void Log(object o) { }
        public static void LogWarning(object o) { }
        public static void LogError(object o) { }
    }

    public class HeaderAttribute : Attribute { public HeaderAttribute(string s) { } }
    public class TooltipAttribute : Attribute { public TooltipAttribute(string s) { } }
    public class RangeAttribute : Attribute { public RangeAttribute(float a, float b) { } }
    public class MinAttribute : Attribute { public MinAttribute(float a) { } }
    public class SerializeField : Attribute { }
    public class ContextMenuAttribute : Attribute { public ContextMenuAttribute(string s) { } }
    public class DisallowMultipleComponent : Attribute { }
    public class DefaultExecutionOrderAttribute : Attribute { public DefaultExecutionOrderAttribute(int order) { } }
}

namespace UnityEngine.Rendering
{
    public enum IndexFormat { UInt16, UInt32 }
    public enum ShadowCastingMode { Off, On, TwoSided, ShadowsOnly }
}

namespace UnityEngine.SceneManagement
{
    public struct Scene { }
}

namespace UnityEditor
{
    public class MenuItem : Attribute
    {
        public MenuItem(string path) { }
        public MenuItem(string path, bool validate) { }
        public MenuItem(string path, bool validate, int priority) { }
        public int priority;
    }

    public static class EditorUtility { public static void SetDirty(UnityEngine.Object o) { } }

    public static class AssetDatabase
    {
        public static T LoadAssetAtPath<T>(string path) where T : UnityEngine.Object => null;
        public static void CreateAsset(UnityEngine.Object o, string path) { }
        public static void SaveAssets() { }
        public static void Refresh() { }
    }

    public static class Selection { public static UnityEngine.Object activeObject; }

    public static class Undo { public static void RegisterCreatedObjectUndo(UnityEngine.Object o, string name) { } }
}

namespace UnityEditor.SceneManagement
{
    public static class EditorSceneManager
    {
        public static UnityEngine.SceneManagement.Scene GetActiveScene() => default;
        public static void MarkSceneDirty(UnityEngine.SceneManagement.Scene s) { }
    }
}

namespace UnityEngine.Networking
{
    public class UnityWebRequest : IDisposable
    {
        public enum Result { Success, Failure }
        public Result result;
        public string error;
        public DownloadHandler downloadHandler;
        public static UnityWebRequest Get(string url) => null;
        public object SendWebRequest() => null;
        public void Dispose() { }
    }
    public class DownloadHandler { public string text; }
}

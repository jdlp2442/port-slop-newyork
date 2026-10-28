using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace GunnellSmoke
{
    // Persistent RTHandles, never persistent RenderGraph TextureHandles.
    // Each camera owns its shadow-dependent lighting history independently.
    internal sealed class GunnellSmokeLightingCache : System.IDisposable
    {
        internal sealed class Entry
        {
            internal Camera camera;
            internal GunnellSmokeVolume volume;
            internal RTHandle sun, local;
            internal Vector3Int sunSize, localSize;
            internal Vector4 sunLayout,localLayout,halfTexel;
            internal Vector3 cameraPosition;
            internal Quaternion cameraRotation;
            internal int inputHash;
            internal bool hasWake;
            internal bool playing;
            internal float nextUpdate, usedAt;
            internal void Release(){sun?.Release();sun=local=null;}
        }
        readonly Dictionary<(int,int),Entry> entries=new Dictionary<(int,int),Entry>();
        readonly List<(int,int)> obsolete=new List<(int,int)>();
        readonly List<RTHandle> retired=new List<RTHandle>();
        float nextTrim;
        static RTHandle Allocate(string name,Vector3Int size)
        {
            var texture=new RenderTexture(size.x,size.y,0,RenderTextureFormat.ARGBHalf)
            {name=name,dimension=TextureDimension.Tex3D,volumeDepth=size.z,enableRandomWrite=true,
                filterMode=FilterMode.Bilinear,wrapMode=TextureWrapMode.Clamp,hideFlags=HideFlags.HideAndDontSave};
            texture.Create();return RTHandles.Alloc(texture,transferOwnership:true);
        }
        internal Entry Acquire(Camera camera,GunnellSmokeVolume volume,Vector3Int sunSize,Vector3Int localSize)
        {
            var key=(camera.GetEntityId().GetHashCode(),volume.GetEntityId().GetHashCode());
            if(!entries.TryGetValue(key,out var entry))
            {entry=new Entry{camera=camera,volume=volume};entries.Add(key,entry);}
            if(entry.sun==null||entry.sunSize!=sunSize||entry.localSize!=localSize)
            {
                // Another graph recorded in this context can still import the
                // old texture. Retire it at the start of the next context.
                if(entry.sun!=null)retired.Add(entry.sun);
                // Sun and local transport occupy disjoint regions in ONE
                // texture. The view kernel needs one lighting SRV per cloud.
                var atlasSize=new Vector3Int(Mathf.Max(sunSize.x,localSize.x),
                    Mathf.Max(sunSize.y,localSize.y),sunSize.z+localSize.z);
                entry.sun=Allocate("Smoke cached lighting atlas",atlasSize);entry.local=entry.sun;
                entry.sunLayout=new Vector4((float)sunSize.x/atlasSize.x,(float)sunSize.y/atlasSize.y,
                    (float)sunSize.z/atlasSize.z,0);
                entry.localLayout=new Vector4((float)localSize.x/atlasSize.x,(float)localSize.y/atlasSize.y,
                    (float)localSize.z/atlasSize.z,(float)sunSize.z/atlasSize.z);
                entry.halfTexel=new Vector4(.5f/atlasSize.x,.5f/atlasSize.y,.5f/atlasSize.z,0);
                entry.sunSize=sunSize;entry.localSize=localSize;entry.nextUpdate=-1;
            }
            entry.usedAt=Time.realtimeSinceStartup;return entry;
        }
        internal static bool NeedsUpdate(Entry entry,int hash,bool enabled,float hz,bool allowRefresh)
        {
            var camera=entry.camera;bool wake=entry.volume.WakeDensityHandle!=null;
            bool invalid=!enabled||entry.playing!=Application.isPlaying||entry.nextUpdate<0||Time.timeSinceLevelLoad>=entry.nextUpdate||entry.inputHash!=hash||entry.hasWake!=wake
                ||(camera.transform.position-entry.cameraPosition).sqrMagnitude>.0225f
                ||Quaternion.Angle(camera.transform.rotation,entry.cameraRotation)>5;
            // Uninitialized textures must never reach the view pass. Deferring
            // an ordinary refresh leaves the previous valid texture intact.
            bool update=invalid&&(!enabled||entry.nextUpdate<0||entry.playing!=Application.isPlaying||allowRefresh);
            if(update)
            {
                entry.nextUpdate=Time.timeSinceLevelLoad+1f/Mathf.Max(1,hz);entry.inputHash=hash;entry.hasWake=wake;
                entry.playing=Application.isPlaying;
                entry.cameraPosition=camera.transform.position;entry.cameraRotation=camera.transform.rotation;
            }
            return update;
        }
        // Call only before recording any camera graphs; old imports may still
        // be referenced by another pass while a frame is being recorded.
        internal void Trim()
        {
            foreach(var handle in retired)handle.Release();retired.Clear();
            if(Time.realtimeSinceStartup<nextTrim)return;nextTrim=Time.realtimeSinceStartup+1;
            obsolete.Clear();
            foreach(var pair in entries)
                if(!pair.Value.camera||!pair.Value.volume||Time.realtimeSinceStartup-pair.Value.usedAt>3)obsolete.Add(pair.Key);
            foreach(var key in obsolete){entries[key].Release();entries.Remove(key);}
        }
        public void Dispose()
        {
            foreach(var entry in entries.Values)entry.Release();entries.Clear();
            foreach(var handle in retired)handle.Release();retired.Clear();
            nextTrim=0;
        }
    }
}

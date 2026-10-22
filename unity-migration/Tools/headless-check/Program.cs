using System;
using System.Collections.Generic;
using UnityEngine;
using Nyc.Data;
using Nyc.Core;
using Nyc.Generation;
using Nyc.Gameplay;
using Nyc.Gameplay.Traffic;

public static class Program
{
    public static int Main(string[] args)
    {
        string path = args.Length > 0 ? args[0] : null;
        if (string.IsNullOrEmpty(path) || !System.IO.File.Exists(path))
        {
            string[] candidates = new[]
            {
                @"D:/escena/slop-newyork-main/js/nyc-geodata.js",
                @"D:/escena/slop-newyork-main/slop-newyork-main/js/nyc-geodata.js",
                @"unity-migration/Assets/StreamingAssets/nyc-geodata.json",
                @"Assets/StreamingAssets/nyc-geodata.json"
            };
            foreach (var cand in candidates)
            {
                if (System.IO.File.Exists(cand)) { path = cand; break; }
            }
        }

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var geo = NycGeoData.LoadFromFile(path);
        Console.WriteLine($"parse: {sw.ElapsedMilliseconds} ms");
        Console.WriteLine($"streets {geo.Streets.Count}, avenues {geo.Avenues.Count}, blocks {geo.Blocks.Count}, " +
                          $"roads {geo.Roads.Count}, landmarks {geo.Landmarks.Count}, parks {geo.Parks.Count}");

        CityConfig.Initialize(geo);
        Console.WriteLine($"district 5th/42: {CityConfig.DistrictAt(0, 0)}");
        Console.WriteLine($"district Harlem: {CityConfig.DistrictAt(-600, -5800)}");
        Console.WriteLine($"district FiDi:   {CityConfig.DistrictAt(200, 5200)}");
        Console.WriteLine($"streetZ(42)={CityConfig.StreetZ(42):F1} streetZ(59)={CityConfig.StreetZ(59):F1}");
        Console.WriteLine($"avenueX(5)={CityConfig.AvenueX("5"):F1} avenueX(8)={CityConfig.AvenueX("8"):F1}");
        CityConfig.IslandBoundsAtZ(0, out float w, out float e);
        Console.WriteLine($"shore at z=0: {w:F0} .. {e:F0}");

        var go = new GameObject("builder");
        var b = go.AddComponent<ManhattanMeshBuilder>();
        b.buildProps = true;

        sw.Restart();
        b.Build();
        Console.WriteLine($"build: {sw.ElapsedMilliseconds} ms");

        // ---- validation -------------------------------------------------------
        int meshes = 0, verts = 0, nan = 0, degen = 0, badUv = 0;
        float minY = float.MaxValue, maxY = float.MinValue;
        float minX = float.MaxValue, maxX = float.MinValue, minZ = float.MaxValue, maxZ = float.MinValue;

        foreach (var m in Capture.Meshes)
        {
            meshes++;
            verts += m.verts.Count;
            for (int i = 0; i < m.verts.Count; i++)
            {
                var p = m.verts[i];
                if (float.IsNaN(p.x) || float.IsNaN(p.y) || float.IsNaN(p.z)) nan++;
                if (p.y < minY) minY = p.y;
                if (p.y > maxY) maxY = p.y;
                if (p.x < minX) minX = p.x;
                if (p.x > maxX) maxX = p.x;
                if (p.z < minZ) minZ = p.z;
                if (p.z > maxZ) maxZ = p.z;
            }
            foreach (var t in m.tris)
            {
                if (t == null) continue;
                for (int i = 0; i + 2 < t.Count; i += 3)
                {
                    var a = m.verts[t[i]]; var bb = m.verts[t[i + 1]]; var c = m.verts[t[i + 2]];
                    var cr = Vector3.Cross(bb - a, c - a);
                    if (cr.x * cr.x + cr.y * cr.y + cr.z * cr.z < 1e-10f) degen++;
                }
            }
            if (m.uvs[1].Count != m.verts.Count || m.uvs[2].Count != m.verts.Count || m.uvs[3].Count != m.verts.Count)
                badUv++;
        }

        Console.WriteLine($"meshes {meshes}, verts {verts:N0}, chunks {b.chunkCount}, buildings {b.buildingCount}, boxes {b.boxCount}");
        Console.WriteLine($"bounds X [{minX:F0},{maxX:F0}] Y [{minY:F2},{maxY:F1}] Z [{minZ:F0},{maxZ:F0}]");
        Console.WriteLine($"NaN verts {nan}, degenerate tris {degen}, meshes with bad uv sets {badUv}");

        // ---- road markings ----------------------------------------------------
        var markGo = new GameObject("markings");
        markGo.AddComponent<MeshFilter>();
        markGo.AddComponent<MeshRenderer>();
        var markings = markGo.AddComponent<RoadMarkingBuilder>();
        sw.Restart();
        markings.Build();
        Console.WriteLine($"markings: {markings.stripCount} strips, {markings.vertexCount:N0} verts, {sw.ElapsedMilliseconds} ms");

        // ---- Phase 4: controllers --------------------------------------------
        var colGo = new GameObject("collision");
        var world = colGo.AddComponent<CollisionWorld>();
        world.Build(b.CollisionBoxes);
        Console.WriteLine($"collision: {world.Count:N0} boxes");

        var probe = b.CollisionBoxes[0];
        bool insideBox = world.Overlaps(
            new Vector2((probe.MinX + probe.MaxX) * 0.5f, (probe.MinZ + probe.MaxZ) * 0.5f), 0.4f, 0.4f, 1f);
        Console.WriteLine($"collider sanity (inside a building): {insideBox}");

        var mgrGo = new GameObject("manager");
        var mgr = mgrGo.AddComponent<ThirdPersonControllerManager>();
        mgr.collision = world;

        var ctx = new ControllerContext
        {
            Collision = world,
            Manager = mgr,
            Ground = (x, z) => 0f,
            Elapsed = 0f
        };

        var car = new GameObject("car").AddComponent<CarController>();
        car.Position = new Vector2(3.6f, CityConfig.StreetZ(37) + 12f);
        var drive = ControllerInput.FromBooleans(true, false, false, false, false, false, false);
        for (int i = 0; i < 600; i++) { ctx.Elapsed += 1f / 60f; car.Tick(1f / 60f, drive, ctx); }
        bool carInside = world.Overlaps(car.Position, 0.86f, 2.18f, 1f);
        Console.WriteLine($"car 10s: pos ({car.Position.x:F1},{car.Position.y:F1}) speed {car.Speed:F2} inside={carInside}");

        var ped = new GameObject("ped").AddComponent<PedestrianController>();
        ped.Position = new Vector2(-11.6f, CityConfig.StreetZ(37) + 12f);
        for (int i = 0; i < 300; i++) { ctx.Elapsed += 1f / 60f; ped.Tick(1f / 60f, drive, ctx); }
        Console.WriteLine($"ped 5s: pos ({ped.Position.x:F1},{ped.Position.y:F1}) speed {ped.Speed:F2} y {ped.Height:F2}");

        var heli = new GameObject("heli").AddComponent<HelicopterController>();
        heli.Position = new Vector2(-110f, CityConfig.StreetZ(37) - 60f);
        heli.Height = 96f;
        var climb = ControllerInput.FromBooleans(true, false, false, false, true, false, false);
        for (int i = 0; i < 300; i++) { ctx.Elapsed += 1f / 60f; heli.Tick(1f / 60f, climb, ctx); }
        Console.WriteLine($"heli 5s: pos ({heli.Position.x:F1},{heli.Position.y:F1}) alt {heli.Height:F1} speed {heli.Speed:F2}");

        var tp = new GameObject("teleport").AddComponent<TeleportService>();
        tp.collision = world;
        tp.manager = mgr;
        bool ok = tp.TryFindSafe(new Vector2(0f, 0f), 0f, TravelMode.Car, car, out Vector2 safe);
        Console.WriteLine($"teleport to 5th/42: {ok} -> ({safe.x:F1},{safe.y:F1})");

        // ---- Phase 5: lane network + traffic ---------------------------------
        var netGo = new GameObject("network");
        var net = netGo.AddComponent<RoadNetwork>();
        sw.Restart();
        net.Build(geo);
        Console.WriteLine($"lanes {net.laneCount:N0}, connections {net.connectionCount:N0}, roads {net.roadCount:N0} in {sw.ElapsedMilliseconds} ms");

        var trafficGo = new GameObject("traffic");
        var traffic = trafficGo.AddComponent<TrafficSystem>();
        traffic.network = net;
        sw.Restart();
        traffic.Build();
        Console.WriteLine($"traffic spawn: {traffic.activeCount} agents in {sw.ElapsedMilliseconds} ms");

        sw.Restart();
        for (int i = 0; i < 1800; i++) traffic.Tick(1f / 60f);
        Console.WriteLine($"traffic 30 s sim: {sw.ElapsedMilliseconds} ms total");

        int nanAgents = 0, stuck = 0;
        float sumSpeed = 0f, maxSpeed = 0f;
        int active = 0;
        foreach (var a in traffic.Agents)
        {
            if (!a.Active) continue;
            active++;
            if (float.IsNaN(a.Position.x) || float.IsNaN(a.Position.y) || float.IsNaN(a.Speed)) nanAgents++;
            if (a.Speed < 0.05f) stuck++;
            sumSpeed += a.Speed;
            if (a.Speed > maxSpeed) maxSpeed = a.Speed;
        }
        Console.WriteLine($"traffic stats: active {active}, nan {nanAgents}, stopped {stuck}, " +
                          $"avg {sumSpeed / Mathf.Max(1, active):F2} m/s, max {maxSpeed:F2} m/s");

        var crowd = new GameObject("crowd").AddComponent<Nyc.Gameplay.Pedestrians.PedestrianCrowd>();
        sw.Restart();
        crowd.RebuildNow();
        Console.WriteLine($"pedestrians: {crowd.walkerCount} walkers over {crowd.blockCount} blocks in {sw.ElapsedMilliseconds} ms");

        // ---- Phase 6: map atlas -------------------------------------------------
        sw.Restart();
        var px = Nyc.UI.NycMapBaker.Rasterize(geo, 512, out Rect mapRect);
        int land = 0, parks = 0, roads = 0;
        for (int i = 0; i < px.Length; i++)
        {
            Color32 c = px[i];
            if (c.r == 10 && c.g == 17 && c.b == 24) continue;
            land++;
            if (c.r == 24 && c.g == 52 && c.b == 36) parks++;
            if (c.r == 78 || c.r == 104 || c.r == 150) roads++;
        }
        Console.WriteLine($"map atlas 512px: {mapRect.width:F0}x{mapRect.height:F0} m, land {land * 100f / px.Length:F1}%, " +
                          $"parks {parks * 100f / px.Length:F1}%, roads {roads * 100f / px.Length:F2}%, {sw.ElapsedMilliseconds} ms");

        var fullMap = Nyc.UI.NycMapBaker.Bake(geo, 1024);
        Vector2 back = fullMap.PixelToWorld(fullMap.WorldToPixel(0f, 0f));
        Console.WriteLine($"map projection round-trip at 5th/42: ({back.x:F2},{back.y:F2})");

        return nan == 0 && badUv == 0 && verts > 100000 ? 0 : 1;
    }
}

/// <summary>Captures every Mesh built through MeshFilter so the harness can inspect it.</summary>
public static class Capture
{
    public static readonly List<Mesh> Meshes = new List<Mesh>();
}

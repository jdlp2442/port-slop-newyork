using System;
using System.Collections.Generic;
using UnityEngine;
using Nyc.Data;

namespace Nyc.Core
{
    /// <summary>
    /// Port of the spatial part of js/city-config.js: the street/avenue database, the
    /// neighbourhood classifier, the shoreline profile, Broadway's centreline and the
    /// teleport arrivals. Pure static data, no MonoBehaviour, safe to call from jobs.
    /// </summary>
    public static class CityConfig
    {
        public struct Teleport
        {
            public string Id, Name, Subtitle;
            public float X, Z, HeadingRad;
        }

        // Midtown core avenues used by the signal / traffic code.
        public const float HoustonZ  = 3376f; // at Broadway
        public const float CanalZ    = 4169f;
        public const float ChambersZ = 4859f;

        public static bool Ready { get; private set; }
        public static NycGeoData Geo { get; private set; }

        static readonly Dictionary<int, StreetRec> StreetByNumber = new Dictionary<int, StreetRec>(160);
        static readonly Dictionary<string, AvenueRec> AvenueById  = new Dictionary<string, AvenueRec>(32);
        static Vector4 centralPark;                 // minX, maxX, minZ, maxZ
        static Vector4 manhattanBounds;              // minX, maxX, minZ, maxZ

        static Vector2[] broadwayLine;
        static float[] shoreWest, shoreEast;
        static float shoreZ0, shoreZ1;
        const float ShoreStep = 2f;

        public static readonly Dictionary<string, Teleport> Teleports = new Dictionary<string, Teleport>();

        public static void Initialize(NycGeoData geo)
        {
            Geo = geo ?? throw new ArgumentNullException(nameof(geo));
            StreetByNumber.Clear();
            AvenueById.Clear();

            foreach (var s in geo.Streets) StreetByNumber[s.N] = s;
            foreach (var a in geo.Avenues) AvenueById[a.Id] = a;

            centralPark = geo.CentralPark?.Bounds ?? new Vector4(-845f, -13f, -5491f, -1397f);
            manhattanBounds = ComputeBounds(geo.Manhattan);
            broadwayLine = geo.Broadway ?? Array.Empty<Vector2>();

            BuildShoreProfile(geo.Manhattan);
            BuildTeleports(geo);
            Ready = true;
        }

        // ------------------------------------------------------------------ streets & avenues

        public static float StreetZ(int n)
        {
            if (StreetByNumber.TryGetValue(n, out var s)) return s.Z;

            int lo = n - 1, hi = n + 1;
            while (lo > 0 && !StreetByNumber.ContainsKey(lo)) lo--;
            while (hi < 300 && !StreetByNumber.ContainsKey(hi)) hi++;

            if (StreetByNumber.TryGetValue(lo, out var a) && StreetByNumber.TryGetValue(hi, out var b))
                return a.Z + ((n - lo) / (float)(hi - lo)) * (b.Z - a.Z);

            return (42 - n) * 79.5f;
        }

        public static float AvenueX(string id)
        {
            if (AvenueById.TryGetValue(id, out var a)) return a.X;
            // Shore highways are authored, not in GEO.avenues (see city-config.js ALL_AVENUES).
            return id switch
            {
                "westside" => -2000f,
                "fdr"      => 1500f,
                _          => 0f
            };
        }

        public static bool TryAvenue(string id, out AvenueRec avenue) => AvenueById.TryGetValue(id, out avenue);

        public static Vector4 CentralParkBounds => centralPark;
        public static Vector4 ManhattanBounds => manhattanBounds;

        static Vector4 ComputeBounds(Vector2[] ring)
        {
            if (ring == null || ring.Length == 0) return new Vector4(-2117f, 2569f, -7090f, 6512f);
            float minX = float.MaxValue, maxX = float.MinValue;
            float minZ = float.MaxValue, maxZ = float.MinValue;
            for (int i = 0; i < ring.Length; i++)
            {
                minX = Mathf.Min(minX, ring[i].x);
                maxX = Mathf.Max(maxX, ring[i].x);
                minZ = Mathf.Min(minZ, ring[i].y);
                maxZ = Mathf.Max(maxZ, ring[i].y);
            }
            return new Vector4(minX, maxX, minZ, maxZ);
        }

        // ------------------------------------------------------------------ Broadway

        public static float BroadwayX(float z)
        {
            var L = broadwayLine;
            if (L == null || L.Length == 0) return 0f;
            if (z <= L[0].y) return L[0].x;
            if (z >= L[L.Length - 1].y) return L[L.Length - 1].x;

            int lo = 0, hi = L.Length - 1;
            while (hi - lo > 1)
            {
                int mid = (lo + hi) >> 1;
                if (L[mid].y <= z) lo = mid; else hi = mid;
            }
            var a = L[lo];
            var b = L[hi];
            return a.x + ((z - a.y) / Mathf.Max(1e-6f, b.y - a.y)) * (b.x - a.x);
        }

        // ------------------------------------------------------------------ shoreline

        static void BuildShoreProfile(Vector2[] ring)
        {
            if (ring == null || ring.Length < 3) { shoreWest = shoreEast = Array.Empty<float>(); return; }

            float z0 = float.MaxValue, z1 = float.MinValue;
            for (int i = 0; i < ring.Length; i++)
            {
                z0 = Mathf.Min(z0, ring[i].y);
                z1 = Mathf.Max(z1, ring[i].y);
            }
            shoreZ0 = Mathf.Ceil(z0 / ShoreStep) * ShoreStep;
            shoreZ1 = z1;
            int n = Mathf.FloorToInt((z1 - shoreZ0) / ShoreStep) + 1;
            shoreWest = new float[n];
            shoreEast = new float[n];
            for (int i = 0; i < n; i++) { shoreWest[i] = float.NaN; shoreEast[i] = float.NaN; }

            for (int i = 0, j = ring.Length - 1; i < ring.Length; j = i++)
            {
                Vector2 a = ring[j], b = ring[i];
                if (Mathf.Approximately(a.y, b.y)) continue;
                float lo = Mathf.Min(a.y, b.y), hi = Mathf.Max(a.y, b.y);
                int k0 = Mathf.Max(0, Mathf.CeilToInt((lo - shoreZ0) / ShoreStep));
                for (int k = k0; k < n && shoreZ0 + k * ShoreStep < hi; k++)
                {
                    float z = shoreZ0 + k * ShoreStep + 1e-3f;
                    if (z < lo || z >= hi) continue;
                    float x = a.x + ((z - a.y) * (b.x - a.x)) / (b.y - a.y);
                    if (!(shoreWest[k] <= x)) shoreWest[k] = x;
                    if (!(shoreEast[k] >= x)) shoreEast[k] = x;
                }
            }
            for (int k = 1; k < n; k++)
                if (float.IsNaN(shoreWest[k])) { shoreWest[k] = shoreWest[k - 1]; shoreEast[k] = shoreEast[k - 1]; }
            for (int k = n - 2; k >= 0; k--)
                if (float.IsNaN(shoreWest[k])) { shoreWest[k] = shoreWest[k + 1]; shoreEast[k] = shoreEast[k + 1]; }
        }

        /// <summary>West / east bulkhead x of Manhattan at a given z.</summary>
        public static void IslandBoundsAtZ(float z, out float westX, out float eastX)
        {
            if (shoreWest == null || shoreWest.Length < 2) { westX = -2000f; eastX = 2000f; return; }
            float f = (z - shoreZ0) / ShoreStep;
            int k = Mathf.Clamp(Mathf.FloorToInt(f), 0, shoreWest.Length - 2);
            float t = Mathf.Clamp01(f - k);
            westX = Mathf.Lerp(shoreWest[k], shoreWest[k + 1], t);
            eastX = Mathf.Lerp(shoreEast[k], shoreEast[k + 1], t);
        }

        public static bool IsWater(float x, float z)
        {
            IslandBoundsAtZ(z, out float w, out float e);
            return x < w || x > e;
        }

        // ------------------------------------------------------------------ districts

        /// <summary>Port of districtAt() from city-config.js.</summary>
        public static string DistrictAt(float x, float z)
        {
            if (x > centralPark.x && x < centralPark.y && z > centralPark.z && z < centralPark.w)
                return "central_park";

            if (z < StreetZ(110))
            {
                if (x < AvenueX("manhattan") - 30f) return "morningside";
                if (x > AvenueX("5") + 10f) return "east_harlem";
                return "harlem";
            }
            if (z < StreetZ(96) && x > AvenueX("5") + 10f) return "east_harlem";
            if (z < StreetZ(59)) return x < centralPark.x ? "uws" : "ues";

            if (z < StreetZ(34))
            {
                if (x < AvenueX("8") - 20f)
                    return z > StreetZ(42) && x < AvenueX("10") - 20f ? "hudson_yards" : "hells_kitchen";
                if (x > AvenueX("3") + 20f) return "turtle_bay";
                if (z < StreetZ(56) && x < AvenueX("lex")) return "billionaires_row";
                if (z > StreetZ(42) && x < AvenueX("6")) return "garment";
                if (z > StreetZ(38) && x > AvenueX("madison")) return "murray_hill";
                return "midtown";
            }
            if (z < StreetZ(14))
            {
                if (x < AvenueX("10") - 20f) return z < StreetZ(28) ? "hudson_yards" : "west_chelsea";
                if (x < AvenueX("6")) return "chelsea";
                if (x < AvenueX("park") + 20f) return z < StreetZ(26) ? "nomad" : "flatiron";
                if (x > AvenueX("1") + 20f) return "stuy_town";
                return z < StreetZ(23) ? "murray_hill" : "gramercy";
            }
            if (z < HoustonZ)
            {
                if (x > AvenueX("3") - 10f) return "east_village";
                if (x < AvenueX("7") - 300f) return "west_village";
                if (x < AvenueX("5") - 120f) return "west_village";
                return "village";
            }
            if (z < CanalZ)
            {
                if (x > 760f) return "lower_east_side";
                if (x > 380f) return "little_italy";
                if (x < -420f) return "hudson_square";
                return "soho";
            }
            if (x < -380f && z > ChambersZ - 200f) return "battery_park_city";
            if (z < ChambersZ)
            {
                if (x > 900f) return "lower_east_side";
                if (x > 300f) return "chinatown";
                return "tribeca";
            }
            if (z < 5250f && x > 250f && x < 900f) return "civic_center";
            return "fidi";
        }

        // ------------------------------------------------------------------ traffic signals

        public struct SignalTiming
        {
            public float Cycle, AvenueAmber, AvenueRed, StreetAmber, Walk;
        }

        public static readonly SignalTiming Signals = new SignalTiming
        {
            Cycle = 60f, AvenueAmber = 26f, AvenueRed = 30f, StreetAmber = 56f, Walk = 7f
        };

        /// <summary>0 = green, 1 = amber, 2 = red.</summary>
        public static int SignalPhase(float time, bool isAvenue)
        {
            float c = Signals.Cycle;
            float t = ((time % c) + c) % c;
            if (isAvenue) return t < Signals.AvenueAmber ? 0 : t < Signals.AvenueRed ? 1 : 2;
            return t < Signals.AvenueRed ? 2 : t < Signals.StreetAmber ? 0 : 1;
        }

        public static bool WalkSignal(float time, bool isAvenue)
        {
            float c = Signals.Cycle;
            float t = (((time - (isAvenue ? 0f : Signals.AvenueRed)) % c) + c) % c;
            return t < Signals.Walk;
        }

        // ------------------------------------------------------------------ teleports

        static void BuildTeleports(NycGeoData geo)
        {
            Teleports.Clear();
            // Values copied verbatim from TELEPORT_LOCATIONS (city-config.js).
            Add("empire",      "Empire State Building",       "5th Ave · Midtown",              AvenueX("5") + 3.6f,        StreetZ(36), 0f);
            Add("timessq",     "Times Square",                "7th Ave · Theater District",     AvenueX("7") + 3.6f,        StreetZ(46), 0f);
            Add("chrysler",    "Chrysler Building",           "Lexington Ave · Midtown East",   AvenueX("lex") + 3.6f,      StreetZ(40), Mathf.PI);
            Add("grandcentral","Grand Central Terminal",      "Park Ave · Midtown East",        AvenueX("park") - 8.0f,     StreetZ(40), Mathf.PI);
            Add("bryant",      "Bryant Park",                 "6th Ave · Midtown",              AvenueX("6") + 3.6f,        StreetZ(41), Mathf.PI * 0.5f);
            Add("flatiron",    "Flatiron Building",           "5th Ave · Flatiron District",    AvenueX("5") + 3.6f,        StreetZ(25), 0f);
            Add("centralpark", "Central Park",                "6th Ave · Central Park South",   AvenueX("6") + 3.6f,        StreetZ(57), Mathf.PI);

            float owtcX = -305.7f, owtcZ = 5210f;
            float heading = Mathf.PI;
            if (geo.Landmarks.TryGetValue("One World Trade Center", out var owtc))
                heading = Mathf.Atan2(owtc.X - owtcX, owtc.Z - owtcZ);
            Add("owtc", "One World Trade Center", "Vesey St · Financial District", owtcX, owtcZ, heading);
        }

        static void Add(string id, string name, string sub, float x, float z, float headingRad)
        {
            Teleports[id] = new Teleport { Id = id, Name = name, Subtitle = sub, X = x, Z = z, HeadingRad = headingRad };
        }
    }
}

using UnityEngine;
using Nyc.Data;

namespace Nyc.UI
{
    /// <summary>
    /// "5th Ave &amp; 42nd St" for any world point. Lightweight port of getLocationLabel()
    /// (city-config.js): landmarks within 80 m, Central Park, otherwise the nearest avenue and
    /// the nearest numbered street.
    /// </summary>
    public static class LocationLabel
    {
        public static string Get(NycGeoData geo, float x, float z)
        {
            if (geo == null) return "Manhattan";

            // Landmark proximity
            foreach (var kv in geo.Landmarks)
            {
                LandmarkRec l = kv.Value;
                float dx = x - l.X, dz = z - l.Z;
                if (dx * dx + dz * dz < 80f * 80f) return kv.Key;
            }

            if (geo.CentralPark != null)
            {
                Vector4 cp = geo.CentralPark.Bounds;
                if (x > cp.x && x < cp.y && z > cp.z && z < cp.w)
                {
                    StreetRec near = NearestStreet(geo, x, z);
                    return near != null ? $"Central Park · near {Ordinal(near.N)} St" : "Central Park";
                }
            }

            StreetRec street = NearestStreet(geo, x, z);
            AvenueRec avenue = NearestAvenue(geo, x, z);

            if (avenue != null && street != null)
                return $"{ShortAvenue(avenue)} & {Ordinal(street.N)} St";
            if (avenue != null) return ShortAvenue(avenue);
            if (street != null) return $"{Ordinal(street.N)} St";
            return "New York Harbor";
        }

        static StreetRec NearestStreet(NycGeoData geo, float x, float z)
        {
            StreetRec best = null;
            float bestDz = 90f;

            for (int i = 0; i < geo.Streets.Count; i++)
            {
                StreetRec s = geo.Streets[i];
                float dz = Mathf.Abs(z - s.Z);
                if (dz > bestDz) continue;

                // Must actually reach this x.
                bool covers = false;
                for (int k = 0; k < s.Segs.Count; k++)
                {
                    if (x >= s.Segs[k].X0 - 30f && x <= s.Segs[k].X1 + 30f) { covers = true; break; }
                }
                if (!covers) continue;

                bestDz = dz;
                best = s;
            }
            return best;
        }

        static AvenueRec NearestAvenue(NycGeoData geo, float x, float z)
        {
            AvenueRec best = null;
            float bestDx = 90f;

            for (int i = 0; i < geo.Avenues.Count; i++)
            {
                AvenueRec a = geo.Avenues[i];
                float dx = Mathf.Abs(x - a.X);
                if (dx > bestDx) continue;

                bool covers = false;
                for (int k = 0; k < a.Segs.Count; k++)
                {
                    if (z >= a.Segs[k].Z0 - 40f && z <= a.Segs[k].Z1 + 40f) { covers = true; break; }
                }
                if (!covers) continue;

                bestDx = dx;
                best = a;
            }
            return best;
        }

        static string ShortAvenue(AvenueRec a)
        {
            string label = a.Label ?? a.Id;
            int slash = label.IndexOf(" / ", System.StringComparison.Ordinal);
            return slash > 0 ? label.Substring(0, slash) : label;
        }

        public static string Ordinal(int n)
        {
            int mod100 = n % 100;
            if (mod100 >= 11 && mod100 <= 13) return n + "th";
            switch (n % 10)
            {
                case 1: return n + "st";
                case 2: return n + "nd";
                case 3: return n + "rd";
                default: return n + "th";
            }
        }
    }
}

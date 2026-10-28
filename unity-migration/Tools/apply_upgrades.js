const fs = require('fs');

// --- 1. ManhattanMeshBuilder.cs ---
let mb = fs.readFileSync('unity-migration/Assets/Scripts/Generation/ManhattanMeshBuilder.cs', 'utf8');

const mastTarget = `                // Crossbar tines
                EmitSolidBox(f, new RectXZ(ax - 0.75f, ax + 0.75f, az - 0.06f, az + 0.06f), y + mastH * 0.72f, y + mastH * 0.72f + 0.12f,
                             new Color32(64, 70, 78, 255));
                EmitSolidBox(f, new RectXZ(ax - 0.5f, ax + 0.5f, az - 0.06f, az + 0.06f), y + mastH * 0.88f, y + mastH * 0.88f + 0.12f,
                             new Color32(64, 70, 78, 255));
            }
        }`;

const mastReplacement = `                // Crossbar tines
                EmitSolidBox(f, new RectXZ(ax - 0.75f, ax + 0.75f, az - 0.06f, az + 0.06f), y + mastH * 0.72f, y + mastH * 0.72f + 0.12f,
                             new Color32(64, 70, 78, 255));
                EmitSolidBox(f, new RectXZ(ax - 0.5f, ax + 0.5f, az - 0.06f, az + 0.06f), y + mastH * 0.88f, y + mastH * 0.88f + 0.12f,
                             new Color32(64, 70, 78, 255));

                // FAA Red Aviation Hazard Warning Beacon on mast peak
                Color32 beaconRed = new Color32(255, 36, 36, 255);
                EmitSolidBox(f, new RectXZ(ax - 0.22f, ax + 0.22f, az - 0.22f, az + 0.22f), y + mastH, y + mastH + 0.55f, beaconRed);
            }

            // 9. FAA Obstacle hazard lights on corners of high-rise skyscrapers (>90m)
            if (y > 90f && r.Width > 14f && r.Depth > 14f)
            {
                Color32 beaconRed = new Color32(255, 36, 36, 255);
                float px0 = r.X0 + 0.40f, px1 = r.X1 - 0.40f;
                float pz0 = r.Z0 + 0.40f, pz1 = r.Z1 - 0.40f;
                float by0 = y + 0.95f;
                float bh = 0.55f;
                EmitSolidBox(f, new RectXZ(px0 - 0.16f, px0 + 0.16f, pz0 - 0.16f, pz0 + 0.16f), by0, by0 + bh, beaconRed);
                EmitSolidBox(f, new RectXZ(px1 - 0.16f, px1 + 0.16f, pz0 - 0.16f, pz0 + 0.16f), by0, by0 + bh, beaconRed);
                EmitSolidBox(f, new RectXZ(px0 - 0.16f, px0 + 0.16f, pz1 - 0.16f, pz1 + 0.16f), by0, by0 + bh, beaconRed);
                EmitSolidBox(f, new RectXZ(px1 - 0.16f, px1 + 0.16f, pz1 - 0.16f, pz1 + 0.16f), by0, by0 + bh, beaconRed);
            }
        }`;

if (!mb.includes(mastTarget)) {
    console.error('mastTarget not found in ManhattanMeshBuilder!');
    process.exit(1);
}
mb = mb.replace(mastTarget, mastReplacement);

const tankTarget = `            // Four steel legs (a single thin box each, cheap)
            for (int i = 0; i < 4; i++)
            {
                float a = i * Mathf.PI * 0.5f + Mathf.PI * 0.25f;
                float lx = cx + Mathf.Cos(a) * 1.15f;
                float lz = cz + Mathf.Sin(a) * 1.15f;
                EmitSolidBox(f, new RectXZ(lx - 0.12f, lx + 0.12f, lz - 0.12f, lz + 0.12f), y, y0,
                             new Color32(40, 44, 48, 255));
            }`;

const tankReplacement = `            // Four steel legs (a single thin box each, cheap)
            for (int i = 0; i < 4; i++)
            {
                float a = i * Mathf.PI * 0.5f + Mathf.PI * 0.25f;
                float lx = cx + Mathf.Cos(a) * 1.15f;
                float lz = cz + Mathf.Sin(a) * 1.15f;
                EmitSolidBox(f, new RectXZ(lx - 0.12f, lx + 0.12f, lz - 0.12f, lz + 0.12f), y, y0,
                             new Color32(40, 44, 48, 255));
            }

            // Steel access ladder on side of water tower
            float ladX = cx, ladZ = cz + rad + 0.08f;
            Color32 ladderColor = new Color32(45, 49, 54, 255);
            EmitSolidBox(f, new RectXZ(ladX - 0.22f, ladX - 0.16f, ladZ - 0.03f, ladZ + 0.03f), y, y1 + 0.4f, ladderColor);
            EmitSolidBox(f, new RectXZ(ladX + 0.16f, ladX + 0.22f, ladZ - 0.03f, ladZ + 0.03f), y, y1 + 0.4f, ladderColor);
            for (float ry = y + 0.5f; ry <= y1 + 0.3f; ry += 0.50f)
            {
                EmitSolidBox(f, new RectXZ(ladX - 0.18f, ladX + 0.18f, ladZ - 0.02f, ladZ + 0.02f), ry, ry + 0.04f, ladderColor);
            }`;

if (!mb.includes(tankTarget)) {
    console.error('tankTarget not found in ManhattanMeshBuilder!');
    process.exit(1);
}
mb = mb.replace(tankTarget, tankReplacement);
fs.writeFileSync('unity-migration/Assets/Scripts/Generation/ManhattanMeshBuilder.cs', mb, 'utf8');
console.log('ManhattanMeshBuilder updated successfully');

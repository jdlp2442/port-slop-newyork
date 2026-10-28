const fs = require('fs');

let code = fs.readFileSync('unity-migration/Assets/Scripts/Rendering/StreetDetailsSystem.cs', 'utf8');

// Replace the buggy lines:
const bug1 = `                    float aveW = ave.Width > 0f ? ave.Width : 30f;
                    string aName = ave.Name ?? "";`;
const fix1 = `                    float aveW = (ave.Segs != null && ave.Segs.Count > 0) ? ave.Segs[0].W : 30f;
                    string aName = ave.Label ?? ave.Id ?? "";`;

const bug2 = `                    float aveW = ave.Width > 0f ? ave.Width : 30f;
                    for (int sIdx = 1; sIdx < geo.Streets.Count - 1; sIdx += 2)`;
const fix2 = `                    float aveW = (ave.Segs != null && ave.Segs.Count > 0) ? ave.Segs[0].W : 30f;
                    for (int sIdx = 1; sIdx < geo.Streets.Count - 1; sIdx += 2)`;

code = code.replace(bug1, fix1);
code = code.replace(bug2, fix2);

fs.writeFileSync('unity-migration/Assets/Scripts/Rendering/StreetDetailsSystem.cs', code, 'utf8');
console.log('Fixed StreetDetailsSystem AvenueRec properties');

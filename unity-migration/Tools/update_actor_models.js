const fs = require('fs');

let code = fs.readFileSync('unity-migration/Assets/Scripts/Gameplay/ActorModelFactory.cs', 'utf8');

const targetHeli = `            // Fuselage root for attitude tilt
            var fuselageGo = new GameObject("FuselageRoot");
            fuselageGo.transform.SetParent(root.transform, false);
            vis.fuselageRoot = fuselageGo.transform;

            Material heliBodyMat = CreateMaterial("HeliBody", new Color(0.92f, 0.15f, 0.15f)); // NY sightseeing red`;

const replaceHeli = `            // Fuselage root for attitude tilt
            var fuselageGo = new GameObject("FuselageRoot");
            fuselageGo.transform.SetParent(root.transform, false);
            vis.fuselageRoot = fuselageGo.transform;

            // Check if high-fidelity 3D NYPD Helicopter prefab is available in Resources
            var heliPrefab = Resources.Load<GameObject>("Prefabs/Helicopter");
            if (heliPrefab != null)
            {
                var heliInstance = UnityEngine.Object.Instantiate(heliPrefab, fuselageGo.transform, false);
                heliInstance.name = "NYPD_Helicopter_PBR";
                // Rotate 180 deg around Y so +Z aligns with travel heading (-Z forward)
                heliInstance.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
                heliInstance.transform.localPosition = new Vector3(0f, 0f, 0f);

                // Wire up rotating rotors to HelicopterVisual for continuous high-RPM spin
                Transform mainRotorTrans = heliInstance.transform.Find("Main_Rotor");
                Transform tailRotorTrans = heliInstance.transform.Find("Tail_Rotor");
                if (mainRotorTrans) vis.mainRotor = mainRotorTrans;
                if (tailRotorTrans) vis.tailRotor = tailRotorTrans;
                return;
            }

            Material heliBodyMat = CreateMaterial("HeliBody", new Color(0.92f, 0.15f, 0.15f)); // NY sightseeing red`;

if (!code.includes(targetHeli)) {
    console.error('targetHeli not found in ActorModelFactory!');
    process.exit(1);
}

code = code.replace(targetHeli, replaceHeli);

fs.writeFileSync('unity-migration/Assets/Scripts/Gameplay/ActorModelFactory.cs', code, 'utf8');
console.log('ActorModelFactory updated successfully');

const fs = require('fs');

let stubs = fs.readFileSync('unity-migration/Tools/headless-check/UnityStubs.cs', 'utf8');

const target = `    public partial class Object
    {
        public static T Instantiate<T>(T original) where T : class => original;
    }`;

const replace = `    public partial class Object
    {
        public static T Instantiate<T>(T original) where T : class => original;
        public static T Instantiate<T>(T original, Transform parent, bool instantiateInWorldSpace = false) where T : class => original;
    }`;

if (!stubs.includes(target)) {
    console.error('Target not found in UnityStubs.cs');
    process.exit(1);
}

stubs = stubs.replace(target, replace);
fs.writeFileSync('unity-migration/Tools/headless-check/UnityStubs.cs', stubs, 'utf8');
console.log('Added Instantiate overload to UnityStubs.cs');

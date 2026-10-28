const fs = require('fs');
let s = fs.readFileSync('unity-migration/Assets/Prototype Collection/Assets/Editor/StartScreen.cs', 'utf8');
const target = `\t\tstatic StartScreen()
\t\t{
\t\t\tif ( !EditorPrefs.HasKey( Preferences.PrefStartUp ) )
\t\t\t{
\t\t\t\tEditorPrefs.SetBool( Preferences.PrefStartUp, true );
\t\t\t}

\t\t\tif ( EditorPrefs.GetBool( Preferences.PrefStartUp ) )
\t\t\t{
\t\t\t\tEditorApplication.delayCall += Init;
\t\t\t}
\t\t}`;

const rep = `\t\tstatic StartScreen()
\t\t{
\t\t\tEditorApplication.delayCall += () =>
\t\t\t{
\t\t\t\ttry
\t\t\t\t{
\t\t\t\t\tif ( !EditorPrefs.HasKey( Preferences.PrefStartUp ) )
\t\t\t\t\t{
\t\t\t\t\t\tEditorPrefs.SetBool( Preferences.PrefStartUp, false );
\t\t\t\t\t}
\t\t\t\t}
\t\t\t\tcatch {}
\t\t\t};
\t\t}`;

s = s.replace(/\r\n/g, '\n');
const normTarget = target.replace(/\r\n/g, '\n');
if (!s.includes(normTarget)) {
    console.error('Target not found in StartScreen.cs');
    process.exit(1);
}
s = s.replace(normTarget, rep.replace(/\r\n/g, '\n'));
fs.writeFileSync('unity-migration/Assets/Prototype Collection/Assets/Editor/StartScreen.cs', s, 'utf8');
console.log('Fixed StartScreen.cs static constructor');

using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace DungeonContract.Editor
{
    public static class DcBuild
    {
        public static void Windows()
        {
            var output = DcBootstrap.Argument("-dcBuildRoot");
            if (string.IsNullOrWhiteSpace(output)) throw new ArgumentException("-dcBuildRoot required");
            if(output.Contains("s1-v4")||output.Contains("s2-v4")||output.Contains("p2-1-v4")||output.Contains("p3-v4")||output.Contains("p4-v4")||output.Contains("p4-lan")||(output.Contains("v5")||output.Contains("v6")||(output.Contains("v7")||output.Contains("v8"))))PlayerSettings.enableFrameTimingStats=true;
            var graphics=new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/GraphicsSettings.asset")[0]);
            var included=graphics.FindProperty("m_AlwaysIncludedShaders");
            for(var index=included.arraySize-1;index>=0;index--)if(included.GetArrayElementAtIndex(index).objectReferenceValue is Shader previous&&previous.name=="GUI/Text Shader")included.DeleteArrayElementAtIndex(index);
            foreach(var shaderName in new[]{"UI/Default","Sprites/Default"})
            {
                var shader=Shader.Find(shaderName);if(shader==null)throw new InvalidOperationException("Required runtime shader missing: "+shaderName);
                var exists=false;for(var index=0;index<included.arraySize;index++)exists|=included.GetArrayElementAtIndex(index).objectReferenceValue==shader;
                if(!exists){var index=included.arraySize;included.InsertArrayElementAtIndex(index);included.GetArrayElementAtIndex(index).objectReferenceValue=shader;}
            }
            graphics.ApplyModifiedPropertiesWithoutUndo();AssetDatabase.SaveAssets();
            var scenePath = "Assets/DungeonContract/Scenes/Bootstrap.unity";
            Directory.CreateDirectory("Assets/DungeonContract/Scenes");
            if (!File.Exists(scenePath))
            {
                var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                if (!EditorSceneManager.SaveScene(scene, scenePath)) throw new InvalidOperationException("Scene save failed");
            }
            foreach (var name in new[] { "Lobby", "Dungeon" })
            {
                var path = "Assets/DungeonContract/Scenes/" + name + ".unity";
                if (!File.Exists(path)) { var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single); if (!EditorSceneManager.SaveScene(scene, path)) throw new InvalidOperationException("Scene save failed"); }
            }
            foreach (var name in new[] { "DungeonContract", "GameService" })
            {
                var target = Path.Combine(output, name, name + ".exe");
                Directory.CreateDirectory(Path.GetDirectoryName(target));
                var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions { scenes = new[] { scenePath, "Assets/DungeonContract/Scenes/Lobby.unity", "Assets/DungeonContract/Scenes/Dungeon.unity" }, locationPathName = target, target = BuildTarget.StandaloneWindows64, options = BuildOptions.None });
                if (report.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded) throw new InvalidOperationException("Demo role build failed");
                Debug.Log("DC role build succeeded: " + name + "; bytes=" + report.summary.totalSize);
                var configurationDirectory = Path.Combine(output, name, "Content");
                Directory.CreateDirectory(configurationDirectory);
                File.Copy(Path.GetFullPath("../../docs/game-demo/config/demo-v2.json"), Path.Combine(configurationDirectory, "demo-v2.json"), true);
                File.Copy(Path.GetFullPath("../../docs/game-demo/config/world-v2.json"), Path.Combine(configurationDirectory, "world-v2.json"), true);
                File.Copy(Path.GetFullPath("../../docs/game-demo/config/demo-v1.json"), Path.Combine(configurationDirectory, "demo-v1.json"), true);
                File.Copy(Path.GetFullPath("../../docs/game-demo/config/p1-world.v1.json"), Path.Combine(configurationDirectory, "p1-world.v1.json"), true);
                File.Copy(Path.GetFullPath("../../docs/game-demo/config/demo-v3-p0.json"), Path.Combine(configurationDirectory, "demo-v3-p0.json"), true);
                File.Copy(Path.GetFullPath("../../docs/game-demo/config/demo-v3-p1.json"), Path.Combine(configurationDirectory,"demo-v3-p1.json"),true);
                File.Copy(Path.GetFullPath("../../docs/game-demo/config/demo-v4-s1.json"), Path.Combine(configurationDirectory,"demo-v4-s1.json"),true);
                File.Copy(Path.GetFullPath("../../docs/game-demo/config/demo-v4-s2.json"), Path.Combine(configurationDirectory,"demo-v4-s2.json"),true);
                File.Copy(Path.GetFullPath("../../docs/game-demo/config/demo-v4-p2-1.json"), Path.Combine(configurationDirectory,"demo-v4-p2-1.json"),true);
                File.Copy(Path.GetFullPath("../../docs/game-demo/config/demo-v4-p2-mp.json"), Path.Combine(configurationDirectory,"demo-v4-p2-mp.json"),true);
                File.Copy(Path.GetFullPath("../../docs/game-demo/config/demo-v4-p3.json"), Path.Combine(configurationDirectory,"demo-v4-p3.json"),true);
                File.Copy(Path.GetFullPath("../../docs/game-demo/config/demo-v4-p4.json"), Path.Combine(configurationDirectory,"demo-v4-p4.json"),true);
                if((output.Contains("v7")||output.Contains("v8")))foreach(var filename in new[]{"demo-v7-p5.json","parameters-v7-p5.json","world-v7-p5.json"})File.Copy(Path.GetFullPath("../../docs/game-demo/config/"+filename),Path.Combine(configurationDirectory,filename),true);
                if(output.Contains("v6")||(output.Contains("v7")||output.Contains("v8")))foreach(var filename in new[]{"demo-v6-p5.json","parameters-v6-p5.json","world-v6-p5.json"})File.Copy(Path.GetFullPath("../../docs/game-demo/config/"+filename),Path.Combine(configurationDirectory,filename),true);
                if(output.Contains("v5"))foreach(var filename in new[]{"demo-v5-p4.json","parameters-v5-p4.json","world-v5-p4.json"})File.Copy(Path.GetFullPath("../../docs/game-demo/config/"+filename),Path.Combine(configurationDirectory,filename),true);
                if(output.Contains("v5-p5"))File.Copy(Path.GetFullPath("../../docs/game-demo/config/demo-v5-p5.json"),Path.Combine(configurationDirectory,"demo-v5-p5.json"),true);
                if(output.Contains("v5-p5")||output.Contains("v6-p5")||(output.Contains("v7-p5")||output.Contains("v8-p5")))
                {
                    var notices=Path.Combine(output,name,"ThirdPartyNotices");Directory.CreateDirectory(notices);
                    foreach(var license in new[]{("Assets/Resources/DungeonContract/Fonts/OFL.txt","NotoSansSC-OFL.txt"),("Assets/Resources/DungeonContract/Art/Kenney-CC0.txt","Historical-Kenney-CC0.txt"),("Assets/Resources/DungeonContract/P5/LICENSE.txt","P5-original-assets-CC0.txt"),("Assets/Resources/DungeonContract/P5/provenance.json","P5-resource-provenance.json"),("Packages/packages-lock.json","Unity-package-versions.json")})File.Copy(license.Item1,Path.Combine(notices,license.Item2),true);
                    foreach(var filename in File.Exists("Assets/Resources/DungeonContract/P5/ruins-lobby-ai.png")?new[]{"AI-NOTICE.txt","ai-provenance.json"}:Array.Empty<string>())
                    {
                        var source=Path.Combine("Assets/Resources/DungeonContract/P5/",filename);
                        if(File.Exists(source))File.Copy(source,Path.Combine(notices,filename),true);
                    }
                    foreach(var package in Directory.GetDirectories("Library/PackageCache"))
                    {
                        var packageName=Path.GetFileName(package);var legal=Directory.GetFiles(package,"*",SearchOption.TopDirectoryOnly).Where(filename=>Path.GetFileName(filename).StartsWith("LICENSE",StringComparison.OrdinalIgnoreCase)||Path.GetFileName(filename).StartsWith("THIRD PARTY NOTICES",StringComparison.OrdinalIgnoreCase));
                        foreach(var license in legal)File.Copy(license,Path.Combine(notices,packageName+"-"+Path.GetFileName(license)),true);
                    }
                }
                if(output.Contains("v8"))foreach(var filename in new[]{"demo-v8-p5.json","parameters-v8-p5.json","world-v8-p5.json"})File.Copy(Path.GetFullPath("../../docs/game-demo/config/"+filename),Path.Combine(configurationDirectory,filename),true);
                File.Copy(Path.GetFullPath("../../docs/game-demo/config/world-v4-p4.json"), Path.Combine(configurationDirectory,"world-v4-p4.json"),true);
                File.Copy(Path.GetFullPath("../../docs/game-demo/contracts/planned-v4/parameters.json"), Path.Combine(configurationDirectory,"parameters-v4-s1.json"),true);
                File.Copy(Path.GetFullPath("../../docs/game-demo/config/world-v3-p1.json"), Path.Combine(configurationDirectory,"world-v3-p1.json"),true);
                File.Copy(Path.GetFullPath("../../docs/game-demo/config/world-v4-p2-1.json"), Path.Combine(configurationDirectory,"world-v4-p2-1.json"),true);
                File.Copy(Path.GetFullPath("../../docs/game-demo/config/parameters-v4-p2-1.json"), Path.Combine(configurationDirectory,"parameters-v4-p2-1.json"),true);
                File.Copy(Path.GetFullPath("../../docs/game-demo/contracts/planned-v3/parameters.json"), Path.Combine(configurationDirectory,"parameters-v3-p1.json"),true);
            }
        }
    }
}

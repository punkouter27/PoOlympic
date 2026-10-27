using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace PoOlympic.Editor
{
    /// <summary>
    /// The game in a browser: a WebGL player in Builds/WebGL, served from the Azure Static Web App
    /// "poolympic" (resource group PoOlympic) by Tools/deploy_webgl.ps1. Menu Tools/PoOlympic/Build WebGL (browser), or headless:
    ///   Unity.exe -batchmode -quit -projectPath . -buildTarget WebGL -executeMethod PoOlympic.Editor.WebGLBuild.Build
    /// The outcome is the "WEBGL BUILD RESULT:" log line (and the exit code in batch mode).
    ///
    /// Gzip with the decompression fallback: Static Web Apps does not send the Content-Encoding
    /// header a compressed Unity build otherwise needs, so the loader unpacks the files itself.
    /// The canvas is 540 x 960.
    /// The MCP bridge's UNITY_MCP_* defines are stripped for the build, as for Android, so its
    /// runtime (and the NuGet closure behind it) stays out of the player.
    ///
    /// MuJoCo is statically linked: Assets/Plugins/WebGL/MuJoCo holds it compiled to wasm with this
    /// editor's own Emscripten (how: README.md beside it), and org.mujoco's MujocoLib binds to
    /// "__Internal" on WebGL. Those .a files are pinned to WebGL here, since Android's IL2CPP
    /// would otherwise try to link them too.
    /// </summary>
    public static class WebGLBuild
    {
        const string OUTPUT = "Builds/WebGL";
        const string MUJOCO_WASM = "Assets/Plugins/WebGL/MuJoCo";

        [MenuItem("Tools/PoOlympic/Build WebGL (browser)")]
        public static void Build()
        {
            if (EditorApplication.isPlaying) { Finish("Aborted — exit Play mode first", false); return; }

            string[] scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
            PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Gzip;
            PlayerSettings.WebGL.decompressionFallback = true;
            PlayerSettings.defaultWebScreenWidth = 540;
            PlayerSettings.defaultWebScreenHeight = 960;
            PinMujocoToWebGL();
            PlayerSettings.WebGL.template = "PROJECT:Po";

            string defines = PlayerSettings.GetScriptingDefineSymbols(NamedBuildTarget.WebGL);
            PlayerSettings.SetScriptingDefineSymbols(NamedBuildTarget.WebGL,
                string.Join(";", defines.Split(';').Where(d => !d.Trim().StartsWith("UNITY_MCP_"))));
            BuildSummary summary;
            try
            {
                ShrinkGlbTextures();
                summary = BuildPipeline.BuildPlayer(scenes, OUTPUT, BuildTarget.WebGL, BuildOptions.None).summary;
            }
            finally
            {
                RestoreGlbs();
                PlayerSettings.SetScriptingDefineSymbols(NamedBuildTarget.WebGL, defines);
            }
            Finish($"{summary.result} | v{PlayerSettings.bundleVersion} | {scenes.Length} scenes | {summary.totalErrors} errors | " +
                   $"{summary.totalSize / (1024 * 1024)} MB | {summary.totalTime.TotalMinutes:0.0} min | {OUTPUT}",
                   summary.result == BuildResult.Succeeded);
        }

        /// glTFast imports a GLB's images uncompressed, and the athletes' are 4096 px: Kim and
        /// Grandma came to ~259 MB each, the first web build to 291 MB, over the Static Web Apps
        /// Free cap (250 MB) and minutes of download. For the WebGL build only, each GLB here is
        /// rewritten with its images capped at WEB_TEXTURE px, and the original is put back after.
        /// Originals wait in Library/webgl-glb-originals; a build killed halfway is undone by the
        /// next one (RestoreGlbs runs first), or by `git checkout` on the .glb.
        static readonly string[] WEB_GLBS = { "Assets/PoOlympic/Art/Kim.glb", "Assets/PoOlympic/Art/Grandma.glb",
                                              "Assets/PoOlympic/Art/Zombie.glb", "Assets/PoOlympic/Art/MATT.glb" };
        const int WEB_TEXTURE = 1024;
        const string GLB_BACKUP = "Library/webgl-glb-originals";

        static void ShrinkGlbTextures()
        {
            RestoreGlbs();
            Directory.CreateDirectory(GLB_BACKUP);
            foreach (string glb in WEB_GLBS)
            {
                File.Copy(glb, Path.Combine(GLB_BACKUP, Path.GetFileName(glb)), true);
                File.WriteAllBytes(glb, Glb.CapImages(File.ReadAllBytes(glb), WEB_TEXTURE));
                AssetDatabase.ImportAsset(glb, ImportAssetOptions.ForceUpdate);
            }
        }

        static void RestoreGlbs()
        {
            if (!Directory.Exists(GLB_BACKUP)) return;
            foreach (string glb in WEB_GLBS)
            {
                string backup = Path.Combine(GLB_BACKUP, Path.GetFileName(glb));
                if (!File.Exists(backup)) continue;
                File.Copy(backup, glb, true);
                File.Delete(backup);
                AssetDatabase.ImportAsset(glb, ImportAssetOptions.ForceUpdate);
            }
        }

        static void PinMujocoToWebGL()
        {
            foreach (string path in Directory.GetFiles(MUJOCO_WASM, "*.a"))
            {
                var importer = (PluginImporter)AssetImporter.GetAtPath(path.Replace('\\', '/'));
                if (!importer.GetCompatibleWithAnyPlatform() && !importer.GetCompatibleWithEditor() &&
                    importer.GetCompatibleWithPlatform(BuildTarget.WebGL)) continue;
                importer.SetCompatibleWithAnyPlatform(false);
                importer.SetCompatibleWithEditor(false);
                importer.SetCompatibleWithPlatform(BuildTarget.WebGL, true);
                importer.SaveAndReimport();
            }
        }

        static void Finish(string message, bool ok)
        {
            if (ok) Debug.Log("WEBGL BUILD RESULT: " + message);
            else Debug.LogError("WEBGL BUILD RESULT: " + message);
            if (Application.isBatchMode) EditorApplication.Exit(ok ? 0 : 1);
        }
    }

    /// <summary>
    /// WebGL has no compute shaders, so VFX Graph cannot run there: each VisualEffect in a scene
    /// logged "Invalid VFX Particle System. It is skipped." and ImpactFx already falls back to
    /// Shuriken (sparksFallback) when SystemInfo.supportsComputeShaders is false. So the WebGL
    /// player gets the scenes without them.
    /// </summary>
    sealed class StripVfxForWebGL : IProcessSceneWithReport
    {
        public int callbackOrder => 0;

        public void OnProcessScene(UnityEngine.SceneManagement.Scene scene, BuildReport report)
        {
            if (report == null || report.summary.platform != BuildTarget.WebGL) return;
            foreach (var root in scene.GetRootGameObjects())
                foreach (var vfx in root.GetComponentsInChildren<UnityEngine.VFX.VisualEffect>(true))
                    Object.DestroyImmediate(vfx);
        }
    }

    /// <summary>Rewrites a binary glTF with every embedded image at most maxSize px on its longer side.</summary>
    static class Glb
    {
        const uint JSON = 0x4E4F534A, BIN = 0x004E4942;

        public static byte[] CapImages(byte[] glb, int maxSize)
        {
            int jsonLength = System.BitConverter.ToInt32(glb, 12);
            var gltf = Newtonsoft.Json.Linq.JObject.Parse(System.Text.Encoding.UTF8.GetString(glb, 20, jsonLength));
            int binStart = 20 + jsonLength + 8;

            var views = (Newtonsoft.Json.Linq.JArray)gltf["bufferViews"];
            var data = new byte[views.Count][];
            for (int i = 0; i < views.Count; i++)
            {
                int offset = (int?)views[i]["byteOffset"] ?? 0;
                data[i] = new byte[(int)views[i]["byteLength"]];
                System.Buffer.BlockCopy(glb, binStart + offset, data[i], 0, data[i].Length);
            }
            foreach (var image in gltf["images"] ?? new Newtonsoft.Json.Linq.JArray())
            {
                if (image["bufferView"] == null) continue;
                int view = (int)image["bufferView"];
                data[view] = Shrink(data[view], (string)image["mimeType"] == "image/jpeg", maxSize);
            }

            var bin = new MemoryStream();
            for (int i = 0; i < views.Count; i++)
            {
                while (bin.Length % 4 != 0) bin.WriteByte(0);
                views[i]["byteOffset"] = bin.Length;
                views[i]["byteLength"] = data[i].Length;
                bin.Write(data[i], 0, data[i].Length);
            }
            while (bin.Length % 4 != 0) bin.WriteByte(0);
            gltf["buffers"][0]["byteLength"] = bin.Length;

            byte[] json = System.Text.Encoding.UTF8.GetBytes(gltf.ToString(Newtonsoft.Json.Formatting.None));
            int jsonPadded = (json.Length + 3) & ~3;
            var output = new MemoryStream();
            var writer = new BinaryWriter(output);
            writer.Write(0x46546C67u); writer.Write(2u); writer.Write((uint)(12 + 8 + jsonPadded + 8 + bin.Length));
            writer.Write((uint)jsonPadded); writer.Write(JSON); writer.Write(json);
            for (int i = json.Length; i < jsonPadded; i++) writer.Write((byte)' ');
            writer.Write((uint)bin.Length); writer.Write(BIN); writer.Write(bin.ToArray());
            return output.ToArray();
        }

        /// Linear on both ends, so the bytes pass through untouched (normal maps stay normal maps);
        /// halved one 2x bilinear step at a time, which is a box filter, not one 4x jump that aliases.
        static byte[] Shrink(byte[] encoded, bool jpeg, int maxSize)
        {
            var source = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
            source.LoadImage(encoded);
            if (source.width <= maxSize && source.height <= maxSize) { Object.DestroyImmediate(source); return encoded; }

            Texture current = source;
            RenderTexture step = null;
            int width = source.width, height = source.height;
            while (width > maxSize || height > maxSize)
            {
                width = Mathf.Max(1, width / 2); height = Mathf.Max(1, height / 2);
                var next = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
                Graphics.Blit(current, next);
                if (step != null) RenderTexture.ReleaseTemporary(step);
                current = step = next;
            }

            var previous = RenderTexture.active;
            RenderTexture.active = step;
            var result = new Texture2D(width, height, TextureFormat.RGBA32, false, true);
            result.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            result.Apply();
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(step);

            byte[] output = jpeg ? result.EncodeToJPG(90) : result.EncodeToPNG();
            Object.DestroyImmediate(source);
            Object.DestroyImmediate(result);
            return output;
        }
    }
}

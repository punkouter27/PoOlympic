using System;
using System.Globalization;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace PoOlympic.Editor
{
    /// <summary>Writes Assets/PoOlympic/Resources/BuildStamp.txt (YYMMDD) before every build and sets
    /// PlayerSettings.bundleVersion to it. The stamp is PO_BUILD_STAMP when scripts/publish_webgl.ps1 set it (one
    /// stamp for the player, index.html, diag.html and the release tag), else today's UTC date.</summary>
    public sealed class BuildStampPreprocessor : IPreprocessBuildWithReport
    {
        private const string Dir = "Assets/PoOlympic/Resources";
        private const string AssetPath = Dir + "/BuildStamp.txt";
        public int callbackOrder => -100;

        public static string Stamp =>
            Environment.GetEnvironmentVariable("PO_BUILD_STAMP") is { Length: 6 } s && int.TryParse(s, out _)
                ? s : DateTime.UtcNow.ToString("yyMMdd", CultureInfo.InvariantCulture);

        public void OnPreprocessBuild(BuildReport report)
        {
            Directory.CreateDirectory(Dir);
            string stamp = Stamp;
            File.WriteAllText(AssetPath, stamp);
            PlayerSettings.bundleVersion = stamp;
            AssetDatabase.ImportAsset(AssetPath, ImportAssetOptions.ForceSynchronousImport);
        }
    }
}

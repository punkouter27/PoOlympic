using System;
using System.Globalization;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace PoOlympic.Editor
{
    /// <summary>Writes Assets/PoOlympic/Resources/BuildStamp.txt (YYMMDD) before every build.</summary>
    public sealed class BuildStampPreprocessor : IPreprocessBuildWithReport
    {
        private const string Dir = "Assets/PoOlympic/Resources";
        private const string AssetPath = Dir + "/BuildStamp.txt";
        public int callbackOrder => -100;

        public void OnPreprocessBuild(BuildReport report)
        {
            Directory.CreateDirectory(Dir);
            string stamp = DateTime.UtcNow.ToString("yyMMdd", CultureInfo.InvariantCulture);
            File.WriteAllText(AssetPath, stamp);
            PlayerSettings.bundleVersion = stamp;
            AssetDatabase.ImportAsset(AssetPath, ImportAssetOptions.ForceSynchronousImport);
        }
    }
}

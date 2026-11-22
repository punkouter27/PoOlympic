using NUnit.Framework;
using UnityEditor;

namespace PoOlympic.Tests
{
    /// <summary>The editor-tunable showcase settings (attract mode, broadcast camera, gaze, Tab benchmark) are one
    /// ScriptableObject in Resources that the player loads, and the camera values every event scene used before they
    /// moved there are unchanged.</summary>
    public class ShowcaseSettingsTests
    {
        const string Path = "Assets/_Project/Data/Resources/ShowcaseSettings.asset";

        [Test]
        public void AssetIsThePlayersSettings()
        {
            var asset = AssetDatabase.LoadAssetAtPath<ShowcaseSettings>(Path);
            Assert.NotNull(asset, Path + " missing");
            Assert.AreSame(asset, ShowcaseSettings.Current, "Resources/ShowcaseSettings is the asset the player uses");
            Assert.AreEqual(30f, asset.idleDemoSeconds, "attract mode after 30 s idle");
            Assert.Greater(asset.benchmarkSeconds, 0f);
            Assert.That(asset.gazeMaxYaw, Is.InRange(1f, 90f), "gaze yaw within a neck's reach");
            Assert.That(asset.gazeMaxPitch, Is.InRange(1f, 60f), "gaze pitch within a neck's reach");
            // the BroadcastDirector values of the 21 event scenes before they moved into the asset
            Assert.AreEqual(5f, asset.shotSeconds);
            Assert.AreEqual(0.12f, asset.orbitSpeed, 1e-6f);
            Assert.AreEqual(2.5f, asset.shotBlendSeconds);
        }

        [Test]
        public void BenchmarkNeedsALoadedWorld()
        {
            if (Mujoco.MjScene.InstanceExists) Assert.Ignore("a MuJoCo scene is open in the editor");
            Assert.IsFalse(SimBenchmark.Start());
            StringAssert.Contains("start an event first", SimBenchmark.Result);
        }
    }
}

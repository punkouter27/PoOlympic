using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace PoOlympic.Tests
{
    /// <summary>The Unity reward-penalty asset and the trainer's config (Training/config/reward_penalties.json) hold the
    /// same weights, so the Tab overlay's episode reward weighs effort / smoothness as the brains were trained.</summary>
    public class RewardPenaltiesTests
    {
        [System.Serializable]
        class Config { public float torque_l2, body_ang_vel_l2, action_rate_l2, foot_slip, joint_vel_l2; }

        [Test]
        public void AssetMatchesTrainingConfig()
        {
            var asset = AssetDatabase.LoadAssetAtPath<RewardPenalties>("Assets/_Project/Data/Resources/RewardPenalties.asset");
            Assert.NotNull(asset, "Assets/_Project/Data/Resources/RewardPenalties.asset missing");
            var path = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Training", "config", "reward_penalties.json"));
            var cfg = JsonUtility.FromJson<Config>(File.ReadAllText(path));
            void Same(float unity, float python, string name) =>
                Assert.AreEqual(python, unity, Mathf.Abs(python) * 1e-5f + 1e-12f, $"{name}: Unity {unity} vs training {python}");
            Same(asset.torqueL2, cfg.torque_l2, "torque_l2");
            Same(asset.bodyAngVelL2, cfg.body_ang_vel_l2, "body_ang_vel_l2");
            Same(asset.actionRateL2, cfg.action_rate_l2, "action_rate_l2");
            Same(asset.footSlip, cfg.foot_slip, "foot_slip");
            Same(asset.jointVelL2, cfg.joint_vel_l2, "joint_vel_l2");
            Assert.AreSame(asset, RewardPenalties.Current, "Resources/RewardPenalties is the asset the player uses");
        }
    }
}

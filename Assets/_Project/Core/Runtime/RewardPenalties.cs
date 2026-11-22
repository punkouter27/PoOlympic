using UnityEngine;

namespace PoOlympic
{
    /// <summary>
    /// The smoothness / effort penalty weights every PoOlympics task trains with, as an editor asset
    /// (Assets/_Project/Data/Resources/RewardPenalties.asset). The trainer reads the same numbers from
    /// Training/config/reward_penalties.json; RewardPenaltiesTests fails when the two disagree.
    /// In the player they weight the episode-reward readout of the Tab overlay (PolicyRunner.EpisodeReward):
    /// alive bonus + these penalties, per second, like mjlab scales each reward term by the step time.
    /// Editing a value does not retrain the shipped brains: a new value needs new task IDs and a training run.
    /// </summary>
    [CreateAssetMenu(menuName = "PoOlympic/Reward Penalties", fileName = "RewardPenalties")]
    public sealed class RewardPenalties : ScriptableObject
    {
        [Tooltip("Σ joint torque² (training: torque_l2, mjlab joint_torques_l2).")]
        public float torqueL2 = -1e-5f;
        [Tooltip("Body (torso) angular velocity² (training: body_ang_vel_l2). The readout uses the pelvis.")]
        public float bodyAngVelL2 = -0.05f;
        [Tooltip("‖a_t − a_{t−1}‖² (training: action_rate_l2).")]
        public float actionRateL2 = -0.05f;
        [Tooltip("Foot planar speed while in contact (training: foot_slip; gait tasks). Not in the readout.")]
        public float footSlip = -0.1f;
        [Tooltip("Σ joint angular velocity² (training: joint_vel_l2; 0 = no term, the shipped tasks have none). Not in the readout.")]
        public float jointVelL2 = 0f;
        [Tooltip("Readout only: reward per second while the athlete is driven by its brain (not a training term).")]
        public float aliveBonus = 1f;

        static RewardPenalties _current;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => _current = null;

        /// <summary>The project asset (Resources/RewardPenalties), else the defaults above.</summary>
        public static RewardPenalties Current
        {
            get
            {
                if (_current == null) _current = Resources.Load<RewardPenalties>("RewardPenalties");
                if (_current == null) _current = CreateInstance<RewardPenalties>();
                return _current;
            }
        }
    }
}

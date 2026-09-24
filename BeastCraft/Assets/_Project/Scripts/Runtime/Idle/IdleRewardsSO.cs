using System;
using UnityEngine;

namespace BeastCraft.Idle
{
    /// <summary>
    /// The imported idle reward rates: <c>Data/Idle/idle-rewards.json</c> copied verbatim by the Editor
    /// importer (menu: Beast Craft/Data/Import Idle Rewards), so the game reads exactly the data the
    /// balance simulator's <c>--mode campaign</c> paced. Never hand-edit <see cref="Data"/>; edit the
    /// JSON and re-import. <see cref="Rewards"/> builds the runtime <see cref="IdleRewards"/> on first
    /// use and keeps it.
    /// </summary>
    [CreateAssetMenu(menuName = "Beast Craft/Idle/Idle Rewards", fileName = "IdleRewards")]
    public class IdleRewardsSO : ScriptableObject
    {
        /// <summary>The rates, as imported.</summary>
        public IdleRewardsData Data = new IdleRewardsData();

        [NonSerialized]
        private IdleRewards _rewards;

        /// <summary>The built rates (built once, then shared).</summary>
        public IdleRewards Rewards
        {
            get
            {
                if (_rewards == null)
                {
                    _rewards = IdleRewardsBuilder.Build(Data);
                }

                return _rewards;
            }
        }

        /// <summary>Drops the built rates, so the next <see cref="Rewards"/> reads the current data (the importer calls it).</summary>
        public void ResetRuntimeCaches()
        {
            _rewards = null;
        }
    }
}

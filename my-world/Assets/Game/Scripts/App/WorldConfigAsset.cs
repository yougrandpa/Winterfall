using MyAiWorld.Sim.World;
using UnityEngine;

namespace MyAiWorld.App
{
    [CreateAssetMenu(menuName = "My AI World/World Config", fileName = "WorldConfig")]
    public sealed class WorldConfigAsset : ScriptableObject
    {
        [Header("World")]
        [Range(16, 256)]
        public int width = 64;

        [Range(16, 256)]
        public int height = 64;

        public int seed = 20261020;

        [Range(0, 100)]
        public int npcCount = 20;

        [Header("Simulation")]
        [Range(1, 30)]
        public int ticksPerSecondAt1x = 2;

        public int[] speedMultipliers = { 1, 2, 4, 8 };

        public WorldConfigData ToData()
        {
            int[] speeds = speedMultipliers;
            if (speeds == null || speeds.Length == 0)
            {
                speeds = new[] { 1, 2, 4, 8 };
            }

            return new WorldConfigData(
                seed,
                Mathf.Clamp(width, 16, 256),
                Mathf.Clamp(height, 16, 256),
                Mathf.Clamp(npcCount, 0, 100),
                GameCalendar.StartTick,
                Mathf.Clamp(ticksPerSecondAt1x, 1, 30),
                speeds);
        }

        private void OnValidate()
        {
            width = Mathf.Clamp(width, 16, 256);
            height = Mathf.Clamp(height, 16, 256);
            npcCount = Mathf.Clamp(npcCount, 0, 100);
            ticksPerSecondAt1x = Mathf.Clamp(ticksPerSecondAt1x, 1, 30);
            if (speedMultipliers == null || speedMultipliers.Length == 0)
            {
                speedMultipliers = new[] { 1, 2, 4, 8 };
            }
        }
    }
}

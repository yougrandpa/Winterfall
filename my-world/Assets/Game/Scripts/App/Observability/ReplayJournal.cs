using System;
using System.Collections.Generic;
using System.IO;
using MyAiWorld.Sim.Systems;
using MyAiWorld.Sim.World;
using Newtonsoft.Json;
using UnityEngine;

namespace MyAiWorld.App.Observability
{
    public sealed class ReplayJournal : IDisposable
    {
        private readonly WorldConfigData _config;
        private readonly List<ReplayCommandRecord> _commands = new List<ReplayCommandRecord>();
        private readonly byte[] _initialSnapshot;
        private bool _disposed;
        private bool _completed;

        public ReplayJournal(WorldConfigData config, byte[] initialSnapshot = null)
        {
            _initialSnapshot = initialSnapshot;
            _config = config ?? throw new ArgumentNullException(nameof(config));
            try
            {
                string root = Path.Combine(Application.persistentDataPath, "MyAiWorld", "replays");
                Directory.CreateDirectory(root);
                string stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff");
                FilePath = Path.Combine(root, "replay-" + stamp + ".json");
            }
            catch (Exception exception)
            {
                Debug.LogWarning("My AI World: replay journal is unavailable: " + exception.Message);
                FilePath = null;
            }
        }

        public string FilePath { get; }

        public void RecordCommand(long tick, string kind, string description)
        {
            if (_commands.Count >= 20000)
            {
                return;
            }

            _commands.Add(new ReplayCommandRecord
            {
                Tick = tick,
                Kind = kind,
                Description = description
            });
        }

        public void Complete(long tick, ulong stateHash)
        {
            if (_completed || string.IsNullOrEmpty(FilePath))
            {
                return;
            }

            _completed = true;

            var replay = new ReplayData
            {
                FormatVersion = 4,
                InitialSnapshot = _initialSnapshot,
                StateHashVersion = StateHasher.Version,
                Seed = _config.Seed,
                Width = _config.Width,
                Height = _config.Height,
                NpcCount = _config.NpcCount,
                StartTick = _config.StartTick,
                FinalTick = tick,
                FinalStateHash = stateHash.ToString("x16"),
                Commands = _commands
            };

            try
            {
                File.WriteAllText(FilePath, JsonConvert.SerializeObject(replay, Formatting.Indented));
            }
            catch (Exception exception)
            {
                Debug.LogWarning("My AI World: failed to write replay journal: " + exception.Message);
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            if (!_completed)
            {
                Complete(0, 0UL);
            }

            _disposed = true;
        }

        private sealed class ReplayData
        {
            public byte[] InitialSnapshot { get; set; }
            public int FormatVersion { get; set; }
            public int StateHashVersion { get; set; }
            public int Seed { get; set; }
            public int Width { get; set; }
            public int Height { get; set; }
            public int NpcCount { get; set; }
            public long StartTick { get; set; }
            public long FinalTick { get; set; }
            public string FinalStateHash { get; set; }
            public List<ReplayCommandRecord> Commands { get; set; }
        }

        private sealed class ReplayCommandRecord
        {
            public long Tick { get; set; }
            public string Kind { get; set; }
            public string Description { get; set; }
        }
    }
}

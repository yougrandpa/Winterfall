using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using MyAiWorld.App.Observability;
using MyAiWorld.App.Query;
using MyAiWorld.Sim.Core;
using MyAiWorld.Sim.Systems;
using MyAiWorld.Sim.World;

namespace MyAiWorld.App
{
    public sealed class GameSession : IDisposable
    {
        private readonly Queue<IWorldCommand> _commandQueue = new Queue<IWorldCommand>();
        private readonly List<TickResult> _combatFrames = new List<TickResult>();
        private readonly SimulationEngine _engine;
        private long _eventSequence;
        private int _loggedTradeCount;
        private bool _disposed;

        public GameSession(WorldConfigData config)
        {
            if (config == null)
            {
                throw new ArgumentNullException(nameof(config));
            }

            Config = config;
            List<SimEvent> initialEvents;
            World = WorldFactory.Create(config, out initialEvents);
            _engine = new SimulationEngine();
            ReadModel = new WorldReadModel(config);

            for (int i = 0; i < initialEvents.Count; i++)
            {
                initialEvents[i].Sequence = ++_eventSequence;
            }

            ReadModel.Initialize(World, initialEvents);
            Log = new SessionLog(World, initialEvents);
            Replay = new ReplayJournal(config);
            LastStateHash = StateHasher.Compute(World);
            LastTick = World.Tick;
        }

        public WorldConfigData Config { get; private set; }
        public WorldState World { get; private set; }
        public WorldReadModel ReadModel { get; private set; }
        public SessionLog Log { get; private set; }
        public ReplayJournal Replay { get; private set; }
        public ulong LastStateHash { get; private set; }
        public long LastTick { get; private set; }
        public int PathFailureCount { get; private set; }
        public int AppliedCommandCount { get; private set; }
        public int QueuedCommandCount => _commandQueue.Count;
        public string LogPath => Log != null ? Log.FilePath : null;
        public int WorldRevision { get; private set; }
        public string SaveStatus { get; private set; } = "尚未存档";
        public bool LastSaveSucceeded { get; private set; }
        public string SaveDirectory { get; set; } = Path.Combine(Application.persistentDataPath, "MyAiWorld", "saves-winter-ecology-v4");
        public string ManualSavePath => Path.Combine(SaveDirectory, "manual.json");
        public string AutoSavePath => Path.Combine(SaveDirectory, "auto.json");
        public string ReplayPath => Replay != null ? Replay.FilePath : null;

        public void EnqueueCommand(IWorldCommand command)
        {
            if (command == null)
            {
                throw new ArgumentNullException(nameof(command));
            }

            _commandQueue.Enqueue(command);
        }

        public void SpawnDebugNpc()
        {
            EnqueueCommand(new SpawnNpcCommand());
        }

        public IReadOnlyList<TickResult> DrainCombatFrames()
        {
            var frames = Array.AsReadOnly(_combatFrames.ToArray());
            _combatFrames.Clear();
            return frames;
        }

        public TickResult Tick()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(GameSession));
            }

            var commands = new List<IWorldCommand>(_commandQueue.Count);
            while (_commandQueue.Count > 0)
            {
                commands.Add(_commandQueue.Dequeue());
            }

            long commandTick = World.Tick;
            for (int i = 0; i < commands.Count; i++)
            {
                IWorldCommand command = commands[i];
                Log.WriteCommand(commandTick, command.GetType().Name, command.Describe());
                Replay.RecordCommand(commandTick, command.GetType().Name, command.Describe());
            }

            long firstSocialSequence = World.Social.NextSequence;
            TickResult result = _engine.Tick(World, commands);
            if (result.CombatTraces.Count > 0 || result.CombatEvents.Count > 0) { _combatFrames.Add(result); }
            for (int i = 0; i < result.Events.Count; i++)
            {
                SimEvent evt = result.Events[i];
                evt.Sequence = ++_eventSequence;
                Log.WriteEvent(evt);
                if (evt.Kind == SimEventKind.NpcPathFailed)
                {
                    PathFailureCount++;
                }
            }

            while (_loggedTradeCount < World.Market.Ledger.Count)
            {
                Log.WriteTrade(World.Market.Ledger[_loggedTradeCount++]);
            }
            foreach (var interaction in World.Social.Timeline)
            {
                if (interaction.Sequence >= firstSocialSequence) { Log.WriteSocial(interaction); }
            }
            AppliedCommandCount += result.AppliedCommands;
            LastStateHash = result.StateHash;
            LastTick = result.Tick;
            ReadModel.Refresh(World, result, PathFailureCount);
            if (World.Tick != commandTick &&
                (World.Tick % GameCalendar.TicksPerDay == 0 || !World.Campaign.CanAdvance))
            {
                Save(AutoSavePath, true);
            }
            return result;
        }

        public bool Save(string path = null, bool automatic = false)
        {
            try
            {
                if (_disposed) { throw new ObjectDisposedException(nameof(GameSession)); }
                var data = new SessionSaveData
                {
                    World = WorldSnapshot.Capture(World), HashVersion = StateHasher.Version,
                    StateHash = StateHasher.ToHex(StateHasher.Compute(World)), EventSequence = _eventSequence,
                    PathFailures = PathFailureCount, AppliedCommands = AppliedCommandCount
                };
                foreach (IWorldCommand command in _commandQueue) { data.Commands.Add(SavedCommand.Capture(command)); }
                SaveStore.Write(path ?? ManualSavePath, data);
                LastSaveSucceeded = true;
                SaveStatus = (automatic ? "自动存档成功 · " : "存档成功 · ") + World.Date.ClockText;
                return true;
            }
            catch (Exception exception)
            {
                LastSaveSucceeded = false;
                SaveStatus = "存档失败：" + exception.Message;
                return false;
            }
        }

        public bool Load(string path = null)
        {
            try
            {
                if (_disposed) { throw new ObjectDisposedException(nameof(GameSession)); }
                WorldState candidate;
                SessionSaveData data = SaveStore.Read(path ?? ManualSavePath, out candidate);
                var commands = new List<IWorldCommand>();
                foreach (SavedCommand record in data.Commands) { commands.Add(record.Restore()); }
                var readModel = new WorldReadModel(candidate.Config);
                readModel.Initialize(candidate, new List<SimEvent>());
                readModel.Refresh(candidate, null, data.PathFailures);
                // All parsing and validation has succeeded; now publish the replacement.
                Log.WriteSessionEnd(World.Tick, LastStateHash);
                Log.Dispose(); Replay.Complete(World.Tick, LastStateHash); Replay.Dispose();
                World = candidate; Config = candidate.Config; ReadModel = readModel;
                _commandQueue.Clear();
                _combatFrames.Clear();
                foreach (IWorldCommand command in commands) { _commandQueue.Enqueue(command); }
                _eventSequence = data.EventSequence; PathFailureCount = data.PathFailures;
                AppliedCommandCount = data.AppliedCommands; _loggedTradeCount = candidate.Market.Ledger.Count;
                Log = new SessionLog(World, new List<SimEvent>());
                Replay = new ReplayJournal(Config, WorldSnapshot.Capture(World));
                LastTick = World.Tick; LastStateHash = StateHasher.Compute(World); WorldRevision++;
                SaveStatus = "读档成功 · " + World.Date.ClockText;
                return true;
            }
            catch (Exception exception)
            {
                SaveStatus = "读档失败，当前世界已保留：" + exception.Message;
                return false;
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            if (Log != null)
            {
                Log.WriteSessionEnd(World.Tick, LastStateHash);
                Log.Dispose();
            }

            if (Replay != null)
            {
                Replay.Complete(World.Tick, LastStateHash);
                Replay.Dispose();
            }
        }
    }
}

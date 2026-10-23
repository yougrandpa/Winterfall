using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using MyAiWorld.Sim.Core;
using MyAiWorld.Sim.Systems;
using MyAiWorld.Sim.World;
using Newtonsoft.Json;

namespace MyAiWorld.App.Observability
{
    public sealed class SavedCommand
    {
        public string Kind { get; set; }
        public int[] Arguments { get; set; }
        public long OrderId { get; set; }

        public static SavedCommand Capture(IWorldCommand command)
        {
            if (command is SpawnNpcCommand spawn)
            {
                return new SavedCommand { Kind = "spawn", Arguments = spawn.RequestedCell.HasValue ? new[] { spawn.RequestedCell.Value.X, spawn.RequestedCell.Value.Y } : new int[0] };
            }
            if (command is TradeCommand trade)
            {
                TradeOrder o = trade.Order;
                return new SavedCommand { Kind = "trade", OrderId = o.Id, Arguments = new[] { o.NpcId.Value, (int)o.Side, (int)o.Item, o.Quantity, o.UnitPrice } };
            }
            throw new InvalidDataException("Unsupported pending command.");
        }
        public IWorldCommand Restore()
        {
            if (Kind == "spawn" && Arguments != null && (Arguments.Length == 0 || Arguments.Length == 2))
            {
                return Arguments.Length == 0 ? new SpawnNpcCommand() : new SpawnNpcCommand(new GridPos(Arguments[0], Arguments[1]));
            }
            if (Kind == "trade" && Arguments != null && Arguments.Length == 5)
            {
                return new TradeCommand(new TradeOrder(OrderId, new EntityId(Arguments[0]), (TradeSide)Arguments[1], (ItemKind)Arguments[2], Arguments[3], Arguments[4]));
            }
            throw new InvalidDataException("Invalid saved command.");
        }
    }

    public sealed class SessionSaveData
    {
        public byte[] World { get; set; }
        public int HashVersion { get; set; }
        public string StateHash { get; set; }
        public long EventSequence { get; set; }
        public int PathFailures { get; set; }
        public int AppliedCommands { get; set; }
        public List<SavedCommand> Commands { get; set; } = new List<SavedCommand>();
    }

    public static class SaveStore
    {
        public const int CurrentVersion = 3;
        private const int MaxBytes = 96 * 1024 * 1024;
        private sealed class Envelope
        {
            public int Version { get; set; }
            public byte[] Payload { get; set; }
            public string Checksum { get; set; }
        }

        public static void Write(string path, SessionSaveData data)
        {
            string folder = Path.GetDirectoryName(Path.GetFullPath(path));
            Directory.CreateDirectory(folder);
            byte[] payload = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(data));
            if (payload.Length > MaxBytes) { throw new InvalidDataException("Save exceeds size limit."); }
            byte[] bytes = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(new Envelope { Version = CurrentVersion, Payload = payload, Checksum = Digest(payload) }));
            if (bytes.Length > MaxBytes) { throw new InvalidDataException("Save exceeds size limit."); }
            string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var file = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    file.Write(bytes, 0, bytes.Length);
                    file.Flush(true);
                }
                if (File.Exists(path))
                {
                    // Never replace a good backup with a corrupt primary.
                    bool valid = false;
                    try { Read(path, out _); valid = true; }
                    catch (Exception e) when (e is IOException || e is JsonException || e is ArgumentException || e is InvalidOperationException) { }
                    File.Replace(temp, path, valid ? path + ".bak" : null);
                }
                else { File.Move(temp, path); }
            }
            finally { if (File.Exists(temp)) { File.Delete(temp); } }
        }

        public static SessionSaveData Read(string path, out WorldState world)
        {
            if (new FileInfo(path).Length > MaxBytes) { throw new InvalidDataException("Save exceeds size limit."); }
            var envelope = JsonConvert.DeserializeObject<Envelope>(File.ReadAllText(path));
            if (envelope != null && envelope.Version != CurrentVersion) { throw new InvalidDataException("旧版存档不支持本次生存规则，请重新开局；旧文件已保留。"); }
            if (envelope == null || envelope.Version != CurrentVersion || envelope.Payload == null || envelope.Checksum != Digest(envelope.Payload))
            {
                throw new InvalidDataException("Unsupported or damaged save.");
            }
            var data = JsonConvert.DeserializeObject<SessionSaveData>(Encoding.UTF8.GetString(envelope.Payload));
            if (data == null || data.World == null || data.HashVersion != StateHasher.Version || data.Commands == null || data.Commands.Count > 20000 || data.EventSequence < 0 || data.PathFailures < 0 || data.AppliedCommands < 0)
            {
                throw new InvalidDataException("Invalid save metadata.");
            }
            world = WorldSnapshot.Restore(data.World);
            if (StateHasher.ToHex(StateHasher.Compute(world, data.HashVersion)) != data.StateHash) { throw new InvalidDataException("Save state hash mismatch."); }
            foreach (SavedCommand command in data.Commands) { if (command == null) { throw new InvalidDataException("Missing command."); } command.Restore(); }
            return data;
        }

        private static string Digest(byte[] bytes)
        {
            using (var sha = SHA256.Create()) { return Convert.ToBase64String(sha.ComputeHash(bytes)); }
        }
    }
}

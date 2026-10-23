using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using MyAiWorld.Sim.Core;
using MyAiWorld.Sim.Agents;
using MyAiWorld.Sim.Systems;
using MyAiWorld.Sim.World;
using Newtonsoft.Json;
using UnityEngine;

namespace MyAiWorld.App.Observability
{
    public sealed class SessionLog : IDisposable
    {
        private StreamWriter _writer;
        private bool _disposed;

        public SessionLog(WorldState world, List<SimEvent> initialEvents)
        {
            if (world == null)
            {
                throw new ArgumentNullException(nameof(world));
            }

            try
            {
                string root = Path.Combine(Application.persistentDataPath, "MyAiWorld", "sessions");
                Directory.CreateDirectory(root);
                string stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff");
                FilePath = Path.Combine(root, "session-" + stamp + ".jsonl");
                _writer = new StreamWriter(FilePath, false, new UTF8Encoding(false));
                _writer.AutoFlush = true;

                WriteRecord(new
                {
                    type = "session_start",
                    stateHashVersion = StateHasher.Version,
                    utc = DateTime.UtcNow.ToString("o"),
                    seed = world.Config.Seed,
                    width = world.Config.Width,
                    height = world.Config.Height,
                    npcCount = world.Config.NpcCount,
                    startTick = world.Tick,
                    initialStateHash = StateHasher.ToHex(StateHasher.Compute(world))
                });

                if (initialEvents != null)
                {
                    for (int i = 0; i < initialEvents.Count; i++)
                    {
                        WriteEvent(initialEvents[i]);
                    }
                }
            }
            catch (Exception exception)
            {
                Debug.LogWarning("My AI World: structured logging is unavailable: " + exception.Message);
                _writer = null;
                FilePath = null;
            }
        }

        public string FilePath { get; }

        public void WriteEvent(SimEvent evt)
        {
            if (evt == null)
            {
                return;
            }

            WriteRecord(new
            {
                type = "event",
                seq = evt.Sequence,
                tick = evt.Tick,
                kind = evt.Kind.ToString(),
                source = evt.Source.Value,
                message = evt.Message
            });
        }

        public void WriteSocial(SocialEvent evt)
        {
            WriteRecord(new
            {
                type = "social", sequence = evt.Sequence, tick = evt.Tick, actor = evt.Actor.Value,
                target = evt.Target.Value, kind = evt.Kind.ToString(), trustBefore = evt.TrustBefore,
                trustAfter = evt.TrustAfter, reason = evt.Reason, causeSequence = evt.CauseSequence
            });
        }

        public void WriteTrade(TradeRecord trade)
        {
            WriteRecord(new
            {
                type = "trade",
                orderId = trade.Order.Id,
                tick = trade.Tick,
                npcId = trade.Order.NpcId.Value,
                side = trade.Order.Side.ToString(),
                item = trade.Order.Item.ToString(),
                quantity = trade.Order.Quantity,
                unitPrice = trade.Order.UnitPrice,
                reason = trade.Reason,
                value = trade.Value,
                walletBefore = trade.WalletBefore,
                walletAfter = trade.WalletAfter,
                treasuryBefore = trade.TreasuryBefore,
                treasuryAfter = trade.TreasuryAfter,
                bagBefore = trade.BagBefore,
                bagAfter = trade.BagAfter,
                marketBefore = trade.MarketBefore,
                marketAfter = trade.MarketAfter
            });
        }

        public void WriteCommand(long tick, string kind, string description)
        {
            WriteRecord(new
            {
                type = "command",
                tick,
                kind,
                description
            });
        }

        public void WriteSessionEnd(long tick, ulong hash)
        {
            WriteRecord(new
            {
                type = "session_end",
                tick,
                hash = hash.ToString("x16")
            });
        }

        private void WriteRecord(object record)
        {
            if (_writer == null)
            {
                return;
            }

            try
            {
                _writer.WriteLine(JsonConvert.SerializeObject(record));
            }
            catch (Exception exception)
            {
                Debug.LogWarning("My AI World: failed to write session log: " + exception.Message);
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            if (_writer != null)
            {
                _writer.Dispose();
                _writer = null;
            }
        }
    }
}

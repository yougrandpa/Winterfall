using System;
using System.Collections.Generic;
using MyAiWorld.Sim.Agents;
using MyAiWorld.Sim.Core;
using MyAiWorld.Sim.Systems;

namespace MyAiWorld.Sim.World
{
    public enum TradeSide
    {
        Buy, Sell
    }

    public sealed class TradeOrder
    {
        public TradeOrder(long id, EntityId npcId, TradeSide side, ItemKind item, int quantity, int unitPrice)
        {
            Id = id;
            NpcId = npcId;
            Side = side;
            Item = item;
            Quantity = quantity;
            UnitPrice = unitPrice;
        }

        public long Id { get; }
        public EntityId NpcId { get; }
        public TradeSide Side { get; }
        public ItemKind Item { get; }
        public int Quantity { get; }
        public int UnitPrice { get; }
    }

    public sealed class TradeRecord
    {
        internal TradeRecord(TradeOrder order, long tick, string reason, long walletBefore, long treasuryBefore,
            int bagBefore, int marketBefore)
        {
            Order = order;
            Tick = tick;
            Reason = reason;
            WalletBefore = walletBefore;
            TreasuryBefore = treasuryBefore;
            BagBefore = bagBefore;
            MarketBefore = marketBefore;
        }

        public TradeOrder Order { get; }
        public long Tick { get; }
        public string Reason { get; }
        public bool Filled => Reason == "filled";
        public long Value => Filled ? (long)Order.Quantity * Order.UnitPrice : 0;
        public long WalletBefore { get; }
        public long TreasuryBefore { get; }
        public int BagBefore { get; }
        public int MarketBefore { get; }
        public long WalletAfter => WalletBefore + (Order.Side == TradeSide.Buy ? -Value : Value);
        public long TreasuryAfter => TreasuryBefore + (Order.Side == TradeSide.Buy ? Value : -Value);
        public int BagAfter => BagBefore + (Filled ? (Order.Side == TradeSide.Buy ? Order.Quantity : -Order.Quantity) : 0);
        public int MarketAfter => MarketBefore + (Filled ? (Order.Side == TradeSide.Buy ? -Order.Quantity : Order.Quantity) : 0);
    }

    public sealed partial class MarketState
    {
        private readonly List<TradeRecord> _ledger = new List<TradeRecord>();
        private readonly int[] _prices = { 5, 3, 4 };
        private readonly long[] _bought = new long[SurvivalRules.MarketItemCount];
        private readonly long[] _sold = new long[SurvivalRules.MarketItemCount];

        public MarketState()
        {
            Ledger = _ledger.AsReadOnly();
            Inventory = new InventoryState(600);
        }

        public InventoryState Inventory { get; }
        public long Treasury { get; internal set; } = 2000;
        public long IssuedCurrency { get; internal set; } = 2000;
        public long NextOrderId { get; private set; } = 1;
        public IReadOnlyList<TradeRecord> Ledger { get; }
        public ulong LedgerHash { get; private set; } = 14695981039346656037UL;
        public int FilledCount { get; private set; }
        public int RejectedCount { get; private set; }
        public long Volume { get; private set; }
        public int Price(ItemKind item) => ValidItem(item) ? _prices[(int)item] : 0;
        public long Bought(ItemKind item) => ValidItem(item) ? _bought[(int)item] : 0;
        public long Sold(ItemKind item) => ValidItem(item) ? _sold[(int)item] : 0;

        internal void UpdatePrices()
        {
            for (int i = 0; i < SurvivalRules.MarketItemCount; i++)
            {
                // Scarce goods cost more; all prices remain positive and bounded.
                int basis = i == 0 ? 5 : i == 1 ? 3 : 4;
                _prices[i] = Math.Max(1, Math.Min(12, basis + (30 - Inventory.Count((ItemKind)i)) / 8));
            }
        }

        public long CurrencyError(WorldState world)
        {
            long total = Treasury + world.Survival.GroundCoins;
            for (int i = 0; i < world.Entities.Npcs.Count; i++)
            {
                total += world.Entities.Npcs[i].Coins;
            }
            return total - IssuedCurrency;
        }

        // Validate every leg before applying any mutation. IDs prevent duplicate fills.
        internal bool Execute(WorldState world, TradeOrder order, List<SimEvent> events)
        {
            if (order == null)
            {
                return false;
            }
            if (order.Id != NextOrderId)
            {
                events.Add(new SimEvent(SimEventKind.TradeRejected, world.Tick, order.NpcId,
                    "Order #" + order.Id + " rejected: duplicate_or_out_of_sequence."));
                return false;
            }
            NpcState npc;
            world.Entities.TryGetNpc(order.NpcId, out npc);
            long value = (long)order.Quantity * order.UnitPrice;
            string reason = npc == null ? "npc_missing" : npc.Survival.Life != LifeState.Active ? "npc_unavailable" :
                !ValidItem(order.Item) || (order.Side != TradeSide.Buy && order.Side != TradeSide.Sell) ||
                order.Quantity <= 0 || order.UnitPrice <= 0 ? "invalid_order" :
                npc.Position != world.Resources.StockpilePosition ? "not_at_market" :
                order.UnitPrice != Price(order.Item) ? "price_changed" : "filled";
            if (reason == "filled")
            {
                if (order.Side == TradeSide.Buy)
                {
                    reason = Inventory.Count(order.Item) < order.Quantity ? "market_stock_short" :
                        npc.Inventory.Capacity - npc.Inventory.TotalCount < order.Quantity ? "bag_full" :
                        npc.Coins < value ? "insufficient_coins" : Treasury > long.MaxValue - value ? "currency_overflow" : "filled";
                }
                else
                {
                    reason = npc.Inventory.Count(order.Item) < order.Quantity ? "seller_stock_short" :
                        Inventory.Capacity - Inventory.TotalCount < order.Quantity ? "market_full" :
                        Treasury < value ? "treasury_short" : npc.Coins > long.MaxValue - value ? "currency_overflow" : "filled";
                }
            }
            var record = new TradeRecord(order, world.Tick, reason, npc != null ? npc.Coins : 0, Treasury,
                npc != null ? npc.Inventory.Count(order.Item) : 0, Inventory.Count(order.Item));
            if (record.Filled)
            {
                InventoryState source = order.Side == TradeSide.Buy ? Inventory : npc.Inventory;
                InventoryState target = order.Side == TradeSide.Buy ? npc.Inventory : Inventory;
                int farmDelivery = order.Side == TradeSide.Sell && order.Item == ItemKind.Food ? Math.Min(order.Quantity, source.UndeliveredFarmFood) : 0;
                source.TryRemove(order.Item, order.Quantity);
                world.Survival.FarmFoodDelivered += farmDelivery;
                target.TryAdd(order.Item, order.Quantity);
                npc.Coins = record.WalletAfter;
                Treasury = record.TreasuryAfter;
                FilledCount++;
                Volume += value;
                if (order.Side == TradeSide.Buy)
                {
                    _bought[(int)order.Item] += order.Quantity;
                    npc.Purchases++;
                }
                else
                {
                    _sold[(int)order.Item] += order.Quantity;
                    npc.Sales++;
                }
            }
            else
            {
                RejectedCount++;
            }
            _ledger.Add(record);
            NextOrderId++;
            // Records and orders are immutable; cache the hash of the append-only history.
            Mix(order.Id); Mix(order.NpcId.Value); Mix((int)order.Side); Mix((int)order.Item);
            Mix(order.Quantity); Mix(order.UnitPrice); Mix(world.Tick); Mix(record.WalletBefore);
            Mix(record.TreasuryBefore); Mix(record.BagBefore); Mix(record.MarketBefore);
            Mix(reason.Length);
            foreach (char letter in reason)
            {
                Mix(letter);
            }
            events.Add(new SimEvent(record.Filled ? SimEventKind.TradeFilled : SimEventKind.TradeRejected,
                world.Tick, order.NpcId, "Order #" + order.Id + " " + order.Side + " " + order.Quantity + " " +
                order.Item + " @" + order.UnitPrice + ": " + reason + "."));
            return record.Filled;
        }

        private void Mix(long value)
        {
            unchecked
            {
                LedgerHash = (LedgerHash ^ (ulong)value) * 1099511628211UL;
            }
        }

        private static bool ValidItem(ItemKind item) => item >= ItemKind.Food && item <= ItemKind.SpinachSeed;
    }
}

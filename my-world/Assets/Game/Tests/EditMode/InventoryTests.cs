using MyAiWorld.Sim.World;
using NUnit.Framework;

namespace MyAiWorld.Tests.EditMode
{
    public sealed class InventoryTests
    {
        [Test]
        public void Inventory_MixedItemsShareCapacity_AndFailedAddsAreAtomic()
        {
            var inventory = new InventoryState(5);

            Assert.That(inventory.TryAdd(ItemKind.Food, 3), Is.True);
            Assert.That(inventory.TryAdd(ItemKind.Water, 2), Is.True);
            Assert.That(inventory.TotalCount, Is.EqualTo(5));
            Assert.That(inventory.TryAdd(ItemKind.SpinachSeed, 1), Is.False);
            Assert.That(inventory.TryAdd(ItemKind.Food, int.MaxValue), Is.False);

            Assert.That(inventory.Food, Is.EqualTo(3));
            Assert.That(inventory.Water, Is.EqualTo(2));
            Assert.That(inventory.Seeds, Is.Zero);
            Assert.That(inventory.TotalCount, Is.EqualTo(inventory.Capacity));
        }

        [Test]
        public void Inventory_InsufficientRemovalDoesNotConsumePartialStock()
        {
            var inventory = new InventoryState(10);
            inventory.TryAdd(ItemKind.Food, 4);
            inventory.TryAdd(ItemKind.SpinachSeed, 2);

            Assert.That(inventory.TryRemove(ItemKind.Food, 5), Is.False);
            Assert.That(inventory.TryRemove(ItemKind.Water, 1), Is.False);
            Assert.That(inventory.Food, Is.EqualTo(4));
            Assert.That(inventory.Seeds, Is.EqualTo(2));
            Assert.That(inventory.TotalCount, Is.EqualTo(6));

            Assert.That(inventory.TryRemove(ItemKind.Food, 4), Is.True);
            Assert.That(inventory.Food, Is.Zero);
            Assert.That(inventory.TotalCount, Is.EqualTo(2));
        }

        [Test]
        public void Inventory_InvalidQuantitiesCannotCreateOrDestroyResources()
        {
            var inventory = new InventoryState(10);
            inventory.TryAdd(ItemKind.Food, 4);

            Assert.That(inventory.TryAdd(ItemKind.Food, -1), Is.False);
            Assert.That(inventory.TryRemove(ItemKind.Food, -1), Is.False);
            Assert.That(inventory.TryRemove(ItemKind.Food, int.MaxValue), Is.False);
            Assert.That(inventory.Food, Is.EqualTo(4));
            Assert.That(inventory.TotalCount, Is.EqualTo(4));
        }

        [Test]
        public void Inventory_RepeatedMixedOperationsPreserveNonnegativeBoundedTotals()
        {
            var inventory = new InventoryState(17);
            int expectedFood = 0;
            int expectedWater = 0;
            int expectedSeed = 0;
            ItemKind[] items = { ItemKind.Food, ItemKind.Water, ItemKind.SpinachSeed };

            for (int operation = 0; operation < 1000; operation++)
            {
                ItemKind item = items[operation % items.Length];
                int quantity = (operation * 7 % 11) + 1;
                bool add = operation % 5 < 3;
                int before = inventory.Count(item);
                bool changed = add
                    ? inventory.TryAdd(item, quantity)
                    : inventory.TryRemove(item, quantity);

                int delta = changed ? (add ? quantity : -quantity) : 0;
                switch (item)
                {
                    case ItemKind.Food: expectedFood += delta; break;
                    case ItemKind.Water: expectedWater += delta; break;
                    case ItemKind.SpinachSeed: expectedSeed += delta; break;
                }

                Assert.That(inventory.Count(item), Is.EqualTo(before + delta));
                Assert.That(inventory.Food, Is.EqualTo(expectedFood));
                Assert.That(inventory.Water, Is.EqualTo(expectedWater));
                Assert.That(inventory.Seeds, Is.EqualTo(expectedSeed));
                Assert.That(inventory.TotalCount, Is.EqualTo(expectedFood + expectedWater + expectedSeed));
                Assert.That(inventory.TotalCount, Is.InRange(0, inventory.Capacity));
                Assert.That(inventory.Food, Is.GreaterThanOrEqualTo(0));
                Assert.That(inventory.Water, Is.GreaterThanOrEqualTo(0));
                Assert.That(inventory.Seeds, Is.GreaterThanOrEqualTo(0));
            }
        }
    }
}

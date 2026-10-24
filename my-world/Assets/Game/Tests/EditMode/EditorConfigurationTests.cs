using System;
using MyAiWorld.App;
using MyAiWorld.Sim.World;
using NUnit.Framework;
using UnityEngine;

namespace MyAiWorld.Tests.EditMode
{
    [Category("EditorIntegration")]
    public sealed class EditorConfigurationTests
    {
        [Test]
        public void ConfigAsset_UsesSerializedStartAndDurationAndRejectsEmptyChallenge()
        {
            var asset = ScriptableObject.CreateInstance<WorldConfigAsset>();
            try
            {
                Assert.That(asset.ToData().StartTick, Is.EqualTo(GameCalendar.WinterStartTick));
                Assert.That(asset.ToData().ChallengeDays, Is.EqualTo(GameCalendar.DefaultChallengeDays));
                asset.startTick = GameCalendar.StartTick + 42;
                asset.challengeDays = 0;
                asset.npcCount = 0;
                Assert.That(asset.ToData().StartTick, Is.EqualTo(asset.startTick));
                Assert.That(asset.ToData().ChallengeDays, Is.Zero);
                asset.challengeDays = 14;
                Assert.Throws<ArgumentException>(() => asset.ToData());
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(asset);
            }
        }
    }
}

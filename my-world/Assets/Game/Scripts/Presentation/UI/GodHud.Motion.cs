using System.Collections.Generic;
using MyAiWorld.App.Query;
using MyAiWorld.Sim.Core;
using MyAiWorld.Sim.World;
using UnityEngine;
using UnityEngine.UI;

namespace MyAiWorld.Presentation.UI
{
    public sealed partial class GodHud
    {
        private readonly Dictionary<string, long> _motionValues = new Dictionary<string, long>();
        private readonly float[] _supplyFeedbackAt = { -1f, -1f, -1f };
        private readonly bool[] _exhausted = new bool[3];
        private readonly HudPulse[] _supplyFeedback = new HudPulse[3];
        private readonly HudPulse[] _marketFeedback = new HudPulse[3];
        private readonly HudPulse[] _dayFeedback = new HudPulse[14];
        private HudPulse _environmentFeedback, _ecologyFeedback, _summaryFeedback, _inspectorFeedback;
        private HudPulse _rabbitFeedback, _boarFeedback, _birthFeedback, _deathFeedback, _huntFeedback, _tradeFeedback, _treasuryFeedback;
        private HudWeatherMotion _weatherMotion;
        private HudEntrance _settlementBackdrop;
        private PanelTransition _settlementTransition;
        private Button _motionToggle;
        private int _motionRevision = -1;
        private bool _motionReady;
        private EntityId? _motionNpc;
        private float _ecologyFeedbackAt = -HudMotion.EcologyCooldown;
        public bool ReducedMotion => HudMotion.Reduced;

        public void SetReducedMotion(bool reduced)
        {
            HudMotion.Reduced = reduced;
            RefreshMotionPreference();
        }

        private void RefreshMotionPreference()
        {
            if (_motionToggle == null) { return; }
            _motionToggle.GetComponentInChildren<Text>().text = "减少动态：" + (ReducedMotion ? "开" : "关");
            HudTheme.Select(_motionToggle, ReducedMotion);
        }

        private void PrepareMotionFrame()
        {
            if (_motionRevision == _session.WorldRevision) { return; }
            _motionRevision = _session.WorldRevision;
            _motionReady = false;
            _motionNpc = null;
            _motionValues.Clear();
            _ecologyFeedbackAt = -HudMotion.EcologyCooldown;
            for (int i = 0; i < 3; i++) { _supplyFeedbackAt[i] = -1f; _exhausted[i] = false; }
            foreach (var pulse in GetComponentsInChildren<HudPulse>(true)) { pulse.ResetFeedback(); }
            foreach (var meter in GetComponentsInChildren<HudMeterMotion>(true)) { meter.Complete(); }
            foreach (var transition in GetComponentsInChildren<PanelTransition>(true)) { transition.Complete(); }
            _ecology.ResetFeedback();
            _ledger.ResetFeedback();
            _weatherMotion.Complete();
        }

        private int Observe(string key, long value)
        {
            bool known = _motionValues.TryGetValue(key, out long previous);
            _motionValues[key] = value;
            return _motionReady && known ? value.CompareTo(previous) : 0;
        }

        private void RefreshInstrumentMotion(CampaignSnapshot campaign, EnvironmentSnapshot environment, int completed)
        {
            int dayChange = Observe("day", completed);
            if (dayChange > 0 && !campaign.IsUnlimited && completed > 0)
            {
                _dayFeedback[Mathf.Min(13, completed - 1)].Play(HudTheme.Gold, HudMotion.DayDuration);
            }
            int weather = Observe("weather", (long)environment.Weather);
            int frozen = Observe("frozen", environment.WaterFrozen ? 1 : 0);
            if ((weather != 0 && environment.Weather == WeatherKind.Blizzard) || frozen > 0)
            {
                _environmentFeedback.Play(HudTheme.Ink, HudMotion.WarningDuration);
            }
            var sprite = HudIcons.Get(environment.Weather == WeatherKind.Snow || environment.Weather == WeatherKind.Blizzard ? "snow" :
                environment.IsDaytime ? "sun" : "moon");
            _weatherMotion.Bind(sprite, !_motionReady);
            SupplyEstimateSnapshot[] supplies = { environment.Food, environment.Water, environment.Fuel };
            for (int i = 0; i < 3; i++)
            {
                var supply = supplies[i];
                int change = Observe("supply" + i, supply.Reserve);
                bool statusChanged = Observe("supplyStatus" + i, (long)supply.Status) != 0;
                _exhausted[i] = supply.Status == SupplyStatus.Exhausted;
                bool alert = statusChanged && (supply.Status == SupplyStatus.Tight || _exhausted[i]);
                if (alert || (change != 0 && Time.unscaledTime >= _supplyFeedbackAt[i]))
                {
                    _supplyFeedback[i].Play(alert ? (_exhausted[i] ? HudTheme.Danger : HudTheme.Gold) :
                        change > 0 ? HudTheme.Positive : HudTheme.Gold, alert ? HudMotion.WarningDuration : HudMotion.ValueDuration);
                    _supplyFeedbackAt[i] = Time.unscaledTime + HudMotion.SupplyCooldown;
                }
            }
        }

        private void RefreshNpcMotion(NpcDetails details)
        {
            bool sameNpc = _motionNpc == details.Id && _motionReady;
            _motionNpc = details.Id;
            long state = (long)details.Life * 2 + (details.DiseaseTicks > 0 ? 1 : 0);
            int change = Observe("npcState", state);
            if (!sameNpc)
            {
                _summaryFeedback.ResetFeedback(); _inspectorFeedback.ResetFeedback();
            }
            else if (change != 0)
            {
                Color accent = details.Life != LifeState.Active || details.DiseaseTicks > 0 ? HudTheme.Danger : HudTheme.Positive;
                if (NpcSummaryOpen) { _summaryFeedback.Play(accent, 0.36f); }
                if (InspectorOpen) { _inspectorFeedback.Play(accent, 0.36f); }
            }
            int[] values = { details.Food, details.Water, details.Energy, details.Health, details.Safety };
            for (int i = 0; i < values.Length; i++)
            {
                HudLayout.SetMeter(_needFills[i], values[i] / 10000f, !sameNpc);
                HudLayout.SetMeter(_summaryFills[i], values[i] / 10000f, !sameNpc);
            }
        }

        private bool AnimateEcology => _motionReady && OpenGlobalPanel == 1;

        private bool ObserveEcology(string key, long value, HudPulse pulse, Color? accent = null)
        {
            int change = Observe(key, value);
            if (change == 0) { return false; }
            if (AnimateEcology && pulse != null)
            {
                pulse.Play(accent ?? (change > 0 ? HudTheme.Positive : HudTheme.Danger), HudMotion.EcologyDuration);
            }
            return true;
        }

        private void RefreshEconomyMotion(EcologyEconomySnapshot economy)
        {
            bool changed = ObserveEcology("rabbits", economy.Rabbits, _rabbitFeedback);
            changed |= ObserveEcology("boars", economy.Boars, _boarFeedback);
            changed |= ObserveEcology("births", economy.Births, _birthFeedback, HudTheme.Positive);
            changed |= ObserveEcology("naturalDeaths", economy.NaturalDeaths, _deathFeedback, HudTheme.Danger);
            changed |= ObserveEcology("predationDeaths", economy.PredationDeaths, null, HudTheme.Danger);
            changed |= ObserveEcology("hunts", economy.HuntKills, _huntFeedback, HudTheme.Gold);
            changed |= ObserveEcology("departures", economy.Departures, null, HudTheme.Gold);
            changed |= ObserveEcology("returns", economy.Returns, null, HudTheme.Gold);
            foreach (var species in economy.Species) { changed |= ObserveEcology("species" + species.Species, species.Count, null); }
            changed |= ObserveEcology("filled", economy.Filled, _tradeFeedback, HudTheme.Gold);
            changed |= ObserveEcology("rejected", economy.Rejected, _tradeFeedback, HudTheme.Gold);
            ObserveEcology("treasury", economy.Treasury, _treasuryFeedback, HudTheme.Gold);
            for (int i = 0; i < 3; i++)
            {
                bool row = Observe("price" + i, economy.Prices[i]) != 0;
                row |= Observe("stock" + i, economy.Stocks[i]) != 0;
                row |= Observe("bought" + i, economy.Bought[i]) != 0;
                row |= Observe("sold" + i, economy.Sold[i]) != 0;
                if (row && AnimateEcology) { _marketFeedback[i].Play(HudTheme.Gold, HudMotion.EcologyDuration); }
            }
            if (changed && OpenGlobalPanel != 1 && Time.unscaledTime >= _ecologyFeedbackAt)
            {
                _ecologyFeedback.Play(HudTheme.Gold, HudMotion.WarningDuration);
                _ecologyFeedbackAt = Time.unscaledTime + HudMotion.EcologyCooldown;
            }
        }

        private void LateUpdate()
        {
            if (!_initialized) { return; }
            float alpha = HudMotion.Reduced ? 1f : 0.91f + 0.09f * Mathf.Cos(Time.unscaledTime * Mathf.PI * 2f / HudMotion.ExhaustedPeriod);
            for (int i = 0; i < 3; i++)
            {
                if (!_exhausted[i]) { continue; }
                foreach (var mark in _supplyMarks[i])
                {
                    if (mark.color.a == 0f) { continue; }
                    var tint = mark.color; tint.a = alpha; mark.color = tint;
                }
            }
        }

        private static HudPulse Feedback(Text value, string name) => HudPulse.Attach(value.transform.parent, name);

        private void SetCampaignModal(bool open)
        {
            bool opening = open && !_campaignModal.activeSelf;
            _campaignModal.SetActive(open);
            if (!opening) { return; }
            // The overlay is active before animation, so the world is blocked immediately.
            _settlementBackdrop.Begin(0f, HudMotion.BackdropDuration);
            _settlementTransition.SetOpen(false, true);
            _settlementTransition.SetOpen(true);
        }
    }
}

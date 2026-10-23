using System;
using MyAiWorld.Sim.World;
using UnityEngine;

namespace MyAiWorld.App
{
    public sealed class SimulationRunner : MonoBehaviour
    {
        private const int MaxCatchUpTicks = 8;

        private GameSession _session;
        private double _accumulator;
        private int[] _speedMultipliers = { 1, 2, 4, 8 };
        private int _speedMultiplier = 1;
        private bool _initialized;
        private int _worldRevision;

        public GameSession Session => _session;
        public float TickAlpha { get; private set; }
        public bool IsPaused { get; private set; }
        public float AnimationTime { get; private set; }
        public float WorldDeltaTime { get; private set; }
        public int SpeedMultiplier => _speedMultiplier;
        public int[] SpeedMultipliers => (int[])_speedMultipliers.Clone();
        public string SpeedText => IsPaused ? "PAUSED" : "x" + _speedMultiplier;

        public void Initialize(GameSession session, WorldConfigData config)
        {
            if (session == null)
            {
                throw new ArgumentNullException(nameof(session));
            }

            _session = session;
            _worldRevision = session.WorldRevision;
            _speedMultipliers = config != null ? config.SpeedMultipliers : new[] { 1, 2, 4, 8 };
            _speedMultiplier = _speedMultipliers.Length > 0 ? _speedMultipliers[0] : 1;
            IsPaused = !session.World.Campaign.CanAdvance;
            _accumulator = 0.0;
            TickAlpha = IsPaused ? 1f : 0f;
            _initialized = true;
        }

        public bool Load(string path = null)
        {
            if (!_session.Load(path)) { return false; }
            ResetAfterLoad();
            return true;
        }

        private void ResetAfterLoad()
        {
            _worldRevision = _session.WorldRevision;
            _speedMultipliers = _session.Config.SpeedMultipliers;
            _speedMultiplier = _speedMultipliers[0];
            _accumulator = 0; TickAlpha = 1f; AnimationTime = 0; WorldDeltaTime = 0;
            IsPaused = true;
        }

        public void Pause()
        {
            IsPaused = true;
        }

        public void Resume()
        {
            IsPaused = _session != null && !_session.World.Campaign.CanAdvance;
        }

        public bool ContinueObservation()
        {
            if (!_initialized || _session.World.Campaign.Phase != CampaignPhase.SettlementLocked) { return false; }
            _session.EnqueueCommand(new MyAiWorld.Sim.Systems.ContinueObservationCommand());
            _session.Tick();
            _accumulator = 0;
            TickAlpha = 1f;
            Resume();
            return !IsPaused;
        }

        public void TogglePause()
        {
            if (IsPaused)
            {
                Resume();
            }
            else
            {
                Pause();
            }
        }

        public void SetSpeed(int multiplier)
        {
            for (int i = 0; i < _speedMultipliers.Length; i++)
            {
                if (_speedMultipliers[i] == multiplier)
                {
                    _speedMultiplier = multiplier;
                    Resume();
                    return;
                }
            }
        }

        public void StepOnce()
        {
            if (!_initialized || !_session.World.Campaign.CanAdvance)
            {
                return;
            }

            _session.Tick();
            TickAlpha = 1f;
            _accumulator = 0.0;
            if (!_session.World.Campaign.CanAdvance) { Pause(); }
        }

        private void Update()
        {
            if (!_initialized || _session == null)
            {
                return;
            }

            if (_worldRevision != _session.WorldRevision) { ResetAfterLoad(); }
            if (!_session.World.Campaign.CanAdvance) { Pause(); }
            WorldDeltaTime = IsPaused ? 0f : Mathf.Min(Time.unscaledDeltaTime, 0.25f) * _speedMultiplier;
            AnimationTime += WorldDeltaTime;
            if (IsPaused)
            {
                return;
            }

            double baseTicksPerSecond = _session.Config.TicksPerSecondAt1x;
            float deltaTime = Mathf.Min(Time.unscaledDeltaTime, 0.25f);
            _accumulator += deltaTime * baseTicksPerSecond * _speedMultiplier;

            int steps = 0;
            while (_accumulator >= 1.0 && steps < MaxCatchUpTicks)
            {
                _session.Tick();
                _accumulator -= 1.0;
                steps++;
                if (!_session.World.Campaign.CanAdvance)
                {
                    Pause();
                    _accumulator = 0;
                    TickAlpha = 1f;
                    return;
                }
            }

            if (_accumulator > MaxCatchUpTicks)
            {
                _accumulator = MaxCatchUpTicks;
            }

            TickAlpha = Mathf.Clamp01((float)_accumulator);
        }
    }
}

using System.Collections.Generic;
using System.Text;
using MyAiWorld.App.Query;
using MyAiWorld.Sim.Core;
using MyAiWorld.Sim.Systems;
using MyAiWorld.Sim.World;
using UnityEngine;

namespace MyAiWorld.Presentation.UI
{
    public sealed partial class GodHud
    {
        private UnityEngine.UI.Text _huntDescription, _huntOrderFeedback;
        private UnityEngine.UI.Button _autoHunt, _requestTeam, _cancelHunt, _chooseTeam;
        private RectTransform _huntRoster;
        private readonly List<UnityEngine.UI.Button> _huntCandidateButtons = new List<UnityEngine.UI.Button>();
        private readonly List<EntityId> _chosenHunters = new List<EntityId>();
        private bool _choosingHunters;
        private long _huntCommandTick = -1;
        private EntityId? _huntDraftTarget;
        public string HuntingText => _huntDescription != null ? _huntDescription.text : "";
        public UnityEngine.UI.Button RequestHuntButton => _autoHunt;
        public UnityEngine.UI.Button CancelHuntButton => _cancelHunt;
        private void BuildHunting(Transform parent)
        {
            HudLayout.Section(parent, "狩猎与协作");
            _huntDescription = HudLayout.Label(parent, "HuntingStatus", "点击猎物，标记狩猎目标。", 13);
            _huntOrderFeedback = HudLayout.Label(parent, "HuntingFeedback", "", 12, HudTheme.Gold);
            var actions = HudLayout.Row(parent, "HuntingActions", 32, 6);
            _autoHunt = HudLayout.Button(actions, "AutoHunt", "自动组队", () => RequestSelectedHunt(false));
            _chooseTeam = HudLayout.Button(actions, "ChooseHuntTeam", "指定队员", () =>
            {
                _choosingHunters = !_choosingHunters;
                RefreshHunting();
            }

            );
            _huntRoster = HudLayout.Column(parent, "HuntingRoster", 4);
            _requestTeam = HudLayout.Button(parent, "RequestHuntTeam", "派出已选队员", () => RequestSelectedHunt(true));
            _cancelHunt = HudLayout.Button(parent, "CancelHunt", "停止追猎 · 全队撤退", CancelSelectedHunt);
        }

        private void OnAnimalSelected(EntityId? animal)
        {
            _chosenHunters.Clear();
            _choosingHunters = false;
            _huntDraftTarget = animal;
            if (animal.HasValue)
            {
                _cameraController.StopFollowing();
                if (OpenGlobalPanel != 1)
                {
                    ToggleGlobalPanel(1);
                }
            }

            RefreshHunting();
        }

        public void RequestSelectedHunt(bool specified)
        {
            if (!_selection.SelectedAnimal.HasValue || CampaignLocked)
            {
                return;
            }

            if (specified && _chosenHunters.Count == 0)
            {
                _huntOrderFeedback.text = "请先选择一至三名队员";
                return;
            }

            _session.EnqueueCommand(new RequestHuntCommand(_selection.SelectedAnimal.Value, specified ? new List<EntityId>(_chosenHunters) : null));
            _huntCommandTick = _session.LastTick;
            _huntOrderFeedback.text = "指令待执行 · 恢复或单步后生效";
            _choosingHunters = false;
            RefreshHunting();
        }

        public void CancelSelectedHunt()
        {
            HuntTaskSnapshot task = SelectedHunt();
            if (task == null || CampaignLocked)
            {
                return;
            }

            _session.EnqueueCommand(new CancelHuntCommand(task.Id));
            _huntCommandTick = _session.LastTick;
            _huntOrderFeedback.text = "撤退指令待执行";
        }

        private HuntTaskSnapshot SelectedHunt() => _selection.SelectedAnimal.HasValue ? _session.ReadModel.Hunting.ForTarget(_selection.SelectedAnimal.Value) : _selected.HasValue ? _session.ReadModel.Hunting.ForNpc(_selected.Value) : null;
        private void RefreshHunting()
        {
            if (_huntDescription == null || _session.ReadModel.Hunting == null)
            {
                return;
            }

            if (_huntDraftTarget != _selection.SelectedAnimal)
            {
                _huntDraftTarget = _selection.SelectedAnimal;
                _chosenHunters.Clear();
                _choosingHunters = false;
            }

            AnimalSnapshot target = null;
            foreach (AnimalSnapshot animal in _session.ReadModel.EcologyEconomy.Animals)
            {
                if (_selection.SelectedAnimal.HasValue && animal.Id == _selection.SelectedAnimal.Value)
                {
                    target = animal;
                    break;
                }
            }

            HuntTaskSnapshot task = SelectedHunt();
            var text = new StringBuilder();
            if (target != null)
            {
                text.Append(ChineseText.Species(target.Species)).Append(" #").Append(target.Id.Value).Append(" · 生命 ").Append(target.Health).Append("\n").Append(EncounterText(target.Encounter)).Append(" · 奔逃耐力 ").Append(target.Stamina / 100).Append('%').Append("\n建议 ").Append(HuntingRules.RequiredMembers(target.Species)).Append(" 人").Append(HuntingRules.NeedsSpear(target.Species) ? " · 需要持矛接应" : " · 注意射路");
            }

            if (task != null)
            {
                text.Append("\n狩猎队 #").Append(task.Id).Append(" · ").Append(StageText(task.Stage));
                foreach (var member in task.Members)
                {
                    text.Append("\n").Append(NpcName(member.NpcId)).Append(" · ").Append(RoleText(member.Role));
                }

                if (!string.IsNullOrEmpty(task.Reason))
                {
                    text.Append("\n").Append(task.Reason);
                }
            }
            else if (target == null)
            {
                text.Append("点击猎物可标记目标；点击村民可查看所属队伍。\n当前狩猎队：").Append(_session.ReadModel.Hunting.Tasks.Count);
            }

            _huntDescription.text = text.ToString();
            if (_huntCommandTick >= 0 && _session.LastTick > _huntCommandTick)
            {
                _huntCommandTick = -1;
                _huntOrderFeedback.text = task != null ? "狩猎指令已处理" : "无法组队，请检查人数、距离、装备与需求";
                foreach (EventSnapshot evt in _session.ReadModel.RecentEvents)
                {
                    if (evt.Kind == SimEventKind.CommandRejected && evt.Tick >= _session.LastTick - 2)
                    {
                        _huntOrderFeedback.text = evt.Message;
                    }
                }
            }

            bool canRequest = target != null && target.Health > 0 && target.Region == -1 && task == null && !CampaignLocked;
            _autoHunt.interactable = _chooseTeam.interactable = canRequest && _huntCommandTick < 0;
            _huntRoster.gameObject.SetActive(canRequest && _choosingHunters);
            _requestTeam.gameObject.SetActive(canRequest && _choosingHunters);
            _requestTeam.interactable = _chosenHunters.Count > 0 && _huntCommandTick < 0;
            _cancelHunt.gameObject.SetActive(task != null);
            _cancelHunt.interactable = task != null && task.Stage != HuntStage.Retreating && !CampaignLocked && _huntCommandTick < 0;
            if (!_choosingHunters)
            {
                return;
            }

            int i = 0;
            foreach (HuntCandidateSnapshot candidate in _session.ReadModel.Hunting.Candidates)
            {
                EntityId id = candidate.Id;
                if (i == _huntCandidateButtons.Count)
                {
                    _huntCandidateButtons.Add(HudLayout.Button(_huntRoster, "HuntCandidate" + i, "", () =>
                    {
                    }

                    ));
                }

                var button = _huntCandidateButtons[i++];
                button.gameObject.SetActive(true);
                button.onClick.RemoveAllListeners();
                button.onClick.AddListener(() =>
                {
                    if (_chosenHunters.Contains(id))
                    {
                        _chosenHunters.Remove(id);
                    }
                    else if (_chosenHunters.Count < HuntingRules.MaxMembers)
                    {
                        _chosenHunters.Add(id);
                    }

                    RefreshHunting();
                }

                );
                button.GetComponentInChildren<UnityEngine.UI.Text>().text = (_chosenHunters.Contains(id) ? "✓ " : "＋ ") + candidate.Name + " · " + (candidate.HasSpear ? "矛" : "弓") + " · 技能 " + candidate.Skill;
            }

            for (; i < _huntCandidateButtons.Count; i++)
            {
                _huntCandidateButtons[i].gameObject.SetActive(false);
            }
        }

        public static string StageText(HuntStage stage) => new[]{"集结准备", "接近围堵", "交战", "回收猎获", "返回村庄", "掩护撤退"}[(int)stage];
        public static string RoleText(HuntRole role) => new[]{"弓手", "持矛接应", "侧翼截击"}[(int)role];
        private static string EncounterText(EncounterPhase phase) => new[]{"平静", "警觉", "逃跑", "反击", "脱战恢复"}[(int)phase];
    }
}

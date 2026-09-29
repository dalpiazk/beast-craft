using System;
using System.Collections.Generic;
using BeastCraft.Campaign;
using BeastCraft.Creatures;
using BeastCraft.Economy;
using BeastCraft.Progression;
using BeastCraft.Save;
using BeastCraft.Tutorial;

namespace BeastCraft.Presentation.Screens
{
    /// <summary>One beast a picker offers.</summary>
    public sealed class PickOptionView
    {
        public string SpeciesId;
        public string Name;
        public CombatStance Stance;
        public Element Element;

        /// <summary>The personality blurb (the roster's Description).</summary>
        public string Blurb;

        /// <summary>The illustrated art key (the species' ArtKey).</summary>
        public string ArtKey;
    }

    /// <summary>What a pick is for: the New Game pick (Hearthglen), a trial's pick, or the skip-tutorial run of three.</summary>
    public enum PickMode
    {
        NewGame,
        Trial,
        Skip
    }

    /// <summary>
    /// The beast pickers: the <c>StarterPickScreen</c> (the New Game pick of all ten, or the skip's three
    /// picks in a row) and a trial's popup (<see cref="PickMode.Trial"/>: the pending pick). Lists the legal
    /// options for the current step (<see cref="StarterPicks.Options"/>) with their stance, element, art and
    /// blurb, and the stance explainer; <see cref="Choose"/> makes the pick through the session.
    /// </summary>
    public sealed class StarterPickViewModel : IBeastPicker
    {
        /// <summary>The stances, what each does in a fight (the pickers' explainer), as text keys: "Stance: what it does".</summary>
        public static readonly string[] StanceExplainerKeys =
        {
            "ui.tutorial.stance_vanguard",
            "ui.tutorial.stance_ranged",
            "ui.tutorial.stance_skirmisher"
        };

        private readonly GameSession _session;
        private readonly List<string> _picked = new List<string>();

        public StarterPickViewModel(GameSession session, PickMode mode)
        {
            _session = session ?? throw new ArgumentNullException(nameof(session));
            Mode = mode;
            if (mode == PickMode.Trial)
            {
                _picked.AddRange(StarterPicks.Picked(session.Save));
            }

            Refresh();
        }

        public PickMode Mode { get; }

        /// <summary>The pick being made, 1-3.</summary>
        public int Step
        {
            get { return _picked.Count + 1; }
        }

        public List<PickOptionView> Options { get; } = new List<PickOptionView>();

        /// <summary>The species picked so far this run (the skip's), in order.</summary>
        public IReadOnlyList<string> Picked
        {
            get { return _picked; }
        }

        /// <summary>The stance this pick must be, or null (the first pick: any).</summary>
        public CombatStance? RequiredStance { get; private set; }

        public string Title
        {
            get
            {
                if (Mode == PickMode.Trial)
                {
                    return _session.Content.Text.Get(Step == 2 ? "ui.tutorial.second_beast" : "ui.tutorial.third_beast");
                }

                return Mode == PickMode.Skip ? _session.Content.Text.Format("ui.tutorial.choose_step", Step, StarterPicks.PickCount) : _session.Content.Text.Get("ui.tutorial.choose_first_beast");
            }
        }

        public string Subtitle
        {
            get
            {
                if (RequiredStance == null)
                {
                    return _session.Content.Text.Get("ui.tutorial.any_of_ten");
                }

                return _session.Content.Text.Format(Step == 2 ? "ui.tutorial.stance_after_first" : "ui.tutorial.stance_after_second", RequiredStance.Value);
            }
        }

        /// <summary>
        /// Picks <paramref name="speciesId"/>: New Game starts Hearthglen with it; a trial's pick joins the
        /// team; the skip collects three, then starts the game in the first campaign region. Returns
        /// true once the game has what it needs (the caller moves on); false with
        /// <paramref name="error"/> when refused, or (skip) while picks remain.
        /// </summary>
        public bool Choose(string speciesId, out string error)
        {
            error = null;
            if (!StarterPicks.IsLegal(_picked, speciesId, _session.Content.Species, out error))
            {
                return false;
            }

            switch (Mode)
            {
                case PickMode.NewGame:
                    return _session.NewGame(speciesId);
                case PickMode.Trial:
                    PickResult result = _session.Pick(speciesId);
                    error = result.Error;
                    return result.Success;
                default:
                    _picked.Add(speciesId);
                    if (_picked.Count < StarterPicks.PickCount)
                    {
                        Refresh();
                        return false;
                    }

                    if (!_session.NewGameSkippingTutorial(_picked))
                    {
                        error = _session.Content.Text.Get("ui.tutorial.picks_failed");
                        _picked.Clear();
                        Refresh();
                        return false;
                    }

                    return true;
            }
        }

        /// <summary>The toast once a trial's pick has joined.</summary>
        public string JoinedMessage(PickOptionView option)
        {
            return _session.Content.Text.Format("ui.discovery.joined", option?.Name ?? _session.Content.Text.Get("ui.discovery.a_beast"), 1);
        }

        /// <summary>A species as a picker's option.</summary>
        public static PickOptionView OptionOf(CreatureSpeciesSO species)
        {
            return new PickOptionView
            {
                SpeciesId = species.SpeciesId,
                Name = species.DisplayName ?? species.SpeciesId,
                Stance = species.Stance,
                Element = species.Elements != null && species.Elements.Length > 0 ? species.Elements[0] : Element.None,
                Blurb = species.Description,
                ArtKey = species.ArtKey
            };
        }

        /// <summary>The skip's Back: undo the last pick (false when none was made).</summary>
        public bool Undo()
        {
            if (Mode != PickMode.Skip || _picked.Count == 0)
            {
                return false;
            }

            _picked.RemoveAt(_picked.Count - 1);
            Refresh();
            return true;
        }

        private void Refresh()
        {
            Options.Clear();
            List<CreatureSpeciesSO> roster = _session.Content.Species;
            CreatureSpeciesSO first = _picked.Count == 0 ? null : roster.Find(s => s.SpeciesId == _picked[0]);
            RequiredStance = first == null ? (CombatStance?)null : StarterPicks.RequiredStance(Step, first.Stance);
            foreach (CreatureSpeciesSO species in StarterPicks.Options(_picked, roster))
            {
                Options.Add(OptionOf(species));
            }
        }
    }

    /// <summary>
    /// A story location (<see cref="MapNodeType.Story"/>): its mentor scene line by line (the dialogue
    /// box), then <see cref="Complete"/>: <see cref="CampaignRules.Visit"/> (its gifts; the last one
    /// completes Hearthglen), saved.
    /// </summary>
    public sealed class StoryViewModel
    {
        private readonly GameSession _session;

        public StoryViewModel(GameSession session, int nodeId, string sceneId)
        {
            _session = session;
            NodeId = nodeId;
            DialogueSceneData scene = session.Content.Dialogue?.Scene(sceneId);
            Title = scene?.Title ?? string.Empty;
            foreach (DialogueLineData line in session.Content.Dialogue?.SceneLines(sceneId) ?? new List<DialogueLineData>())
            {
                NpcData npc = session.Content.Dialogue.Npc(line.NpcId);
                Lines.Add(new DialogueLineView { Speaker = npc?.DisplayName ?? line.NpcId, PortraitKey = npc?.PortraitKey, Text = line.Text });
            }
        }

        public int NodeId { get; }

        public string Title { get; }

        public List<DialogueLineView> Lines { get; } = new List<DialogueLineView>();

        public int Index { get; private set; }

        public DialogueLineView Current
        {
            get { return Index < Lines.Count ? Lines[Index] : null; }
        }

        public bool IsLast
        {
            get { return Index >= Lines.Count - 1; }
        }

        /// <summary>The next line; false at the end.</summary>
        public bool Next()
        {
            if (IsLast)
            {
                return false;
            }

            Index++;
            return true;
        }

        /// <summary>Visits the location (gifts, the region's completion) and saves. The result says what happened.</summary>
        public CampaignResult Complete()
        {
            CampaignResult result = CampaignRules.Visit(_session.Save, _session.Content.Campaign, NodeId, _session.Content.Battle.GetConsumable);
            if (result.Success)
            {
                _session.Autosave(AutosaveReason.Results);
                if (result.Outcome == CampaignOutcome.TutorialCleared)
                {
                    _session.EnsureExpedition();
                    _session.Autosave(AutosaveReason.Results);
                }
            }

            return result;
        }

        /// <summary>What a visit's gifts read as ("2 Fury Draughts").</summary>
        public static string GiftText(GameSession session, CampaignResult result)
        {
            List<string> parts = new List<string>();
            foreach (ConsumableStack stack in result?.ItemsGranted ?? new List<ConsumableStack>())
            {
                string name = session.Content.Battle.GetConsumable(stack.ConsumableId)?.DisplayName ?? stack.ConsumableId;
                parts.Add(session.Content.Text.Format("ui.story.gift", stack.Quantity, name + (stack.Quantity > 1 && !name.EndsWith("s", StringComparison.Ordinal) ? "s" : string.Empty)));
            }

            return string.Join(session.Content.Text.Get("ui.common.list_sep"), parts);
        }
    }

    /// <summary>One line in the dialogue box.</summary>
    public sealed class DialogueLineView
    {
        public string Speaker;
        public string PortraitKey;
        public string Text;
    }

    /// <summary>One beast at camp.</summary>
    public sealed class CampBeastView
    {
        public string BeastId;
        public string SpeciesId;
        public string Name;
        public int Level;
        public float XpFraction;
    }

    /// <summary>
    /// A Camp location (<see cref="MapNodeType.Rest"/>), for any region: pick one beast to train
    /// (<see cref="CampaignRules.Camp"/>: a standing beast's clear XP at the camp's level; in Hearthglen
    /// the camp also catches every beast up to the leader), the idle rewards' state and claim, saved.
    /// </summary>
    public sealed class CampViewModel
    {
        private readonly GameSession _session;

        public CampViewModel(GameSession session, int nodeId)
        {
            _session = session;
            NodeId = nodeId;
            MapRun run = session.Save.Campaign.ActiveRun;
            MapNode node = run.Find(nodeId);
            Title = node == null ? session.Content.Text.Get("ui.map.kind_camp") : session.LocationName(node);
            Level = node?.Level ?? 1;
            IsTutorial = session.Content.Campaign.IsTutorial(run.RegionId);
            FixedNodeData authored = session.Content.Campaign.FixedNode(run.RegionId, nodeId);
            SceneId = authored?.SceneId;
            Refresh();
        }

        public int NodeId { get; }

        public string Title { get; }

        public int Level { get; }

        /// <summary>Whether this camp also catches every beast up to the leader (Hearthglen).</summary>
        public bool IsTutorial { get; }

        /// <summary>The mentor scene the camp opens with ("" / null: none).</summary>
        public string SceneId { get; }

        public List<CampBeastView> Beasts { get; } = new List<CampBeastView>();

        /// <summary>The beast suggested for training: the lowest level (the newest on a tie).</summary>
        public string Suggested { get; private set; }

        /// <summary>After <see cref="Train"/>: what happened, for the screen.</summary>
        public List<string> Summary { get; } = new List<string>();

        public bool Done { get; private set; }

        public string Explainer
        {
            get
            {
                return IsTutorial
                           ? _session.Content.Text.Get("ui.camp.intro_tutorial")
                           : _session.Content.Text.Get("ui.camp.intro");
            }
        }

        /// <summary>Trains <paramref name="beastId"/> and saves; false with <paramref name="error"/> when refused.</summary>
        public bool Train(string beastId, out string error)
        {
            CampaignResult result = CampaignRules.Camp(_session.Save, _session.Content.Campaign, NodeId, beastId, _session.Content.Achievements);
            error = result.Error;
            if (!result.Success)
            {
                return false;
            }

            Done = true;
            OwnedBeast beast = _session.Save.FindBeast(beastId);
            Summary.Clear();
            string extra = GameSession.ExtraRewardText(_session.Content.Text, result.TitlesEarned, 0);
            Summary.Add((result.LevelsGained > 0
                             ? _session.Content.Text.Format("ui.camp.trained_level", _session.BeastName(beast), result.XpTrained, beast.Progress.Level)
                             : _session.Content.Text.Format("ui.camp.trained", _session.BeastName(beast), result.XpTrained)) + extra);
            foreach (string id in result.CaughtUp)
            {
                OwnedBeast caught = _session.Save.FindBeast(id);
                Summary.Add(_session.Content.Text.Format("ui.camp.caught_up", _session.BeastName(caught), caught.Progress.Level));
            }

            _session.Autosave(AutosaveReason.Results);
            Refresh();
            return true;
        }

        private void Refresh()
        {
            Beasts.Clear();
            OwnedBeast lowest = null;
            foreach (OwnedBeast beast in _session.Save.Beasts)
            {
                int next = Math.Max(1, BeastProgression.XpToNextLevel(beast.Progress.Level));
                Beasts.Add(new CampBeastView
                {
                    BeastId = beast.BeastId,
                    SpeciesId = beast.Progress.SpeciesId,
                    Name = _session.BeastName(beast),
                    Level = beast.Progress.Level,
                    XpFraction = Math.Min(1f, beast.Progress.Xp / (float)next)
                });
                if (lowest == null || beast.Progress.Level <= lowest.Progress.Level)
                {
                    lowest = beast;
                }
            }

            Suggested = lowest?.BeastId;
        }
    }

    /// <summary>The session's hints: the one due for a trigger (<see cref="HintBook.Next"/>, off when the setting is), and dismissing it (saved).</summary>
    public static class HintService
    {
        /// <summary>The context a hint's conditions read: the region and location in play, the beasts owned, Hearthglen's state.</summary>
        public static HintContext Context(GameSession session, int nodeId = -1, int pickStep = 0)
        {
            PlayerSave save = session?.Save;
            return new HintContext
            {
                RegionId = save?.Campaign?.ActiveRun?.RegionId ?? string.Empty,
                NodeId = nodeId,
                Beasts = save?.Beasts?.Count ?? 0,
                PickStep = pickStep,
                InTutorial = save != null && !save.Tutorial.HearthglenCleared
            };
        }

        /// <summary>The hint due for <paramref name="trigger"/> now, or null.</summary>
        public static HintData Next(GameSession session, string trigger, int nodeId = -1, int pickStep = 0)
        {
            if (session?.Save == null || session.Content.Hints == null)
            {
                return null;
            }

            return session.Content.Hints.Next(trigger, Context(session, nodeId, pickStep), session.Save.Tutorial, session.Settings.TutorialHints);
        }

        /// <summary>Marks <paramref name="hintId"/> seen (never shown again) and saves.</summary>
        public static void Dismiss(GameSession session, string hintId)
        {
            if (session?.Save != null && session.Save.Tutorial.MarkSeen(hintId))
            {
                session.Autosave(AutosaveReason.Results);
            }
        }

        /// <summary>Turns every hint off (the settings switch) and saves the settings.</summary>
        public static void TurnOff(GameSession session)
        {
            session.Settings.TutorialHints = false;
            session.SaveSettings();
        }
    }
}

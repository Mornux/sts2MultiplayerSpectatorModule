using System.Collections;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.addons.mega_text;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.TreasureRelicPicking;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Screens.ScreenContext;
using MegaCrit.Sts2.Core.Nodes.Screens.TreasureRoomRelic;
using MegaCrit.Sts2.Core.Nodes.TreasureRooms;
using MegaCrit.Sts2.Core.Runs;

namespace sts2MultiplayerSpectatorModule.Scripts.Treasure;

// Replay only native scenery, relic holders and hands; reward allocation remains entirely on the host.
public partial class SpectatorTreasureView : Control, IScreenContext
{
    internal const string DisplayMarker = "spectator_treasure";
    private NTreasureRoom? _native;
    private NTreasureRoomRelicCollection? _collection;
    private NHandImageCollection? _hands;
    private Control? _fightBackstop;
    private NCommonBanner? _banner;
    private NTreasureButton? _chest;
    private RunState? _displayRun;
    private readonly List<NTreasureRoomRelicHolder> _holders = [];
    private readonly HashSet<NTreasureRoomRelicHolder> _initializedHolders = [];
    private TreasureState? _state;
    private string _roomKey = "";
    private int _generation;
    private bool _awardsStarted, _emptyShown;
    private CancellationTokenSource _cts = new();
    internal NTreasureRoom? NativeRoom => _native;
    internal NTreasureRoomRelicCollection? NativeCollection => _collection;
    internal NHandImageCollection? Hands => _hands;
    internal IReadOnlyList<NTreasureRoomRelicHolder> RelicHolders => _holders;
    internal Task AnimationTask { get; private set; } = Task.CompletedTask;
    internal int OpenCount { get; private set; }
    internal int AwardReplayCount { get; private set; }
    internal int GrabCount { get; private set; }
    internal int FightCount { get; private set; }
    public Control? DefaultFocusedControl => null;

    public override void _Ready()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;
    }

    internal void Render(TreasureState state)
    {
        if (!TreasureStateBounds.Valid(state)) return;
        if (_roomKey != state.Key)
        {
            Clear();
            _roomKey = state.Key;
            _state = state;
            int generation = _generation;
            Callable.From(() => LoadNativeRoom(generation)).CallDeferred();
            return;
        }
        _state = state;
        if (GodotObject.IsInstanceValid(_native)) UpdateNativeState();
    }

    private void LoadNativeRoom(int generation)
    {
        if (generation != _generation || !IsInsideTree() || _state is not { } state) return;
        try
        {
            var players = state.Players.Select(player => SpectatorDisplayModels.Create(
                ModelDb.GetById<CharacterModel>(ModelId.Deserialize(player.CharacterId)), player.Id)).ToArray();
            _displayRun = RunState.CreateForTest(players, seed: "SPECTATOR_TREASURE");
            var room = ResourceLoader.Load<PackedScene>("res://scenes/rooms/treasure_room.tscn").Instantiate<NTreasureRoom>();
            _native = room;
            room.SetMeta(DisplayMarker, true);
            var proceed = room.GetNode("%ProceedButton");
            proceed.GetParent().RemoveChild(proceed); proceed.Free();
            AddChild(room);
            room.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
            _collection = room.GetNode<NTreasureRoomRelicCollection>("%RelicCollection");
            _hands = _collection.GetNode<NHandImageCollection>("%HandsContainer");
            _fightBackstop = _collection.GetNode<Control>("%FightBackstop");
            _fightBackstop.Hide(); _fightBackstop.Modulate = new Color(1, 1, 1, 0);
            _collection.GetNode<MegaLabel>("%FightLabel").Text = new LocString("gameplay_ui", "TREASURE_FIGHT_TEXT").GetFormattedText();
            SpectatorMap.Set(_collection, "_runState", _displayRun);
            SpectatorMap.Set(_hands, "_runState", _displayRun);
            var nativeHands = (IList)AccessTools.Field(typeof(NHandImageCollection), "_hands").GetValue(_hands)!;
            // The hands use their native animation controllers, without the live input synchronizer or cursor hooks.
            if (players.Length > 1)
                for (int index = 0; index < players.Length; index++)
                {
                    var hand = NHandImage.Create(players[index], index);
                    nativeHands.Add(hand); _hands.AddChild(hand);
                    hand.AnimateIn();
                    hand.SetFrozenForRelicAwards(true);
                }
            _banner = room.GetNode<NCommonBanner>("%Banner");
            _banner.label.SetTextAutoSize(new LocString("gameplay_ui", players.Length == 1
                ? "TREASURE_BANNER" : "CHOOSE_SHARED_RELIC_HEADER").GetFormattedText());
            _banner.AnimateIn();
            _chest = room.GetNode<NTreasureButton>("%Chest");
            _chest.Setup(ModelDb.GetById<ActModel>(ModelId.Deserialize(state.ActId)));
            _chest.UpdateChestSkin(false);
            _chest.GetNode<Control>("%HotkeyIcon").Hide();
            _chest.AnimOut();
            OpenCount++;
            DisableInput(room);
            UpdateNativeState();
        }
        catch (Exception error)
        {
            if (generation == _generation && IsInsideTree()) GD.PushWarning("[Spectator] Treasure scenery: " + error);
            Clear();
        }
    }

    private void UpdateNativeState()
    {
        if (_state is not { } state || _collection == null || _hands == null || _displayRun == null || _awardsStarted) return;
        var container = _collection.GetNode<Control>("Container");
        var single = _collection.GetNode<NTreasureRoomRelicHolder>("%SingleplayerRelicHolder");
        var originals = container.GetChildren().OfType<NTreasureRoomRelicHolder>().Where(holder => holder != single).ToArray();
        while (_holders.Count > state.Relics.Length) { _holders[^1].Hide(); _holders.RemoveAt(_holders.Count - 1); }
        for (int index = 0; index < state.Relics.Length; index++)
        {
            if (index < _holders.Count && _holders[index].Relic.Model.Id.ToString() == state.Relics[index].Id) continue;
            var holder = state.Relics.Length == 1 ? single : index < originals.Length ? originals[index]
                : ResourceLoader.Load<PackedScene>("res://scenes/ui/treasure_relic_holder.tscn").Instantiate<NTreasureRoomRelicHolder>();
            if (index < _holders.Count)
            {
                // A new native holder avoids adding the same player roster twice to its vote container.
                holder = ResourceLoader.Load<PackedScene>("res://scenes/ui/treasure_relic_holder.tscn").Instantiate<NTreasureRoomRelicHolder>();
                holder.Position = _holders[index].Position;
                _holders[index].Hide(); _holders[index].QueueFree(); _holders[index] = holder;
            }
            else
            {
                if (_initializedHolders.Contains(holder))
                {
                    var position = holder.Position;
                    holder = ResourceLoader.Load<PackedScene>("res://scenes/ui/treasure_relic_holder.tscn").Instantiate<NTreasureRoomRelicHolder>();
                    holder.Position = position;
                }
                _holders.Add(holder);
            }
            if (holder.GetParent() == null)
            {
                container.AddChild(holder);
                if (index >= originals.Length) holder.Position = new Vector2((index % 4 - 1.5f) * 225, index / 4 * 250);
            }
            holder.Index = index;
            holder.Initialize(ModelDb.GetById<RelicModel>(ModelId.Deserialize(state.Relics[index].Id)), _displayRun);
            _initializedHolders.Add(holder);
            holder.Modulate = Colors.White;
            holder.Show();
            DisableInput(holder);
            var destination = holder.Position;
            holder.Position += Vector2.Down * (state.Relics.Length == 1 ? 150 : 50);
            holder.CreateTween().TweenProperty(holder, "position", destination, 0.6).SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Back);
        }
        foreach (var holder in container.GetChildren().OfType<NTreasureRoomRelicHolder>())
            if (!_holders.Contains(holder)) holder.Hide();
        if (state.Relics.Length == 2 && originals.Length >= 4) _holders[1].Position = originals[3].Position;
        foreach (var holder in _holders) holder.VoteContainer.RefreshPlayerVotes();
        foreach (var player in state.Players)
        {
            if (_hands.GetHand(player.Id) is not { } hand) continue;
            if (player.Voted && player.Vote >= 0 && player.Vote < _holders.Count)
            {
                hand.SetFrozenForRelicAwards(false);
                var holder = _holders[player.Vote];
                hand.SetPointingPosition(holder.GlobalPosition + holder.Size * 0.5f + Vector2.Down.Rotated(hand.Rotation) * 100);
            }
            else hand.SetFrozenForRelicAwards(true);
            hand.SetIsDown(player.Voted);
        }
        if (state.Finished && state.Relics.Length == 0 && !_emptyShown)
        {
            _emptyShown = true;
            SpectatorMap.Call(_collection, "SpawnEmptyChestVfx");
        }
        _collection.Show();
        if (state.Awards.Length > 0 || state.Finished)
        {
            _awardsStarted = true;
            AnimationTask = PlayAwards(state, _generation, _cts.Token);
        }
    }

    internal bool VotedFor(Player player, int index) => _state?.Players.Any(source => source.Id == player.NetId && source.Voted && source.Vote == index) == true;

    private async Task PlayAwards(TreasureState state, int generation, CancellationToken token)
    {
        AwardReplayCount++;
        var grabs = new List<Task>();
        bool grabsObserved = false;
        try
        {
            bool Current() => generation == _generation && !token.IsCancellationRequested && IsInsideTree();
            var hands = _hands!;
            var players = _displayRun!.Players.ToDictionary(player => player.NetId);
            _banner!.AnimateOut();
            foreach (var player in state.Players.Where(player => player.Voted && player.Vote < 0)) hands.GetHand(player.Id)?.SetSkipped();
            hands.BeforeRelicsAwarded();
            if (_emptyShown) await Cmd.Wait(1.7f, token);
            else
            {
                var entrance = CreateTween(); entrance.TweenInterval(0.6);
                await entrance.AwaitFinished(token); // Match the native holder entrance even in instant mode.
            }
            if (!Current()) return;
            int? previousType = null;
            foreach (var award in state.Awards.OrderBy(award => award.Type))
            {
                if (!Current()) return;
                if (previousType.HasValue && award.Type != previousType.Value)
                {
                    await Cmd.Wait(0.5f, token);
                    if (!Current()) return;
                }
                previousType = award.Type;
                var holder = _holders[award.Index];
                holder.AnimateAwayVotes();
                var type = (RelicPickingResultType)award.Type;
                if (type == RelicPickingResultType.Skipped) continue;
                if (type == RelicPickingResultType.FoughtOver)
                {
                    var fight = new RelicPickingFight();
                    fight.playersInvolved.AddRange(award.Fighters.Select(id => players[id]));
                    fight.rounds.AddRange(award.Rounds.Select(round => new RelicPickingFightRound
                    { moves = round.Select(move => move.HasValue ? (RelicPickingFightMove?)move.Value : null).ToList() }));
                    var result = new RelicPickingResult { type = type, relic = holder.Relic.Model, player = players[award.Player], fight = fight };
                    var backdrop = _fightBackstop!;
                    holder.Reparent(backdrop.GetParent());
                    backdrop.GetParent().MoveChild(holder, backdrop.GetIndex() + 1);
                    backdrop.Show();
                    var tween = CreateTween();
                    tween.TweenProperty(holder, "global_position", (backdrop.Size - holder.Size) * 0.5f, 0.25)
                        .SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.In);
                    tween.TweenProperty(backdrop, "modulate:a", 1f, 0.25);
                    hands.BeforeFightStarted(fight.playersInvolved);
                    await tween.AwaitFinished(token);
                    if (!Current()) return;
                    await Cmd.Wait(1f, token);
                    if (!Current()) return;
                    FightCount++;
                    await hands.DoFight(result, holder);
                    if (!Current()) return;
                    GrabCount++;
                    tween = CreateTween(); tween.TweenProperty(backdrop, "modulate:a", 0f, 0.25);
                    await tween.AwaitFinished(token);
                    if (!Current()) return;
                    backdrop.Hide();
                }
                else if (hands.GetHand(award.Player) is { } hand)
                {
                    GrabCount++;
                    grabs.Add(hand.GrabRelic(holder));
                    await Cmd.Wait(0.25f, token);
                    if (!Current()) return;
                }
                else holder.Hide(); // Native singleplayer has no character hand.
            }
            grabsObserved = true;
            await Task.WhenAll(grabs);
            if (!Current()) return;
            if (!_emptyShown && grabs.Count == 0 && state.Players.Length > 1)
            {
                await Cmd.Wait(0.7f, token);
                if (!Current()) return;
            }
            var fade = CreateTween(); fade.TweenProperty(_collection!, "modulate:a", 0f, 0.3);
            await fade.AwaitFinished(token);
            if (!Current()) return;
            _collection!.Hide(); _chest!.AnimIn();
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested || generation != _generation) { }
        catch (Exception error)
        {
            if (generation == _generation && IsInsideTree()) GD.PushWarning("[Spectator] Treasure animation: " + error);
        }
        finally
        {
            // Observe every started native grab, including those cancelled by leaving this room.
            if (!grabsObserved && grabs.Count > 0)
            {
                try { await Task.WhenAll(grabs); }
                catch (OperationCanceledException) when (token.IsCancellationRequested || generation != _generation) { }
                catch (Exception error)
                {
                    if (generation == _generation && IsInsideTree()) GD.PushWarning("[Spectator] Treasure grab: " + error);
                }
            }
        }
    }

    internal void Clear()
    {
        ++_generation;
        _cts.Cancel(); _cts.Dispose(); _cts = new CancellationTokenSource();
        if (GodotObject.IsInstanceValid(_hands))
        {
            Cancel(_hands!);
            foreach (var hand in _hands!.GetChildren().OfType<NHandImage>()) Cancel(hand);
        }
        if (GodotObject.IsInstanceValid(_native)) { _native!.Hide(); _native.QueueFree(); }
        _native = null; _collection = null; _hands = null; _displayRun = null; _fightBackstop = null; _banner = null; _chest = null;
        _holders.Clear(); _initializedHolders.Clear(); _state = null; _roomKey = ""; _awardsStarted = _emptyShown = false;
    }

    internal static void Cancel(Node node) => (AccessTools.Field(node.GetType(), "_cts")?.GetValue(node) as CancellationTokenSource)?.Cancel();
    internal static bool Owns(NTreasureRoom room) => room.HasMeta(DisplayMarker);
    internal static SpectatorTreasureView? Find(Node node)
    {
        for (Node? current = node; current != null; current = current.GetParent())
            if (current is SpectatorTreasureView view) return view;
        return null;
    }
    private static void DisableInput(Node root)
    {
        if (root is Control control) { control.MouseFilter = MouseFilterEnum.Ignore; control.FocusMode = FocusModeEnum.None; }
        if (root is NButton button) button.Disable();
        foreach (var child in root.GetChildren()) DisableInput(child);
    }
    public override void _ExitTree() => Clear();
}

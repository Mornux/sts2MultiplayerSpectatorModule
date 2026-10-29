using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Multiplayer;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Runs;

namespace sts2MultiplayerSpectatorModule.Scripts;

// 切换观战视角与战斗界面。
// Switch spectator perspective and combat UI.

public partial class SpectatorCombat
{

    // 在动作之间切换视角，完整重建原版 UI 以解除旧玩家的信号绑定。
    // Switch between actions and rebuild native UI to detach the previous player's signals.
    private void SwitchView()
    {
        if (_runNode?.CombatRoom is not { } room || _state == null) return;
        try
        {
            _viewIndex = _pendingView;
            ulong playerId = _state.Players[_viewIndex].NetId;
            LocalContext.NetId = playerId;
            ((NetReplayGameService)RunManager.Instance.NetService).NetId = playerId;
            var old = room.Ui;
            string path = old.SceneFilePath;
            var parent = old.GetParent();
            var ui = ResourceLoader.Load<PackedScene>(path).Instantiate<NCombatUi>();
            old.Deactivate();
            parent.RemoveChild(old);
            old.QueueFree();
            SpectatorMap.Set(room, "Ui", ui);
            parent.AddChild(ui);
            ui.Activate(CombatManager.Instance.DebugOnlyGetState()!);
            foreach (var card in _state.Players[_viewIndex].PlayerCombatState!.Hand.Cards)
                ui.Hand.Add(NCard.Create(card)!);
            SpectatorMap.Call(ui.Hand, "RefreshLayout");
            var oldBar = _runNode.GlobalUi.TopBar;
            var bar = ResourceLoader.Load<PackedScene>(oldBar.SceneFilePath).Instantiate<NTopBar>();
            var barParent = oldBar.GetParent();
            barParent.RemoveChild(oldBar);
            oldBar.QueueFree();
            SpectatorMap.Set(_runNode.GlobalUi, "TopBar", bar);
            barParent.AddChild(bar);
            bar.Initialize(_state);
            var relics = _runNode.GlobalUi.RelicInventory;
            SpectatorMap.Call(relics, "DisconnectPlayerEvents");
            ((System.Collections.IList)AccessTools.Field(relics.GetType(), "_relicNodes").GetValue(relics)!).Clear();
            foreach (var child in relics.GetChildren()) { relics.RemoveChild(child); child.QueueFree(); }
            relics.Initialize(_state);
            var nodes = (List<NCreature>)AccessTools.Field(typeof(NCombatRoom), "_creatureNodes").GetValue(room)!;
            var combat = CombatManager.Instance.DebugOnlyGetState()!;
            NCombatRoom.PositionPlayersAndPets(nodes.Where(node => combat.Allies.Contains(node.Entity)).ToList(),
                combat.Encounter!.GetCameraScaling(), combat.Encounter.FullyCenterPlayers);
            ConfigureCombatUi();
        }
        catch (Exception error) { Fail(L.T("combat.switch_failed") + error.Message); }
    }

    private void ConfigureCombatUi()
    {
        if (_runNode?.CombatRoom is not { } room) return;
        room.Ui.EndTurnButton.Disable();
        var global = _runNode.GlobalUi;
        // 原版窗口提升到观战地图之上，避免已打开却被覆盖。
        // Place native capstone windows above the spectator map overlay.
        if (global.CapstoneContainer.GetParent() is not CanvasLayer)
        {
            var layer = new CanvasLayer { Layer = 150 };
            _runNode.AddChild(layer);
            global.CapstoneContainer.Reparent(layer, false);
        }
        global.RelicInventory.Hide();
        global.TopBar.Gold.Hide();
        global.TopBar.PotionContainer.Hide();
        foreach (var button in Descendants(global.TopBar.PotionContainer).OfType<NButton>()) button.Disable();
        string name = _session.PlayerName(_state!.Players[_viewIndex].NetId);
        global.TopBar.Portrait.TooltipText = L.T("combat.perspective_tooltip", name);
        var label = global.TopBar.GetNodeOrNull<MegaCrit.Sts2.addons.mega_text.MegaLabel>("SpectatorViewLabel");
        if (label == null)
        {
            label = (MegaCrit.Sts2.addons.mega_text.MegaLabel)global.TopBar.Timer.GetNode("TimerLabel").Duplicate();
            label.Name = "SpectatorViewLabel";
            label.MouseFilter = Control.MouseFilterEnum.Ignore;
            global.TopBar.AddChild(label);
            label.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.CenterTop);
            label.Position += new Vector2(-300, 84);
            label.Size = new Vector2(600, 40);
            label.HorizontalAlignment = HorizontalAlignment.Center;
            label.AddThemeFontSizeOverride("font_size", 24);
        }
        label.SetTextAutoSize(L.T("combat.perspective", name));
        UpdateTimer();
    }

    internal void UpdateTimer()
    {
        if (_runNode == null || !GodotObject.IsInstanceValid(_runNode) || _session.Map == null) return;
        var timer = _runNode.GlobalUi?.TopBar?.Timer;
        if (timer == null) return;
        timer.Show();
        timer.GetNode<MegaCrit.Sts2.addons.mega_text.MegaLabel>("TimerLabel")
            .SetTextAutoSize(TimeFormatting.Format(_session.Map.RunTime));
    }

    public void ToggleMap()
    {
        if (!HasCombat) return;
        if (_map.Visible) HideMap(); else _map.Show();
    }

    public void OpenMenu()
    {
        if (!HasCombat) return;
        _map.Show();
        _map.SetPaused(true);
    }

    private void HideMap()
    {
        if (HasCombat && _started && !_loading && !_liveCombatEnded) _map.Hide();
    }

    public override void _Input(InputEvent input)
    {
        if (SpectatorDanmaku.IsEditing) return;
        if (!HasCombat || _loading || _map.Visible || _state == null) return;
        if (input is InputEventMouseButton { Pressed: true } mouse && mouse.ButtonIndex is MouseButton.WheelUp or MouseButton.WheelDown)
        {
            if (NCapstoneContainerInUse()) return;
            int direction = mouse.ButtonIndex == MouseButton.WheelUp ? -1 : 1;
            _pendingView = (_pendingView + direction + _state.Players.Count) % _state.Players.Count;
            GetViewport().SetInputAsHandled();
        }
    }
}

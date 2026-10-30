using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.UI;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Multiplayer;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Screens.ScreenContext;
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
            // 先按旧视角清理悬浮，防止退出回调操作到新视角的角色。
            // Clear hover in the old context before callbacks can target the new perspective.
            var nodes = (List<NCreature>)AccessTools.Field(typeof(NCombatRoom), "_creatureNodes").GetValue(room)!;
            foreach (var node in nodes.Where(node => node.IsFocused)) SpectatorMap.Call(node, "OnUnfocus");
            _viewIndex = _pendingView;
            ulong playerId = _state.Players[_viewIndex].NetId;
            LocalContext.NetId = playerId;
            ((NetReplayGameService)RunManager.Instance.NetService).NetId = playerId;
            var old = room.Ui;
            string path = old.SceneFilePath;
            var parent = old.GetParent();
            int uiIndex = old.GetIndex();
            var ui = ResourceLoader.Load<PackedScene>(path).Instantiate<NCombatUi>();
            old.Deactivate();
            parent.RemoveChild(old);
            old.QueueFree();
            SpectatorMap.Set(room, "Ui", ui);
            parent.AddChild(ui);
            parent.MoveChild(ui, uiIndex);
            ui.Activate(CombatManager.Instance.DebugOnlyGetState()!);
            foreach (var card in _state.Players[_viewIndex].PlayerCombatState!.Hand.Cards)
                ui.Hand.Add(NCard.Create(card)!);
            SpectatorMap.Call(ui.Hand, "RefreshLayout");
            var oldBar = _runNode.GlobalUi.TopBar;
            var bar = ResourceLoader.Load<PackedScene>(oldBar.SceneFilePath).Instantiate<NTopBar>();
            // 保留跑局场景的鼠标穿透覆盖，否则透明顶部栏会拦住整个战斗界面。
            // Preserve the run scene's input passthrough so the transparent top bar cannot block combat controls.
            bar.MouseFilter = oldBar.MouseFilter;
            var barParent = oldBar.GetParent();
            int barIndex = oldBar.GetIndex();
            barParent.RemoveChild(oldBar);
            oldBar.QueueFree();
            SpectatorMap.Set(_runNode.GlobalUi, "TopBar", bar);
            barParent.AddChild(bar);
            barParent.MoveChild(bar, barIndex);
            bar.Initialize(_state);
            var relics = _runNode.GlobalUi.RelicInventory;
            SpectatorMap.Call(relics, "DisconnectPlayerEvents");
            ((System.Collections.IList)AccessTools.Field(relics.GetType(), "_relicNodes").GetValue(relics)!).Clear();
            foreach (var child in relics.GetChildren()) { relics.RemoveChild(child); child.QueueFree(); }
            relics.Initialize(_state);
            var combat = CombatManager.Instance.DebugOnlyGetState()!;
            // 原版创建角色时缓存本地身份；切换后同步缓存，避免悬停自身时把自身状态隐藏。
            // Refresh cached local identity so hovering the new viewpoint cannot hide its own state display.
            foreach (var node in nodes.Where(node => node.Entity.IsPlayer || node.Entity.PetOwner != null))
            {
                bool remote = node.Entity.IsPlayer ? !LocalContext.IsMe(node.Entity) : !LocalContext.IsMe(node.Entity.PetOwner!);
                SpectatorMap.Set(node, "_isRemotePlayerOrPet", remote);
                var display = node.GetNode<NCreatureStateDisplay>("%HealthBar");
                if (remote) display.HideImmediately();
                else display.AnimateIn(HealthBarAnimMode.FromHidden);
            }
            NCombatRoom.PositionPlayersAndPets(nodes.Where(node => combat.Allies.Contains(node.Entity)).ToList(),
                combat.Encounter!.GetCameraScaling(), combat.Encounter.FullyCenterPlayers);
            // 重新绑定导航并通知原版激活流程，恢复牌堆和悬浮交互。
            // Rebind navigation and refresh native activation for pile and hover interaction.
            SpectatorMap.Call(room, "UpdateCreatureNavigation");
            ActiveScreenContext.Instance.Update();
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
            // 提示保留少量左侧边距，与地图提示保持左对齐。
            // Keep a small left margin, matching the map status.
            label.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.TopLeft);
            label.Position = new Vector2(24, 84);
            label.Size = new Vector2(1200, 40);
            label.HorizontalAlignment = HorizontalAlignment.Left;
            label.AddThemeFontSizeOverride("font_size", 24);
        }
        label.SetTextAutoSize(L.T("combat.perspective", name));
        UpdateTimer();
    }

    internal void UpdateTimer()
    {
        if (_runNode == null || !GodotObject.IsInstanceValid(_runNode)) return;
        // 原版列表按回放平台取名；改用观战会话，并随状态刷新迟到的 Steam 昵称。
        // Resolve native roster names through the spectator session and refresh late Steam persona data.
        if (_runNode.GlobalUi?.MultiplayerPlayerContainer is { } roster)
            foreach (var row in roster.GetChildren().OfType<MegaCrit.Sts2.Core.Nodes.Multiplayer.NMultiplayerPlayerState>())
            {
                var label = row.GetNode<MegaCrit.Sts2.addons.mega_text.MegaLabel>("%NameplateLabel");
                string name = _session.PlayerName(row.Player.NetId);
                if (label.Text != name) label.SetTextAutoSize(name);
            }
        if (_session.Map == null) return;
        var timer = _runNode.GlobalUi?.TopBar?.Timer;
        if (timer == null) return;
        timer.Show();
        timer.GetNode<MegaCrit.Sts2.addons.mega_text.MegaLabel>("TimerLabel")
            .SetTextAutoSize(TimeFormatting.Format(_session.Map.RunTime));
    }

    public void ToggleMap()
    {
        if (!HasCombat || !_automaticSwitch.IsCompleted) return;
        if (_map.Visible) HideMap(); else _map.Show();
    }

    public void OpenMenu()
    {
        if (!HasCombat || !_automaticSwitch.IsCompleted) return;
        _map.Show();
        _map.SetPaused(true);
    }

    private void HideMap()
    {
        if (_automaticSwitch.IsCompleted && HasCombat && _started && !_loading && !_liveCombatEnded) _map.Hide();
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

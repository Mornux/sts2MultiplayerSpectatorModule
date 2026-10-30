using System.Text.Json;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using MegaCrit.Sts2.Core.Nodes.Multiplayer;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.Screens.ScreenContext;
using MegaCrit.Sts2.Core.Saves.Runs;

namespace sts2MultiplayerSpectatorModule.Scripts;

// 使用有界分片同步背包，打开原版只读玩家详情，不创建真实参战玩家。
// Synchronize bounded inventory snapshots and open native read-only player details.
public partial class SpectatorMap
{
    private readonly BattleAssembler _inventoryAssembler = new();
    private readonly Dictionary<ulong, SerializablePlayer> _inventories = new();
    private string _inventoryKey = "";
    private long _inventorySequence = -1;
    private Control? _inspection;
    private ColorRect? _inspectionBackstop;
    private ulong? _inspectedPlayer;

    private void ReceiveInventory(string json)
    {
        if (!_session.Watching) return;
        try
        {
            var frame = JsonSerializer.Deserialize<BattleFrame>(json);
            if (frame?.Kind != "inventory" || frame.Combat != _session.CurrentMapKey) return;
            _inventoryAssembler.Expire(Time.GetTicksMsec());
            var bytes = _inventoryAssembler.Accept(frame, Time.GetTicksMsec());
            if (bytes == null) return;
            if (_inventoryKey == frame.Combat && frame.Sequence <= _inventorySequence) return;
            var reader = new PacketReader(); reader.Reset(bytes);
            int count = reader.ReadInt();
            if (count is < 1 or > 4) return;
            var received = new Dictionary<ulong, SerializablePlayer>();
            for (int i = 0; i < count; i++)
            {
                var player = reader.Read<SerializablePlayer>();
                if (_session.Map?.Players?.Any(item => item.Id == player.NetId) != true
                    || player.Deck.Count > 1000 || player.Relics.Count > 512 || player.Potions.Count > 32) return;
                received.Add(player.NetId, player);
            }
            _inventories.Clear();
            foreach (var pair in received) _inventories.Add(pair.Key, pair.Value);
            _inventoryKey = frame.Combat; _inventorySequence = frame.Sequence;
        }
        catch (Exception error) { GD.PushWarning("[Spectator] Inventory: " + error.Message); }
    }

    private void OpenInventory(ulong id)
    {
        if (_inventoryKey != _session.CurrentMapKey || !_inventories.TryGetValue(id, out var save)) return;
        try
        {
            CloseInventory();
            var player = Player.FromSerializable(save);
            Set(player, "RunState", _displayRun);
            // 原版详情依赖共享遮罩，独立地图补上相同透明度的黑色背景。
            // Native details rely on a shared backstop; supply the same black opacity on the standalone map.
            _inspectionBackstop = new ColorRect { Name = "InventoryBackstop", Color = new Color(0, 0, 0, 0.85098f) };
            _hoverTips.AddChild(_inspectionBackstop);
            _inspectionBackstop.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
            _inspection = NMultiplayerPlayerExpandedState.Create(player);
            _hoverTips.AddChild(_inspection);
            _inspection.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
            _inspectedPlayer = id;
            if (_playerRows.TryGetValue(id, out var selected)) selected.Root.GetNode<MegaCrit.Sts2.Core.Nodes.Combat.NSelectionReticle>("SelectionReticle").OnSelect();
            _inspection.GetNode<MegaCrit.Sts2.Core.Nodes.CommonUi.NBackButton>("%BackButton").Enable();
            _inspection.GetNode<MegaCrit.Sts2.addons.mega_text.MegaRichTextLabel>("%PlayerNameLabel").Text =
                (_session.Map?.Players?.FirstOrDefault(item => item.Id == id)?.Name ?? "").Replace("[", "[lb]");
            ActiveScreenContext.Instance.Update();
        }
        catch (Exception error) { GD.PushWarning("[Spectator] Inspect player: " + error.Message); CloseInventory(); }
    }

    internal void CloseInventory()
    {
        if (_inspectedPlayer is { } id && _playerRows.TryGetValue(id, out var selected))
            selected.Root.GetNode<MegaCrit.Sts2.Core.Nodes.Combat.NSelectionReticle>("SelectionReticle").OnDeselect();
        _inspectedPlayer = null;
        if (_inspection != null) { _inspection.Hide(); _inspection.QueueFree(); _inspection = null; }
        if (_inspectionBackstop != null) { _inspectionBackstop.Hide(); _inspectionBackstop.QueueFree(); _inspectionBackstop = null; }
        ActiveScreenContext.Instance.Update();
    }
}

// 地图详情窗口没有原版跑局的 Capstone 容器，由观战界面关闭。
// Close map inspection locally when no native run capstone container owns it.
[HarmonyPatch(typeof(NMultiplayerPlayerExpandedState), "BackButtonPressed")]
internal static class SpectatorInventoryBack
{
    private static bool Prefix(NMultiplayerPlayerExpandedState __instance)
    {
        var map = SpectatorMap.Find(__instance);
        if (map == null) return true;
        map.CloseInventory(); return false;
    }
}

// 独立地图没有原版跑局连接，仅给详情标题提供安全的显示平台。
// Supply a display-only platform when native player details open without a running game connection.
[HarmonyPatch(typeof(NMultiplayerPlayerExpandedState), nameof(NMultiplayerPlayerExpandedState._Ready))]
internal static class SpectatorInventoryPlatform
{
    private static readonly MegaCrit.Sts2.Core.Multiplayer.Game.INetGameService DisplayService =
        new MegaCrit.Sts2.Core.Multiplayer.NetSingleplayerGameService();
    private static MegaCrit.Sts2.Core.Multiplayer.Game.INetGameService Service(MegaCrit.Sts2.Core.Runs.RunManager manager) =>
        manager.NetService ?? DisplayService;
    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) =>
        HarmonyLib.Transpilers.MethodReplacer(instructions,
            AccessTools.PropertyGetter(typeof(MegaCrit.Sts2.Core.Runs.RunManager), "NetService"),
            AccessTools.Method(typeof(SpectatorInventoryPlatform), nameof(Service)));
}


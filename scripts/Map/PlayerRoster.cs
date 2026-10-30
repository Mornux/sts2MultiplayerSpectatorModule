using Godot;
using MegaCrit.Sts2.addons.mega_text;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Unlocks;
using static sts2MultiplayerSpectatorModule.Scripts.SpectatorSession;

namespace sts2MultiplayerSpectatorModule.Scripts;

// 显示地图左侧玩家名单。
// Display the map participant roster.

public partial class SpectatorMap
{
    private VBoxContainer? _playerList;
    private readonly Dictionary<ulong, PlayerRow> _playerRows = new();
    private sealed record PlayerRow(Control Root, Player Model, MegaLabel Name, NHealthBar Health);

    // 复用原版名单的头像、名字和生命条，展示模型只属于观战地图。
    // Reuse native roster portraits, names, and health bars with display-only models.
    private void ClearPlayerList()
    {
        _playerList?.Free();
        _playerList = null;
        _playerRows.Clear();
    }

    private void UpdatePlayerList(MapSnapshot snapshot)
    {
        var players = snapshot.Players ?? Array.Empty<MapPlayer>();
        if (_playerList == null)
        {
            _playerList = new VBoxContainer { Name = "SpectatorPlayers", Position = new Vector2(0, 145),
                Size = new Vector2(280, 0), MouseFilter = MouseFilterEnum.Ignore };
            _playerList.AddThemeConstantOverride("separation", 8);
            AddChild(_playerList);
        }
        foreach (ulong id in _playerRows.Keys.Where(id => !players.Any(player => player.Id == id)).ToArray())
        { _playerRows[id].Root.Free(); _playerRows.Remove(id); }
        for (int index = 0; index < players.Length; index++)
        {
            var source = players[index];
            if (!_playerRows.TryGetValue(source.Id, out var row))
            {
                var model = Player.CreateForNewRun(ModelDb.GetById<CharacterModel>(ModelId.Deserialize(source.CharacterId)), UnlockState.all, source.Id);
                var (root, name, health) = RosterVisuals.Create(model.Character.IconTexture, withHealth: true);
                root.Name = "Player" + source.Id;
                // 保留原版动态选框，鼠标悬停和查看详情时使用同一动画。
                // Reuse the native animated reticle for hover and inventory inspection.
                var template = ResourceLoader.Load<PackedScene>("res://scenes/ui/multiplayer_player_state.tscn").Instantiate<Control>();
                var reticle = (NSelectionReticle)template.GetNode("%SelectionReticle").Duplicate();
                template.Free();
                reticle.UniqueNameInOwner = false;
                root.AddChild(reticle);
                reticle.MouseFilter = MouseFilterEnum.Ignore;
                root.MouseEntered += reticle.OnSelect;
                root.MouseExited += () => { if (_inspectedPlayer != source.Id) reticle.OnDeselect(); };
                root.MouseFilter = MouseFilterEnum.Stop;
                ulong playerId = source.Id;
                root.GuiInput += input =>
                {
                    if (input is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left })
                    { OpenInventory(playerId); root.AcceptEvent(); }
                };
                _playerList.AddChild(root);
                health!.SetCreature(model.Creature);
                health.GetNode<Control>("%BlockContainer").Hide();
                health.FadeOutHpLabel(0, 0);
                row = new(root, model, name, health);
                _playerRows.Add(source.Id, row);
            }
            _playerList.MoveChild(row.Root, index);
            row.Name.SetTextAutoSize(source.Name);
            row.Model.Creature.SetMaxHpInternal(source.MaxHp);
            row.Model.Creature.SetCurrentHpInternal(source.Hp);
            row.Health.UpdateWidthRelativeToReferenceValue(80, 175);
            row.Health.RefreshValues();
            row.Root.TooltipText = $"{source.Name} · {source.Hp}/{source.MaxHp}";
        }
        MoveChild(_playerList, _topBar!.GetIndex() + 1);
        if (_pause != null) MoveChild(_pause, -1);
        MoveChild(_hoverTips, -1);
    }
}

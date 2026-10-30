using Godot;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Multiplayer;
using MegaCrit.Sts2.Core.Multiplayer.Game.PeerInput;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Unlocks;
using static sts2MultiplayerSpectatorModule.Scripts.SpectatorSession;

namespace sts2MultiplayerSpectatorModule.Scripts;

// 直接复用原版画笔、作者视口、轨迹追加和可见性设置，展示身份不进入真实跑局。
// Reuse native pens, author viewports, incremental strokes and visibility with a display-only identity.
public partial class SpectatorNativeInk : NMapDrawings
{
    private sealed class DisplayPlayers(Player player) : IPlayerCollection
    {
        public IReadOnlyList<Player> Players { get; } = [player];
        public Player? GetPlayer(ulong id) => id == player.NetId ? player : null;
        public int GetPlayerSlotIndex(Player value) => ReferenceEquals(value, player) ? 0 : -1;
    }
    private readonly List<Sketch> _rendered = new();
    private PeerInputSynchronizer _spectatorInput = null!;
    private object? _pen;
    private string _color = "";
    internal ulong Author { get; set; }
    private Vector2 _lastSize;
    internal Color StrokeColor => new(_color);

    internal void InitializeInk()
    {
        var service = new NetReplayGameService(SpectatorMap.DisplayPlayerId);
        _spectatorInput = new PeerInputSynchronizer(service);
        var player = Player.CreateForNewRun<Defect>(UnlockState.all, SpectatorMap.DisplayPlayerId);
        Initialize(service, new DisplayPlayers(player), _spectatorInput);
        MouseFilter = MouseFilterEnum.Ignore;
    }

    internal void Sync(NMapScreen map, IReadOnlyList<Sketch> ink, string color, int act)
    {
        var native = map.Drawings;
        Position = native.Position; Scale = native.Scale; Rotation = native.Rotation;
        Size = native.Size;
        var lines = ink.Where(line => line.Act == act).ToArray();
        bool rebuild = _lastSize != Size || lines.Length < _rendered.Count;
        for (int i = 0; !rebuild && i < _rendered.Count; i++)
        {
            var old = _rendered[i]; var next = lines[i];
            rebuild = old.Eraser != next.Eraser || old.Key != next.Key || old.Stroke != next.Stroke || old.Points.Length > next.Points.Length
                || !old.Points.SequenceEqual(next.Points.Take(old.Points.Length))
                || i < _rendered.Count - 1 && old.Points.Length != next.Points.Length;
        }
        _pen ??= SpectatorMap.Call(this, "GetDrawingStateForPlayer", SpectatorMap.DisplayPlayerId);
        if (rebuild)
        {
            ClearAllLines();
            SpectatorMap.Call(this, "StopDrawingLine", _pen!);
            _rendered.Clear();
        }
        _lastSize = Size;
        bool recolor = _color != color;
        _color = color;
        var points = map.GetNode<Control>("TheMap/Points");
        var toPen = native.GetGlobalTransform().AffineInverse() * points.GetGlobalTransform();
        Vector2 ConvertPoint(float[] coords, int index) => toPen * (new Vector2(coords[index], coords[index + 1]) + map.Size / 2);
        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var previous = i < _rendered.Count ? _rendered[i] : null;
            int start = previous?.Points.Length ?? 2;
            if (previous == null)
                SpectatorMap.Call(this, "BeginLine", _pen!, ConvertPoint(line.Points, 0), line.Eraser ? DrawingMode.Erasing : DrawingMode.Drawing);
            for (int point = start; point < line.Points.Length; point += 2)
                SpectatorMap.Call(this, "UpdateCurrentLinePosition", _pen!, ConvertPoint(line.Points, point));
            if (!line.Live && (previous == null || previous.Live)) SpectatorMap.Call(this, "StopDrawingLine", _pen!);
        }
        _rendered.Clear(); _rendered.AddRange(lines);
        if (recolor)
            foreach (var node in Descendants(this).OfType<Line2D>()) node.DefaultColor = StrokeColor;
    }

    private static IEnumerable<Node> Descendants(Node node)
    {
        foreach (var child in node.GetChildren())
        { yield return child; foreach (var descendant in Descendants(child)) yield return descendant; }
    }

    public override void _ExitTree()
    {
        base._ExitTree();
        _spectatorInput.Dispose();
    }
}

using Godot;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.HoverTips;

namespace sts2MultiplayerSpectatorModule.Scripts;

// 暂借共享窗口的层级；保持原版子节点所有权，并在关闭或退出观战时恢复。
// Borrow shared UI layering while retaining native child ownership and restoring it on close or spectator exit.
internal static class SpectatorUiLayers
{
    private static readonly Dictionary<Control, Lease> Leases = new();
    private static readonly Dictionary<Control, Action> Inspections = new();

    // Tips belong above the native capstone/backstop and shared inspectors. Each native tip
    // keeps its lifetime; its temporary canvas disappears when Remove frees the tip.
    internal static void RaiseHoverTip(NHoverTipSet tip)
    {
        var layer = new CanvasLayer
        {
            Name = "SpectatorHoverTipLayer", Layer = 195,
            Transform = tip.GetGlobalTransformWithCanvas() * tip.GetTransform().AffineInverse()
        };
        tip.GetParent().AddChild(layer);
        tip.Reparent(layer, false);
        tip.TreeExited += () => { if (GodotObject.IsInstanceValid(layer)) layer.QueueFree(); };
    }

    internal static void RaiseInspection(Control control, Action close)
    {
        Inspections[control] = close;
        Raise(control);
    }

    internal static bool OwnsInspection(Node owner) => Inspections.Keys.Any(control =>
        GodotObject.IsInstanceValid(control)
        && (control == owner || control.IsAncestorOf(owner)));

    // RestoreAll also serves map/battle transitions; only terminal cleanup closes native inspectors.
    internal static void CloseInspections()
    {
        foreach (var (control, close) in Inspections.ToArray())
        {
            if (!GodotObject.IsInstanceValid(control)) continue;
            try { close(); }
            catch (Exception error) { GD.PushWarning("[Spectator] Close shared inspection: " + error.Message); }
            finally { if (GodotObject.IsInstanceValid(control)) control.Hide(); }
        }
        Inspections.Clear();
        RestoreAll();
    }

    internal static void Raise(Control control)
    {
        if (Leases.ContainsKey(control) || control.GetParent() == null
            || control.GetParent() is CanvasLayer { Layer: >= 190 }) return;
        // Register before mutating the tree so failed setup and failed rollback remain retryable.
        try
        {
            var lease = new Lease(control);
            Leases.Add(control, lease);
            lease.Raise();
        }
        catch (Exception error)
        {
            GD.PushWarning("[Spectator] Raise shared UI: " + error.Message);
            Restore(control);
        }
    }

    internal static void Restore(Control control)
    {
        if (Leases.TryGetValue(control, out var lease) && lease.Restore()) Leases.Remove(control);
    }

    internal static void RestoreAll()
    {
        foreach (var control in Leases.Keys.ToArray()) Restore(control);
    }

    private sealed class Lease
    {
        private readonly Control _control;
        private readonly Node _parent;
        private readonly Node? _owner;
        private readonly int _index;
        private readonly Vector4 _anchors, _offsets;
        private readonly Vector2 _position, _size, _scale;
        private readonly float _rotation;
        private readonly CanvasLayer _layer;
        private readonly Action _visibilityChanged;
        private bool _restoring;
        private bool _restoreLayout;
        private bool _watchingVisibility;

        internal Lease(Control control)
        {
            _control = control;
            _parent = control.GetParent();
            _owner = control.Owner;
            _index = control.GetIndex();
            _anchors = new(control.AnchorLeft, control.AnchorTop, control.AnchorRight, control.AnchorBottom);
            _offsets = new(control.OffsetLeft, control.OffsetTop, control.OffsetRight, control.OffsetBottom);
            _position = control.Position; _size = control.Size;
            _scale = control.Scale; _rotation = control.Rotation;
            _layer = new CanvasLayer { Layer = 190 };
            // Keeping the original owner above the borrowed layer preserves unique-name lookups.
            _visibilityChanged = () =>
            {
                if (_restoring || control.Visible) return;
                // Do not mutate the tree inside a native window's hide notification.
                Callable.From(() =>
                {
                    if (GodotObject.IsInstanceValid(control) && !control.Visible) SpectatorUiLayers.Restore(control);
                }).CallDeferred();
            };
        }

        internal void Raise()
        {
            // A Control reparent preserves only position. Preserve its full parent canvas transform,
            // including scale/rotation/skew, on the temporary layer and keep its local geometry.
            _layer.Transform = _control.GetGlobalTransformWithCanvas() * _control.GetTransform().AffineInverse();
            var host = _owner ?? _control.GetViewport();
            host.AddChild(_layer);
            _control.Reparent(_layer, false);
            if (_control.GetParent() != _layer) throw new InvalidOperationException("Shared UI could not be reparented");
            _control.AnchorLeft = _control.AnchorTop = _control.AnchorRight = _control.AnchorBottom = 0;
            _control.Position = _position; _control.Size = _size;
            _control.Scale = _scale; _control.Rotation = _rotation;
            _control.VisibilityChanged += _visibilityChanged;
            _watchingVisibility = true;
        }

        internal bool Restore()
        {
            if (_restoring) return false;
            _restoring = true;
            try
            {
                if (GodotObject.IsInstanceValid(_control))
                {
                    // If another owner moved the control, leave that newer ownership intact.
                    if ((_control.GetParent() == _layer || _restoreLayout && _control.GetParent() == _parent)
                        && !_control.IsQueuedForDeletion())
                    {
                        Node destination = GodotObject.IsInstanceValid(_parent) && !_parent.IsQueuedForDeletion()
                            ? _parent : (Node?)NGame.Instance ?? _control.GetTree().Root;
                        if (_control.GetParent() != destination) _control.Reparent(destination, false);
                        if (_control.GetParent() != destination) throw new InvalidOperationException("Shared UI parent is still busy");
                        _restoreLayout = true;
                        if (destination == _parent)
                        {
                            destination.MoveChild(_control, Math.Min(_index, destination.GetChildCount() - 1));
                            _control.AnchorLeft = _anchors.X; _control.AnchorTop = _anchors.Y;
                            _control.AnchorRight = _anchors.Z; _control.AnchorBottom = _anchors.W;
                            _control.OffsetLeft = _offsets.X; _control.OffsetTop = _offsets.Y;
                            _control.OffsetRight = _offsets.Z; _control.OffsetBottom = _offsets.W;
                            _control.Scale = _scale; _control.Rotation = _rotation;
                            if (GodotObject.IsInstanceValid(_owner) && _owner!.IsAncestorOf(_control)) _control.Owner = _owner;
                        }
                    }
                    if (_watchingVisibility) _control.VisibilityChanged -= _visibilityChanged;
                    _watchingVisibility = false;
                }
                if (GodotObject.IsInstanceValid(_layer)) _layer.QueueFree();
                return true;
            }
            catch (Exception error)
            {
                // Retain the lease for a later retry rather than freeing a still-borrowed shared window.
                GD.PushWarning("[Spectator] Restore shared UI: " + error.Message);
                return false;
            }
            finally { _restoring = false; }
        }
    }
}

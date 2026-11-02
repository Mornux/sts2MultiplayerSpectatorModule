using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Saves.Validation;
using System.Reflection;

namespace sts2MultiplayerSpectatorModule.Scripts;

// Own only the temporary global state borrowed by a native replay.
internal sealed class ReplayEnvironment : IDisposable
{
    private static readonly Lazy<FieldInfo> CurrentScene = new(() => AccessTools.Field(typeof(NSceneContainer), "_currentScene")
        ?? throw new MissingFieldException(typeof(NSceneContainer).FullName, "_currentScene"));
    private readonly NSceneContainer _root;
    private readonly Control _scene;
    private readonly Node.ProcessModeEnum _processMode;
    private readonly bool _visible;
    private readonly NButton[] _buttons;
    private readonly ProgressState _progress;
    private readonly ProgressState _temporaryProgress;
    private readonly ulong? _netId;
    private ulong? _temporaryNetId;
    private bool _restored;
    internal NRun? Run { get; private set; }

    private ReplayEnvironment()
    {
        _ = CurrentScene.Value; // Validate before borrowing any scene or progress state.
        _root = NGame.Instance!.RootSceneContainer;
        _scene = _root.CurrentScene ?? throw new InvalidOperationException(L.T("combat.no_menu"));
        _processMode = _scene.ProcessMode;
        _visible = _scene.Visible;
        _buttons = Buttons(_scene).Where(button => button.IsEnabled).ToArray();
        _netId = LocalContext.NetId;
        _temporaryNetId = _netId;
        _progress = SaveManager.Instance.Progress;
        _temporaryProgress = ProgressState.FromSerializable(_progress.ToSerializable(), new DeserializationContext());
        _temporaryProgress.EnableFtues = false;
    }

    internal static ReplayEnvironment Enter()
    {
        var environment = new ReplayEnvironment();
        try
        {
            environment._scene.Hide();
            environment._scene.ProcessMode = Node.ProcessModeEnum.Disabled;
            SaveManager.Instance.Progress = environment._temporaryProgress;
            return environment;
        }
        catch { environment.Dispose(); throw; }
    }

    internal void Attach(NRun run)
    {
        Run = run;
        // SetCurrentScene frees the retained menu, so this binding is intentional.
        CurrentScene.Value.SetValue(_root, run);
        _root.AddChild(run);
    }

    internal void TrackIdentity() => _temporaryNetId = LocalContext.NetId;

    public void Dispose()
    {
        if (_restored) return;
        _restored = true;
        Restore("scene", () =>
        {
            if (!GodotObject.IsInstanceValid(_root) || !GodotObject.IsInstanceValid(_scene)) return;
            if (_root.CurrentScene != null && _root.CurrentScene != Run && _root.CurrentScene != _scene) return;
            CurrentScene.Value.SetValue(_root, _scene);
            _scene.ProcessMode = _processMode;
            _scene.Visible = _visible;
            foreach (var button in _buttons.Where(GodotObject.IsInstanceValid))
            { button.Disable(); button.Enable(); }
        });
        Restore("progress", () =>
        {
            if (ReferenceEquals(SaveManager.Instance.Progress, _temporaryProgress))
                SaveManager.Instance.Progress = _progress;
        });
        Restore("local identity", () =>
        {
            if (LocalContext.NetId == _temporaryNetId || LocalContext.NetId == null)
                LocalContext.NetId = _netId;
        });
    }

    internal static void Restore(string step, Action restore)
    {
        try { restore(); }
        catch (Exception error) { GD.PushError($"[Spectator] Restore {step}: {error}"); }
    }

    private static IEnumerable<NButton> Buttons(Node node)
    {
        foreach (var child in node.GetChildren())
        {
            if (child is NButton button) yield return button;
            foreach (var nested in Buttons(child)) yield return nested;
        }
    }
}

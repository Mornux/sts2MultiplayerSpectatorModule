using Godot;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Settings;

namespace sts2MultiplayerSpectatorModule.Scripts;

// 自动进出战斗使用原版房间过渡；独立实例不打断原版载入，也不影响手动切图。
// Use native room transitions for automatic combat changes without interrupting loading or manual map toggles.
public partial class SpectatorCombat
{
    private NTransition? _sceneTransition;
    private Task _automaticSwitch = Task.CompletedTask;

    private Task SwitchAutomatic(Action change, int generation)
    {
        var previous = _automaticSwitch;
        return _automaticSwitch = SwitchAfter(previous, change, generation);
    }

    private async Task SwitchAfter(Task previous, Action change, int generation)
    {
        await previous;
        if (generation != _generation || !_session.Watching) return;
        if (SaveManager.Instance.PrefsSave.FastMode == FastModeType.Instant) { change(); return; }
        try
        {
            if (_sceneTransition == null)
            {
                var original = NGame.Instance!.Transition;
                _sceneTransition = (NTransition)original.Duplicate();
                _sceneTransition.Material = (Material)original.Material.Duplicate(true);
                var gradient = _sceneTransition.GetNode<Control>("GradientTransition");
                float initial = (float)HarmonyLib.AccessTools.Field(typeof(NTransition), "_initialGradientYPosition").GetValue(original)!;
                gradient.Position = new Vector2(gradient.Position.X, initial);
                var layer = new CanvasLayer { Layer = 200, ProcessMode = ProcessModeEnum.Always };
                AddChild(layer); layer.AddChild(_sceneTransition);
                _sceneTransition.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
            }
            _sceneTransition.Show();
            _sceneTransition.MouseFilter = Control.MouseFilterEnum.Stop;
            await _sceneTransition.RoomFadeOut();
            if (generation == _generation && _session.Watching) change();
            await _sceneTransition.RoomFadeIn();
        }
        catch (Exception error)
        {
            GD.PushWarning("[Spectator] Scene transition: " + error.Message);
            if (generation == _generation && _session.Watching) change();
        }
        finally
        {
            if (GodotObject.IsInstanceValid(_sceneTransition)) _sceneTransition!.Hide();
        }
    }
}


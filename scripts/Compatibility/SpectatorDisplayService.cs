using System.Reflection.Emit;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Multiplayer;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Runs;

namespace sts2MultiplayerSpectatorModule.Scripts;

// 原版只读界面只在自己的观战实例中借用显示服务，不替换全局跑局连接。
// Supply a display service only to spectator-owned native controls, without replacing the live connection.
internal static class SpectatorDisplayService
{
    private static readonly INetGameService Display = new NetSingleplayerGameService();

    internal static INetGameService For(RunManager manager, Node owner) =>
        SpectatorMap.Find(owner) != null ? Display : manager.NetService;

    internal static IEnumerable<CodeInstruction> ReplaceReads(IEnumerable<CodeInstruction> instructions)
    {
        var getter = AccessTools.PropertyGetter(typeof(RunManager), nameof(RunManager.NetService));
        var replacement = AccessTools.Method(typeof(SpectatorDisplayService), nameof(For));
        int replaced = 0;
        foreach (var instruction in instructions)
        {
            if (instruction.Calls(getter))
            {
                replaced++;
                // The manager is already on the stack; append this control to scope the replacement.
                var owner = new CodeInstruction(OpCodes.Ldarg_0);
                owner.labels.AddRange(instruction.labels);
                instruction.labels.Clear();
                owner.blocks.AddRange(instruction.blocks.Where(block => block.blockType != ExceptionBlockType.EndExceptionBlock));
                instruction.blocks.RemoveAll(block => block.blockType != ExceptionBlockType.EndExceptionBlock);
                yield return owner;
                instruction.opcode = OpCodes.Call;
                instruction.operand = replacement;
            }
            yield return instruction;
        }
        if (replaced == 0) throw new InvalidOperationException("Native display initialization no longer reads the expected network service.");
    }
}

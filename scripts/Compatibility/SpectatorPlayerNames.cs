using System.Reflection;
using System.Reflection.Emit;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Multiplayer;
using MegaCrit.Sts2.Core.Platform;

namespace sts2MultiplayerSpectatorModule.Scripts;

// 回放平台没有玩家昵称；只替换所属原版展示控件的名字读取，保留原版布局和格式化。
// Resolve names for replay-owned native controls without changing the run's platform or creature models.
[HarmonyPatch]
internal static class SpectatorPlayerNames
{
    private static IEnumerable<MethodBase> TargetMethods()
    {
        yield return AccessTools.Method(typeof(NMultiplayerPlayerExpandedState), nameof(NMultiplayerPlayerExpandedState._Ready));
        yield return AccessTools.Method(typeof(NCreatureStateDisplay), nameof(NCreatureStateDisplay.SetCreature));
        yield return AccessTools.Method(typeof(NCreatureStateDisplay), "RefreshValues");
    }

    private static string FormattedName(PlatformType platform, ulong id, Node owner) =>
        SpectatorCombat.PlayerDisplayName(owner, id)?.EscapeBbcodeTags() ?? PlatformUtil.GetPlayerName(platform, id);

    private static string CreatureName(Creature creature, Node owner) =>
        (creature.IsPlayer && creature.Player is { } player ? SpectatorCombat.PlayerDisplayName(owner, player.NetId) : null) ?? creature.Name;

    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        var formatted = AccessTools.Method(typeof(PlatformUtil), nameof(PlatformUtil.GetPlayerName));
        var creature = AccessTools.PropertyGetter(typeof(Creature), nameof(Creature.Name));
        int replaced = 0;
        foreach (var instruction in instructions)
        {
            string? replacement = instruction.Calls(formatted) ? nameof(FormattedName)
                : instruction.Calls(creature) ? nameof(CreatureName) : null;
            if (replacement != null)
            {
                var owner = new CodeInstruction(OpCodes.Ldarg_0);
                owner.labels.AddRange(instruction.labels);
                instruction.labels.Clear();
                owner.blocks.AddRange(instruction.blocks.Where(block => block.blockType != ExceptionBlockType.EndExceptionBlock));
                instruction.blocks.RemoveAll(block => block.blockType != ExceptionBlockType.EndExceptionBlock);
                yield return owner;
                instruction.opcode = OpCodes.Call;
                instruction.operand = AccessTools.Method(typeof(SpectatorPlayerNames), replacement);
                replaced++;
            }
            yield return instruction;
        }
        if (replaced == 0) throw new InvalidOperationException("Native player display no longer reads the expected name.");
    }
}

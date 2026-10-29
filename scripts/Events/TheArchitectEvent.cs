using HarmonyLib;
using MegaCrit.Sts2.Core.Models.Events;

namespace sts2MultiplayerSpectatorModule.Scripts.Events;

// 建筑师补充当前计分；场景内角色表演不在观众端执行。
// Include the Architect score without running scene actors on the viewer.
internal static class TheArchitectEvent
{
    internal static EventPlayerState Enrich(TheArchitect model, EventPlayerState state) => state with
    { Details = L.Wire("events.architect_score") + AccessTools.Field(typeof(TheArchitect), "_score").GetValue(model)
        + L.Wire("events.architect_hint") };
}

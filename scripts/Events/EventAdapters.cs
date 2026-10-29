using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Events;

namespace sts2MultiplayerSpectatorModule.Scripts.Events;

// 普通事件共用选项适配，仅将特殊事件交给各自的小文件。
// Share option handling and delegate only special events to small dedicated adapters.
internal static class EventAdapters
{
    internal static EventPlayerState Enrich(EventModel model, EventPlayerState state, bool local)
    {
        state = model switch
        {
            AncientEventModel ancient => AncientEvents.Enrich(ancient, state, local),
            CrystalSphere sphere => CrystalSphereEvent.Enrich(sphere, state, local),
            FakeMerchant merchant => FakeMerchantEvent.Enrich(merchant, state),
            TheArchitect architect => TheArchitectEvent.Enrich(architect, state),
            PunchOff => PunchOffEvent.Enrich(state),
            TheLanternKey => TheLanternKeyEvent.Enrich(state),
            _ => state
        };
        if (!state.Finished && model.CurrentOptions.Any(option => option.WasChosen))
            state = state with { Details = state.Details + L.Wire("events.processing") };
        return state;
    }
}

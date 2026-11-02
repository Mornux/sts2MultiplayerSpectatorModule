using Godot;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Events;

namespace sts2MultiplayerSpectatorModule.Scripts.Events;

// 普通事件共用选项适配，仅将特殊事件交给各自的小文件。
// Share option handling and delegate only special events to small dedicated adapters.
public static class EventAdapters
{
    private static readonly List<Registration> Registered = new();
    private sealed class Registration(string id, Func<EventModel, EventPlayerState, bool, EventPlayerState?> capture, int priority) : IDisposable
    {
        internal readonly string Id = id;
        internal readonly Func<EventModel, EventPlayerState, bool, EventPlayerState?> Capture = capture;
        internal readonly int Priority = priority;
        internal bool Warned;
        public void Dispose() { lock (Registered) Registered.Remove(this); }
    }

    /// <summary>
    /// Registers a read-only page adapter after native enrichment. Return null for unrelated models.
    /// Higher priorities run first; the first valid page wins. Dispose the handle to unregister.
    /// Adapters run on the game thread and must not advance events or invoke option actions.
    /// </summary>
    public static IDisposable Register(string id, Func<EventModel, EventPlayerState, bool, EventPlayerState?> capture, int priority = 0)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(capture);
        lock (Registered)
        {
            if (Registered.Any(adapter => adapter.Id == id)) throw new ArgumentException("Event adapter is already registered: " + id, nameof(id));
            var registration = new Registration(id, capture, priority);
            Registered.Add(registration);
            return registration;
        }
    }

    internal static EventPlayerState Enrich(EventModel model, EventPlayerState state, bool local, Action<Exception>? report = null)
    {
        try
        {
            var native = EnrichNative(model, state, local);
            if (!EventStateBounds.Valid(native)) throw new InvalidOperationException("Native event details exceed the spectator page limits.");
            state = native;
        }
        catch (Exception error) { report?.Invoke(error); }
        Registration[] adapters;
        lock (Registered) adapters = Registered.OrderByDescending(adapter => adapter.Priority).ToArray();
        foreach (var adapter in adapters)
        {
            try
            {
                // Give extensions their own arrays; a failed adapter cannot corrupt the fallback page.
                var page = adapter.Capture(model, state with
                {
                    Options = (EventOptionState[])state.Options.Clone(), Choices = (string[])state.Choices.Clone(),
                    Cells = state.Cells?.ToArray()
                }, local);
                if (page == null) continue;
                // Identity, history and votes remain owned by the authenticated capture session.
                page = page with
                {
                    Id = state.Id, Name = state.Name, EventId = state.EventId, Shared = state.Shared,
                    Choices = state.Choices, Vote = state.Vote, LocalPage = state.LocalPage,
                    Source = state.Source, VoteKey = state.VoteKey, Stale = state.Stale
                };
                if (!EventStateBounds.Valid(page)) throw new InvalidOperationException("Event adapter returned an invalid or oversized page.");
                return page;
            }
            catch (Exception error)
            {
                if (!adapter.Warned)
                {
                    adapter.Warned = true;
                    GD.PushWarning("[Spectator] Event adapter " + adapter.Id + ": " + error.Message);
                }
            }
        }
        return state;
    }

    private static EventPlayerState EnrichNative(EventModel model, EventPlayerState state, bool local)
    {
        if (model is CrystalSphere) return CrystalSphereEvent.Enrich(state);
        state = model switch
        {
            AncientEventModel ancient => AncientEvents.Enrich(ancient, state, local),
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

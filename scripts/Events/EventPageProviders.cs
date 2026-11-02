using Godot;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Runs;

namespace sts2MultiplayerSpectatorModule.Scripts.Events;

public sealed record EventCaptureContext(RunState Run, Player Player, EventModel? Model);

// Keep Instance stable during one visit; replace it when the event is entered again.
public sealed record ProvidedEventPage(object Instance, EventPlayerState Page);

/// <summary>Read-only local page sources, including events outside EventRoom.</summary>
public static class EventPageProviders
{
    private sealed class Registration(string id, Func<EventCaptureContext, ProvidedEventPage?> capture, int priority) : IDisposable
    {
        internal readonly string Id = id;
        internal readonly Func<EventCaptureContext, ProvidedEventPage?> Capture = capture;
        internal readonly int Priority = priority;
        internal bool Warned;
        public void Dispose() { lock (Registered) Registered.Remove(this); }
    }
    private static readonly List<Registration> Registered = new();

    /// <summary>
    /// Return null when no custom event is displayed. Called on the game thread; never advance
    /// events or execute choices. Choices must describe confirmed local actions, not inferred clicks.
    /// Dispose the registration when unloading. The first valid result wins, highest priority first.
    /// </summary>
    public static IDisposable Register(string id, Func<EventCaptureContext, ProvidedEventPage?> capture, int priority = 0)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(capture);
        lock (Registered)
        {
            if (Registered.Any(item => item.Id == id)) throw new ArgumentException("Duplicate page provider: " + id, nameof(id));
            var registration = new Registration(id, capture, priority);
            Registered.Add(registration);
            return registration;
        }
    }

    internal static ProvidedEventPage? Capture(EventCaptureContext context, string name)
    {
        Registration[] providers;
        lock (Registered) providers = Registered.OrderByDescending(item => item.Priority).ToArray();
        foreach (var provider in providers)
        {
            try
            {
                var result = provider.Capture(context);
                if (result == null) continue;
                if (result.Instance == null || !EventStateBounds.Valid(result.Page))
                    throw new InvalidOperationException("Provider page or visit identity is invalid.");
                return result with { Page = result.Page with
                {
                    Id = context.Player.NetId, Name = name, LocalPage = true, Source = EventPageSource.Provider, Stale = false,
                    Options = result.Page.Options.ToArray(), Choices = result.Page.Choices.ToArray(), Cells = result.Page.Cells?.ToArray()
                } };
            }
            catch (Exception error)
            {
                if (!provider.Warned)
                {
                    provider.Warned = true;
                    GD.PushWarning("[Spectator] Page provider " + provider.Id + ": " + error.Message);
                }
            }
        }
        return null;
    }
}

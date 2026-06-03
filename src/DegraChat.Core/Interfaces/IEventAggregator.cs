namespace DegraChat.Core.Interfaces;

/// <summary>
/// Simple in-process event bus for decoupled communication between modules.
/// </summary>
public interface IEventAggregator
{
    /// <summary>
    /// Subscribe to events of type T.
    /// </summary>
    IDisposable Subscribe<T>(Action<T> handler) where T : class;

    /// <summary>
    /// Publish an event to all subscribers.
    /// </summary>
    void Publish<T>(T eventArgs) where T : class;
}

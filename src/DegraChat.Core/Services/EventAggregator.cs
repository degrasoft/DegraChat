using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using DegraChat.Core.Interfaces;
using Serilog;

namespace DegraChat.Core.Services;

/// <summary>
/// Thread-safe in-process event bus implementation.
/// Uses reader-writer lock for concurrent subscribe/publish.
/// </summary>
public class EventAggregator : IEventAggregator, IDisposable
{
    private readonly Dictionary<Type, List<Delegate>> _handlers = new();
    private readonly ReaderWriterLockSlim _lock = new();
    private readonly ILogger _logger;

    public EventAggregator(ILogger logger)
    {
        _logger = logger.ForContext<EventAggregator>();
    }

    public IDisposable Subscribe<T>(Action<T> handler) where T : class
    {
        _lock.EnterWriteLock();
        try
        {
            var type = typeof(T);
            if (!_handlers.TryGetValue(type, out var list))
            {
                list = new List<Delegate>();
                _handlers[type] = list;
            }
            list.Add(handler);
            _logger.Debug("Subscribed handler to {EventType}", type.Name);
        }
        finally
        {
            _lock.ExitWriteLock();
        }

        return new Subscription<T>(this, handler);
    }

    public void Publish<T>(T eventArgs) where T : class
    {
        _lock.EnterReadLock();
        List<Delegate>? handlersCopy;
        try
        {
            if (!_handlers.TryGetValue(typeof(T), out var list))
                return;
            handlersCopy = list.ToList();
        }
        finally
        {
            _lock.ExitReadLock();
        }

        foreach (var handler in handlersCopy)
        {
            try
            {
                ((Action<T>)handler)(eventArgs);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error in event handler for {EventType}", typeof(T).Name);
            }
        }
    }

    internal void Unsubscribe<T>(Action<T> handler) where T : class
    {
        _lock.EnterWriteLock();
        try
        {
            if (_handlers.TryGetValue(typeof(T), out var list))
            {
                list.Remove(handler);
                _logger.Debug("Unsubscribed handler from {EventType}", typeof(T).Name);
            }
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }

    public void Dispose()
    {
        _lock.EnterWriteLock();
        try
        {
            _handlers.Clear();
        }
        finally
        {
            _lock.ExitWriteLock();
        }
        _lock.Dispose();
    }

    private class Subscription<T> : IDisposable where T : class
    {
        private readonly EventAggregator _aggregator;
        private readonly Action<T> _handler;
        private int _disposed;

        public Subscription(EventAggregator aggregator, Action<T> handler)
        {
            _aggregator = aggregator;
            _handler = handler;
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                _aggregator.Unsubscribe(_handler);
            }
        }
    }
}

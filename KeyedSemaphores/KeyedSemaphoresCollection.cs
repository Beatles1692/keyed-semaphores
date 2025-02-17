﻿using System;
using System.Collections.Concurrent;
using System.Linq;

namespace KeyedSemaphores
{
    internal sealed class KeyedSemaphoresCollection : IKeyedSemaphoreProvider, IKeyedSemaphoreOwner, IDisposable
    {
        private readonly ConcurrentDictionary<string, IKeyedSemaphore> _index;
        
        private bool _isDisposed;

        internal KeyedSemaphoresCollection()
        {
            _isDisposed = false;
            _index = new ConcurrentDictionary<string, IKeyedSemaphore>();
        }

        internal KeyedSemaphoresCollection(ConcurrentDictionary<string, IKeyedSemaphore> index)
        {
            _isDisposed = false;
            _index = index;
        }

        public IKeyedSemaphore Provide(string key)
        {
            if (_isDisposed)
                throw new ObjectDisposedException("This keyed semaphores collection has already been disposed");
            
            while (true)
            {
                // ReSharper disable once InconsistentlySynchronizedField
                if (_index.TryGetValue(key, out IKeyedSemaphore? existingKeyedSemaphore))
                {
                    lock (existingKeyedSemaphore)
                    {
                        if (existingKeyedSemaphore.Consumers > 0 && _index.ContainsKey(key))
                        {
                            existingKeyedSemaphore.IncreaseConsumers();
                            return existingKeyedSemaphore;
                        }
                    }
                }

                var newKeyedSemaphore = new InternalKeyedSemaphore(key, 1, this);

                // ReSharper disable once InconsistentlySynchronizedField
                if (_index.TryAdd(key, newKeyedSemaphore))
                {
                    return newKeyedSemaphore;
                }

                newKeyedSemaphore.InternalDispose();
            }
        }

        public void Return(IKeyedSemaphore keyedSemaphore)
        {
            if (keyedSemaphore == null) throw new ArgumentNullException(nameof(keyedSemaphore));

            // Do not throw ObjectDisposedException here, because this method is only called from InternalKeyedSemaphore.Dispose
            if (_isDisposed)
                return;

            lock (keyedSemaphore)
            {
                var remainingConsumers = keyedSemaphore.DecreaseConsumers();

                if (remainingConsumers == 0)
                {
                    if(!_index.TryRemove(keyedSemaphore.Key, out _))
                        throw new KeyedSemaphoresException($"Failed to remove a keyed semaphore because it has already been deleted by someone else! Key: {keyedSemaphore.Key}");

                    keyedSemaphore.InternalDispose();
                }
            }
        }

        public void Dispose()
        {
            if (_isDisposed)
                return;
            
            _isDisposed = true;

            while (!_index.IsEmpty)
            {
                var keys = _index.Keys.ToList();

                foreach (var key in keys)
                {
                    if (_index.TryRemove(key, out var keyedSemaphore))
                    {
                        keyedSemaphore.InternalDispose();
                    }
                }
            }
        }
    }
}
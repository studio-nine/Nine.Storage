﻿namespace Nine.Storage
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Nine.Storage.Syncing;

    public class Storage : IStorage, ISyncSource
    {
        private readonly LamportTimestamp timestamp = new LamportTimestamp();
        private readonly ConcurrentDictionary<Type, ConcurrentQueue<Action<object>>> initializers 
                   = new ConcurrentDictionary<Type, ConcurrentQueue<Action<object>>>();

        public IStorageProvider StorageProvider { get; private set; }

        /// <summary>
        /// When enabled, write operations will update IStorageObject.Time 
        /// to the latest lamport timestamp of this instance.
        /// </summary>
        public bool TimestampEnabled { get; set; }

        private long readCount = 0;
        private long writeCount = 0;

        public long ReadCount { get { return readCount; } }
        public long WriteCount { get { return writeCount; } }

        public double ReadWriteRatio
        {
            get { return writeCount > 0 ? 1.0 * readCount / writeCount : 1; }
        }

        public Storage(Type type) : this(x => Activator.CreateInstance(type.MakeGenericType(x)))
        { }

        public Storage(Func<Type, object> factory) : this(new TypedStorageProvider(factory))
        { }

        public Storage(IStorageProvider storageProvider)
        {
            if (storageProvider == null) throw new ArgumentNullException("storageProvider");

            this.StorageProvider = storageProvider;
        }

        public async Task<T> Get<T>(string key)
        {
            Interlocked.Increment(ref readCount);
            var storage = await StorageProvider.GetAsync<T>().ConfigureAwait(false);
            EnsureInitialized<T>(storage);
            return await storage.Get(key).ConfigureAwait(false);
        }

        public async Task<IEnumerable<T>> Range<T>(string minKey, string maxKey, int? maxCount = null)
        {
            Interlocked.Increment(ref readCount);
            var storage = await StorageProvider.GetAsync<T>().ConfigureAwait(false);
            EnsureInitialized<T>(storage);
            return await storage.Range(minKey, maxKey, maxCount).ConfigureAwait(false);
        }

        public async Task<bool> Add<T>(string key, T value)
        {
            if (value == null) throw new ArgumentNullException("value");

            if (Interlocked.Increment(ref writeCount) > 100000)
            {
                Interlocked.Exchange(ref writeCount, 1);
                Interlocked.Exchange(ref readCount, 0);
            }

            var storage = await StorageProvider.GetAsync<T>().ConfigureAwait(false);
            EnsureInitialized<T>(storage);
            if (TimestampEnabled)
            {
                var timestamped = value as ITimestamped;
                // TODO:
                if (timestamped != null && timestamped.Time == default(DateTime)) timestamped.Time = timestamp.Next();
            }
            return await storage.Add(key, value).ConfigureAwait(false);
        }

        public async Task Put<T>(string key, T value)
        {
            if (value == null) throw new ArgumentNullException("value");

            if (Interlocked.Increment(ref writeCount) > 100000)
            {
                Interlocked.Exchange(ref writeCount, 1);
                Interlocked.Exchange(ref readCount, 0);
            }

            var storage = await StorageProvider.GetAsync<T>().ConfigureAwait(false);
            EnsureInitialized<T>(storage);
            if (TimestampEnabled)
            {
                var timestamped = value as ITimestamped;
                if (timestamped != null && timestamped.Time == default(DateTime)) timestamped.Time = timestamp.Next();
            }
            await storage.Put(key, value).ConfigureAwait(false);
        }

        public async Task<bool> Delete<T>(string key)
        {
            if (Interlocked.Increment(ref writeCount) > 100000)
            {
                Interlocked.Exchange(ref writeCount, 1);
                Interlocked.Exchange(ref readCount, 0);
            }

            var storage = await StorageProvider.GetAsync<T>().ConfigureAwait(false);
            EnsureInitialized<T>(storage);
            return await storage.Delete(key).ConfigureAwait(false);
        }

        public IDisposable On<T>(Action<Delta<T>> action)
        {
            var result = new OnDisposable();
            initializers.GetOrAdd(typeof(T), type => new ConcurrentQueue<Action<object>>()).Enqueue(state =>
            {
                // Ensure we are always invoked before any other operations occured on the storage.
                var sync = state as ISyncSource<T>;
                if (sync != null && !result.disposed)
                {
                    result.inner = sync.On(action);
                }
            });
            EnsureInitialized<T>();
            return result;
        }

        public IDisposable On<T>(string key, Action<Delta<T>> action)
        {
            var result = new OnDisposable();
            initializers.GetOrAdd(typeof(T), type => new ConcurrentQueue<Action<object>>()).Enqueue(state =>
            {
                // Ensure we are always invoked before any other operations occured on the storage.
                var sync = state as ISyncSource<T>;
                if (sync != null && !result.disposed)
                {
                    result.inner = sync.On(key, action);
                }
            });
            EnsureInitialized<T>();
            return result;
        }

        private async void EnsureInitialized<T>()
        {
            EnsureInitialized<T>(await StorageProvider.GetAsync<T>().ConfigureAwait(false));
        }

        private void EnsureInitialized<T>(object state)
        {
            Action<object> action;
            ConcurrentQueue<Action<object>> queue;
            if (initializers.TryGetValue(typeof(T), out queue))
            {
                while (queue.TryDequeue(out action)) action(state);
            }
        }

        class OnDisposable : IDisposable
        {
            public bool disposed;
            public IDisposable inner;

            public void Dispose()
            {
                disposed = true;
                if (inner != null) inner.Dispose();
            }
        }

        class TypedStorageProvider : StorageProviderBase
        {
            private readonly Func<Type, object> factory;

            public TypedStorageProvider(Func<Type, object> factory)
            {
                if (factory == null) throw new ArgumentNullException("factory");

                this.factory = factory;
            }

            protected override Task<IStorage<T>> CreateAsync<T>()
            {
                return Task.FromResult((IStorage<T>)factory(typeof(T)));
            }
        }
    }
}
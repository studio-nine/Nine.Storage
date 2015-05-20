﻿namespace Nine.Storage
{
    using System;
    using System.Collections.Generic;
    using System.ComponentModel;
    using System.Linq;
    using System.Threading.Tasks;

    [EditorBrowsable(EditorBrowsableState.Never)]
    public static class StorageExtensions
    {
        public static Task<T> Get<T>(this IStorage<T> storage, params object[] keyComponents) where T : class, IKeyed, new()
        {
            return storage.Get(StorageKey.Get(keyComponents));
        }

        public static Task<IEnumerable<T>> List<T>(this IStorage<T> storage, params object[] keyComponents) where T : class, IKeyed, new()
        {
            var prefix = StorageKey.Get(keyComponents);
            return storage.Range(prefix, StorageKey.Increment(prefix), null);
        }

        public static Task<IEnumerable<T>> Page<T>(this IStorage<T> storage, int? count, params object[] keyComponents) where T : class, IKeyed, new()
        {
            var prefix = StorageKey.Get(keyComponents);
            return storage.Range(prefix, StorageKey.Increment(prefix), count);
        }

        public static IAsyncEnumerator<T> All<T>(this IStorage<T> storage, int? batchSize = 1000) where T : class, IKeyed, new()
        {
            return All(storage, null, batchSize);
        }

        public static IAsyncEnumerator<T> All<T>(this IStorage<T> storage, string prefix, int? batchSize = 1000) where T : class, IKeyed, new()
        {
            // TODO: All does not work well with BatchedTableStorage...
            var continuation = prefix;
            var end = StorageKey.Increment(continuation);
            return AsyncEnumerator.Create(new Func<Task<AsyncEnumerationResult<T>>>(async () =>
            {
                var batch = await storage.Range(continuation, end, batchSize);
                var hasMore = batchSize != null && batch.Any();
                if (hasMore)
                {
                    continuation = StorageKey.Increment(batch.Last().GetKey());
                }
                return new AsyncEnumerationResult<T> { HasMore = hasMore, Items = batch };
            }));
        }

        public static Task Delete<T>(this IStorage<T> storage, T value) where T : class, IKeyed, new()
        {
            return storage.Delete(value.GetKey());
        }

        public static Task<T> Get<T>(this IStorage storage, params object[] keyComponents) where T : class, IKeyed, new()
        {
            return storage.Get<T>(StorageKey.Get(keyComponents));
        }

        public static Task<IEnumerable<T>> List<T>(this IStorage storage, params object[] keyComponents) where T : class, IKeyed, new()
        {
            var prefix = StorageKey.Get(keyComponents);
            return storage.Range<T>(prefix, StorageKey.Increment(prefix), null);
        }

        public static Task<IEnumerable<T>> Page<T>(this IStorage storage, int? count, params object[] keyComponents) where T : class, IKeyed, new()
        {
            var prefix = StorageKey.Get(keyComponents);
            return storage.Range<T>(prefix, StorageKey.Increment(prefix), count);
        }

        public static IAsyncEnumerator<T> All<T>(this IStorage storage, int? batchSize = 1000) where T : class, IKeyed, new()
        {
            return All<T>(storage, null, batchSize);
        }

        public static IAsyncEnumerator<T> All<T>(this IStorage storage, string prefix, int? batchSize = 1000) where T : class, IKeyed, new()
        {
            var continuation = prefix;
            var end = StorageKey.Increment(continuation);
            return AsyncEnumerator.Create(new Func<Task<AsyncEnumerationResult<T>>>(async () =>
            {
                var batch = await storage.Range<T>(continuation, end, batchSize);
                var hasMore = batchSize != null && batch.Any();
                if (hasMore)
                {
                    continuation = StorageKey.Increment(batch.Last().GetKey());
                }
                return new AsyncEnumerationResult<T> { HasMore = hasMore, Items = batch };
            }));
        }

        public static Task Delete<T>(this IStorage storage, T value) where T : class, IKeyed, new()
        {
            return storage.Delete<T>(value.GetKey());
        }

        public static Task<bool> Patch<T>(this IStorage storage, string key, Action<T> action) where T : class, IKeyed, new()
        {
            return Patch(storage, key, x => true, action);
        }

        public static async Task<bool> Patch<T>(this IStorage storage, string key, Func<T, bool> predicate, Action<T> action) where T : class, IKeyed, new()
        {
            var existing = await storage.Get<T>(key).ConfigureAwait(false);
            if (existing == null) return false;
            if (predicate != null && !predicate(existing)) return false;

            var cloned = ObjectHelper<T>.Clone(existing);
            action(cloned);
            await storage.Put(cloned).ConfigureAwait(false);
            return true;
        }
    }
}
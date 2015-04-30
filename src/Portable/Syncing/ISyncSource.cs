﻿namespace Nine.Storage
{
    using System;
    using System.ComponentModel;
    using System.Threading;

    public enum DeltaAction
    {
        Add,
        Put,
        Remove,
    }

    public struct Delta<T>
    {
        public DeltaAction Action;
        public string Key;
        public T Value;

        public Delta(DeltaAction action, string key, T value = default(T))
        {
            this.Action = action;
            this.Key = key;
            this.Value = value;
        }

        public bool TryMerge(ref Delta<T> delta)
        {
            if (delta.Key != Key) return false;
            if (Action == DeltaAction.Remove && delta.Action == DeltaAction.Remove) return true;

            Action = DeltaAction.Put;
            Value = delta.Value;
            return true;
        }

        public override string ToString() => $"{ Action } { typeof(T).Name } { Key }";
    }

    /// <summary>
    /// Enables change notification
    /// </summary>
    public interface ISyncSource<T> where T : class, IKeyed, new()
    {
        IDisposable On(Action<Delta<T>> action);
        IDisposable On(string key, Action<Delta<T>> action);
    }

    /// <summary>
    /// Enables change notification
    /// </summary>
    public interface ISyncSource
    {
        IDisposable On<T>(Action<Delta<T>> action) where T : class, IKeyed, new();
        IDisposable On<T>(string key, Action<Delta<T>> action) where T : class, IKeyed, new();
    }

    [EditorBrowsable(EditorBrowsableState.Never)]
    public static class SyncSourceExtensions
    {
        class Defaults<T> where T : class, new() { public static readonly T Value = new T(); }
        
        public static IDisposable On<T>(this ISyncSource source, string key, Action<T> action) where T : class, IKeyed, new()
        {
            return source.On<T>(key, change => action(change.Value ?? Defaults<T>.Value));
        }

        public static IDisposable On<T>(this ISyncSource source, Action<T> action) where T : class, IKeyed, new()
        {
            return source.On<T>(change => action(change.Value ?? Defaults<T>.Value));
        }

        public static void On<T>(this IStorage storage, Action<T> action) where T : class, IKeyed, new()
        {
            var source = storage as ISyncSource;
            if (source == null) throw new ArgumentException("storage", "storage needs to be a sync source");

            action = PostToSynchronizationContext(action);
            source.On<T>(x => action(x ?? Defaults<T>.Value));
        }

        public static async void On<T>(this IStorage storage, string key, Action<T> action) where T : class, IKeyed, new()
        {
            var source = storage as ISyncSource;
            if (source == null) throw new ArgumentException("storage", "storage needs to be a sync source");

            action = PostToSynchronizationContext(action);
            source.On<T>(key, x => action(x ?? Defaults<T>.Value));
            
            action(await storage.Get<T>(key).ConfigureAwait(false) ?? Defaults<T>.Value);
        }

        public static async void On<T>(this IStorage storage, string key, Action<T, T> action) where T : class, IKeyed, new()
        {
            var source = storage as ISyncSource;
            if (source == null) throw new ArgumentException("storage", "storage needs to be a sync source");

            T oldValue = Defaults<T>.Value;

            action = PostToSynchronizationContext(action);
            source.On<T>(key, x =>
            {
                var copy = ObjectHelper<T>.Clone(x);
                action(x ?? Defaults<T>.Value, oldValue);
                oldValue = copy;
            });

            var value = await storage.Get<T>(key).ConfigureAwait(false) ?? Defaults<T>.Value;
            oldValue = ObjectHelper<T>.Clone(value);
            action(value ?? Defaults<T>.Value, Defaults<T>.Value);
        }

        public static async void On<T>(this IStorage storage, string key, Func<T, object> watch, Action<T> action) where T : class, IKeyed, new()
        {
            var source = storage as ISyncSource;
            if (source == null) throw new ArgumentException("storage", "storage needs to be a sync source");

            T oldValue = Defaults<T>.Value;

            action = PostToSynchronizationContext(action);
            source.On<T>(key, x =>
            {
                var copy = ObjectHelper<T>.Clone(x);
                if (watch != null && !Equals(watch(x ?? Defaults<T>.Value), watch(oldValue ?? Defaults<T>.Value)))
                {
                    action(x ?? Defaults<T>.Value);
                }
                oldValue = copy;
            });

            var value = await storage.Get<T>(key).ConfigureAwait(false) ?? Defaults<T>.Value;
            oldValue = ObjectHelper<T>.Clone(value);
            action(value ?? Defaults<T>.Value);
        }

        private static Action<T> PostToSynchronizationContext<T>(Action<T> action) where T : class, IKeyed, new()
        {
            var syncContext = SynchronizationContext.Current;
            if (syncContext == null) return action;
            return new Action<T>(target => syncContext.Post(x => action(target), null));
        }

        private static Action<T, T> PostToSynchronizationContext<T>(Action<T, T> action) where T : class, IKeyed, new()
        {
            var syncContext = SynchronizationContext.Current;
            if (syncContext == null) return action;
            return new Action<T, T>((a, b) => syncContext.Post(x => action(a, b), null));
        }
    }
}

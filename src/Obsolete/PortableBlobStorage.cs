﻿namespace Nine.Storage.Blobs
{
    using System;
    using System.Collections.Concurrent;
    using System.Diagnostics;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;
    using PCLStorage;

    public class PortableBlobStorage : IBlobStorage
    {
        private readonly string baseDirectory;
        private readonly ConcurrentDictionary<string, Lazy<Task<string>>> puts = new ConcurrentDictionary<string, Lazy<Task<string>>>(StringComparer.OrdinalIgnoreCase);

        public string LocalStoragePath => FileSystem.Current.LocalStorage.Path;

        public PortableBlobStorage(string baseDirectory = "Blobs")
        {
            if (baseDirectory != null && Path.IsPathRooted(baseDirectory))
            {
                throw new NotSupportedException("baseDirectory cannot be rooted.");
            }

            this.baseDirectory = baseDirectory;
        }

        public async Task<bool> Exists(string key)
        {
            if (string.IsNullOrEmpty(key)) return false;
            return await GetFileAsync(key).ConfigureAwait(false) != null;
        }

        public string GetUri(string key)
        {
            if (string.IsNullOrEmpty(key)) return null;
            var file = GetFileAsync(key).Result;
            return file != null ? file.Path : null;
        }

        public async Task<Stream> Get(string key, IProgress<ProgressInBytes> progress = null, CancellationToken cancellationToken = default(CancellationToken))
        {
            if (string.IsNullOrEmpty(key)) return null;
            var file = await GetFileAsync(key, cancellationToken).ConfigureAwait(false);
            return file != null ? await file.OpenAsync(PCLStorage.FileAccess.Read, cancellationToken).ConfigureAwait(false) : null;
        }

        public async Task<string> Put(string key, Stream stream, IProgress<ProgressInBytes> progress = null, CancellationToken cancellationToken = default(CancellationToken))
        {
            if (stream == null || key == null) return null;

            var result = await puts.GetOrAdd(key, k => new Lazy<Task<string>>(() => PutCoreAsync(stream, key, progress), true)).Value.ConfigureAwait(false);

            Lazy<Task<string>> temp;
            puts.TryRemove(key, out temp);
            return result;
        }

        private async Task<string> PutCoreAsync(Stream stream, string key, IProgress<ProgressInBytes> progress)
        {
            var tempId = "." + Guid.NewGuid().ToString("N").Substring(0, 5) + ".tmp";
            var tempFile = await CreateFileIfNotExistAsync(key + tempId).ConfigureAwait(false);
            using (var output = await tempFile.OpenAsync(PCLStorage.FileAccess.ReadAndWrite).ConfigureAwait(false))
            {
                await stream.CopyToAsync(output).ConfigureAwait(false);
            }

            // Check again if someone else has already got the file
            if (await GetFileAsync(key) != null) return key;

            var failed = true;

            try
            {
                var filename = tempFile.Path.Substring(0, tempFile.Path.Length - tempId.Length);
                await tempFile.MoveAsync(filename).ConfigureAwait(false);
                failed = false;
            }
            // This can happen if multiple threads trying to do the remove at the same time.
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            catch (Exception e) { Debug.WriteLine(e); }

            if (failed)
            {
                try
                {
                    await tempFile.DeleteAsync().ConfigureAwait(false);
                }
                catch (Exception e)
                {
                    Debug.WriteLine(e);
                }
            }

            return key;
        }

        public async Task Delete(string key)
        {
            var file = await GetFileAsync(key).ConfigureAwait(false);
            if (file != null) await file.DeleteAsync();
        }

        public async Task DeleteAll()
        {
            var path = FileSystem.Current.LocalStorage.Path;
            if (!string.IsNullOrEmpty(baseDirectory))
            {
                path = PortablePath.Combine(path, baseDirectory);
            }

            var folder = await FileSystem.Current.GetFolderFromPathAsync(path).ConfigureAwait(false);
            if (folder != null) await folder.DeleteAsync().ConfigureAwait(false);
        }

        private async Task<IFile> GetFileAsync(string key, CancellationToken cancellationToken = default(CancellationToken))
        {
            try
            {
                var path = GetUriCore(key);
                if (string.IsNullOrEmpty(path)) return null;
                return await FileSystem.Current.GetFileFromPathAsync(path, cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                return null;
            }
        }

        private async Task<IFile> CreateFileIfNotExistAsync(string key)
        {
            if (key == null || key.Length < 2)
            {
                throw new ArgumentException("key");
            }

            var path = key.Substring(0, 2);
            if (!string.IsNullOrEmpty(baseDirectory))
            {
                path = PortablePath.Combine(baseDirectory, path);
            }

            var storage = FileSystem.Current.LocalStorage;
            await storage.CreateFolderAsync(path, CreationCollisionOption.OpenIfExists).ConfigureAwait(false);

            var filename = PortablePath.Combine(path, key);
            return await storage.CreateFileAsync(filename, CreationCollisionOption.ReplaceExisting).ConfigureAwait(false);
        }

        private string GetUriCore(string key)
        {
            if (string.IsNullOrEmpty(key) || key.Length < 2) return null;

            var path = PortablePath.Combine(key.Substring(0, 2), key);
            if (!string.IsNullOrEmpty(baseDirectory))
            {
                path = PortablePath.Combine(baseDirectory, path);
            }

            return PortablePath.Combine(FileSystem.Current.LocalStorage.Path, path);
        }
    }
}
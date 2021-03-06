﻿namespace Nine.Storage.Blobs
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Threading.Tasks;
    using Xunit;

    public abstract class BlobStorageSpec<TData> : ITestFactoryData<IBlobStorage> where TData : ITestFactoryData<IBlobStorage>, new()
    {
        public static IEnumerable<object[]> Data = new TestFactoryDimension<TData, IBlobStorage>();

        public virtual bool CanDelete => true;

        public abstract IEnumerable<ITestFactory<IBlobStorage>> GetData();

        [Theory, MemberData("Data")]
        public async Task get_null_key_returns_null_content(ITestFactory<IBlobStorage> storageFactory)
        {
            var storage = storageFactory.Create();
            Assert.Null(storage.GetUri(null));
            Assert.Null(storage.GetUri(""));
            Assert.Null(await storage.Get(null));
            Assert.Null(await storage.Get(""));

            // TODO: Test for special characters in keys
        }

        [Theory, MemberData("Data")]
        public async Task store_binaries_into_blob_storage(ITestFactory<IBlobStorage> storageFactory)
        {
            var storage = storageFactory.Create();

            Assert.Null(await storage.Get((Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N")).Substring(0, 40)));

            var bytes = Enumerable.Range(0, 1024).Select(x => (byte)x).ToArray();
            var stream = new MemoryStream(bytes);

            stream.Seek(0, SeekOrigin.Begin);
            var key = await PutStorage(storage, stream);

            Assert.True(await storage.Exists(key));

            using (var read = await storage.Get(key))
            {
                var stored = await read.ReadBytesAsync();
                Assert.Equal(bytes, stored);
            }

            if (CanDelete)
            {
                await storage.Delete(key);
                Assert.False(await storage.Exists(key));
                Assert.Null(await storage.Get(key));
            }
        }

        [Theory, MemberData("Data")]
        public async Task should_not_dispose_input_stream(ITestFactory<IBlobStorage> storageFactory)
        {
            var storage = storageFactory.Create();

            var random = new Random();
            var bytes = Enumerable.Range(0, 11).Select(x => (byte)random.Next(255)).ToArray();
            var stream = new MemoryStream(bytes);

            for (int i = 0; i < 2; i++)
            {
                stream.Seek(0, SeekOrigin.Begin);
                var key = await PutStorage(storage, stream);
                var read = await storage.Get(key);
                var stored = await read.ReadBytesAsync();

                Assert.Equal(bytes, stored);
            }
        }

        [Theory, MemberData("Data")]
        public async Task content_addressable_storage_test(ITestFactory<IBlobStorage> storageFactory)
        {
            var storage = new ContentAddressableStorage(storageFactory.Create());
            if (storage == null) return;

            var bytes = Enumerable.Range(0, 1024).Select(x => (byte)x).ToArray();
            var stream = new MemoryStream(bytes);

            stream.Seek(0, SeekOrigin.Begin);
            var sha = await storage.Put(stream);

            Assert.Equal("5b00669c480d5cffbdfa8bdba99561160f2d1b77", sha);
            Assert.True(await storage.Exists(sha));

            var read = await storage.Get(sha);
            var stored = await read.ReadBytesAsync();

            Assert.True(bytes.SequenceEqual(stored));
        }

        private async Task<string> PutStorage(IBlobStorage storage, MemoryStream stream)
        {
            var cas = storage as IContentAddressableStorage;
            if (cas != null) return await cas.Put(stream);
            var name = Guid.NewGuid().ToString();
            var key = await storage.Put(name, stream);
            Assert.Equal(name, key);
            return key;
        }
    }
}

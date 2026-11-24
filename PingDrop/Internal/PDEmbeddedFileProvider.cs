using Microsoft.Extensions.FileProviders;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PingDrop.Internal
{
    internal static class PDEmbeddedFileProvider
    {
        // The embedded assets never change at runtime, so each is read and decoded once.
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, Task<string>> _cache = new();

        public static Task<string> ReadAsync(string fileName) => _cache.GetOrAdd(fileName, ReadCoreAsync);

        private static async Task<string> ReadCoreAsync(string fileName)
        {
            var resourceName = $"PingDrop.Dashboard.{fileName}";

            var assembly = typeof(PDEmbeddedFileProvider).Assembly;

            await using var stream = assembly.GetManifestResourceStream(resourceName)!;

            using var reader = new StreamReader(stream);

            return await reader.ReadToEndAsync();
        }
    }
}

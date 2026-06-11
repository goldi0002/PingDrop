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
        public static async Task<string> ReadAsync(string fileName)
        {
            var resourceName = $"PingDrop.Dashboard.{fileName}";

            var assembly = typeof(PDEmbeddedFileProvider).Assembly;

            await using var stream = assembly.GetManifestResourceStream(resourceName)!;

            using var reader = new StreamReader(stream);

            return await reader.ReadToEndAsync();
        }
    }
}

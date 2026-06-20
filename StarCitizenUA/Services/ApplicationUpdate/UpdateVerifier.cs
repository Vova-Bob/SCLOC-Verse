using StarCitizenUA.Interfaces;
using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace StarCitizenUA.Services.ApplicationUpdate
{
    public class UpdateVerifier : IUpdateVerifier
    {
        public async Task<bool> VerifyAsync(
            string filePath,
            string? expectedChecksum,
            CancellationToken cancellationToken = default)
        {
            if (filePath is null)
                throw new ArgumentNullException(nameof(filePath));
            if (string.IsNullOrWhiteSpace(filePath))
                throw new ArgumentException("File path cannot be empty.", nameof(filePath));

            cancellationToken.ThrowIfCancellationRequested();

            if (string.IsNullOrWhiteSpace(expectedChecksum))
                return false;

            if (!File.Exists(filePath))
                return false;

            var trimmedChecksum = expectedChecksum.Trim();

            await using var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var sha256 = SHA256.Create();
            var hash = await sha256.ComputeHashAsync(fileStream, cancellationToken).ConfigureAwait(false);

            var actualChecksum = BitConverter.ToString(hash).Replace("-", string.Empty);

            return string.Equals(actualChecksum, trimmedChecksum, StringComparison.OrdinalIgnoreCase);
        }
    }
}

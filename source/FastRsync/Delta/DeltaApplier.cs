using System;
using System.Collections;
using System.IO;
using System.Threading.Tasks;
using FastRsync.Exceptions;

namespace FastRsync.Delta
{
    public class DeltaApplier
    {
        public DeltaApplier()
        {
            SkipHashCheck = false;
        }

        public bool SkipHashCheck { get; set; }

        public void Apply(Stream basisFileStream, IDeltaReader delta, Stream outputStream)
        {
            var buffer = new byte[4 * 1024 * 1024];

            delta.Apply(
                writeData: (data) => outputStream.Write(data, 0, data.Length),
                copy: (startPosition, length) =>
                {
                    basisFileStream.Seek(startPosition, SeekOrigin.Begin);

                    int read;
                    long soFar = 0;
                    while ((read = basisFileStream.Read(buffer, 0, (int)Math.Min(length - soFar, buffer.Length))) > 0)
                    {
                        soFar += read;
                        outputStream.Write(buffer, 0, read);
                    }
                });

            if (!SkipHashCheck)
            {
                if (!HashCheck(delta, outputStream))
                {
                    throw new UsageException(
                        $"Verification of the patched file failed. The {delta.HashAlgorithm.Name} hash of the patch result file, and the file that was used as input for the delta, do not match. This can happen if the basis file changed since the signatures were calculated.");
                }
            }
        }

        public async Task ApplyAsync(Stream basisFileStream, IDeltaReader delta, Stream outputStream)
        {
            var buffer = new byte[4 * 1024 * 1024];

            await delta.ApplyAsync(
                writeData: async (data) => await outputStream.WriteAsync(data, 0, data.Length).ConfigureAwait(false),
                copy: async (startPosition, length) =>
                {
                    basisFileStream.Seek(startPosition, SeekOrigin.Begin);

                    int read;
                    long soFar = 0;
                    while ((read = await basisFileStream.ReadAsync(buffer, 0, (int)Math.Min(length - soFar, buffer.Length)).ConfigureAwait(false)) > 0)
                    {
                        soFar += read;
                        await outputStream.WriteAsync(buffer, 0, read).ConfigureAwait(false);
                    }
                }).ConfigureAwait(false);

            if (!SkipHashCheck)
            {
                if (!await HashCheckAsync(delta, outputStream).ConfigureAwait(false))
                {
                    throw new UsageException(
                        $"Verification of the patched file failed. The {delta.HashAlgorithm.Name} hash of the patch result file, and the file that was used as input for the delta, do not match. This can happen if the basis file changed since the signatures were calculated.");
                }
            }
        }

        public bool HashCheck(IDeltaReader delta, Stream outputStream)
        {
            outputStream.Seek(0, SeekOrigin.Begin);

            var sourceFileHash = delta.ExpectedHash;
            var algorithm = delta.HashAlgorithm;

            var actualHash = algorithm.ComputeHash(outputStream);

            return StructuralComparisons.StructuralEqualityComparer.Equals(sourceFileHash, actualHash);
        }

        public async Task<bool> HashCheckAsync(IDeltaReader delta, Stream outputStream)
        {
            outputStream.Seek(0, SeekOrigin.Begin);

            var sourceFileHash = delta.ExpectedHash;
            var algorithm = delta.HashAlgorithm;

            var actualHash = await algorithm.ComputeHashAsync(outputStream).ConfigureAwait(false);

            return StructuralComparisons.StructuralEqualityComparer.Equals(sourceFileHash, actualHash);
        }
    }
}
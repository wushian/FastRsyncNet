using System;
using System.Collections;
using System.IO;
using System.Threading.Tasks;
using FastRsync.Core;
using FastRsync.Diagnostics;
using FastRsync.Exceptions;
using FastRsync.Hash;

namespace FastRsync.Delta
{
    public class BinaryDeltaReader : IDeltaReader
    {
        private readonly BinaryReader reader;
        private readonly IProgress<ProgressReport> progressReport;
        private byte[] expectedHash;
        private IHashAlgorithm hashAlgorithm;
        private bool hasReadMetadata;

        public BinaryDeltaReader(Stream stream, IProgress<ProgressReport> progressHandler)
        {
            this.reader = new BinaryReader(stream);
            this.progressReport = progressHandler;
        }

        public byte[] ExpectedHash
        {
            get
            {
                EnsureMetadata();
                return expectedHash;
            }
        }

        public IHashAlgorithm HashAlgorithm
        {
            get
            {
                EnsureMetadata();
                return hashAlgorithm;
            }
        }

        private void EnsureMetadata()
        {
            if (hasReadMetadata)
                return;

            reader.BaseStream.Seek(0, SeekOrigin.Begin);

            var first = reader.ReadBytes(BinaryFormat.DeltaHeader.Length);
            if (!StructuralComparisons.StructuralEqualityComparer.Equals(first, BinaryFormat.DeltaHeader))
                throw new CorruptFileFormatException("The delta file appears to be corrupt.");

            var version = reader.ReadByte();
            if (version != BinaryFormat.Version)
                throw new CorruptFileFormatException("The delta file uses a newer file format than this program can handle.");

            var hashAlgorithmName = reader.ReadString();
            hashAlgorithm = SupportedAlgorithms.Hashing.Create(hashAlgorithmName);

            var hashLength = reader.ReadInt32();
            expectedHash = reader.ReadBytes(hashLength);
            var endOfMeta = reader.ReadBytes(BinaryFormat.EndOfMetadata.Length);
            if (!StructuralComparisons.StructuralEqualityComparer.Equals(BinaryFormat.EndOfMetadata, endOfMeta))
                throw new CorruptFileFormatException("The signature file appears to be corrupt.");

            hasReadMetadata = true;
        }

        public void Apply(
            Action<byte[]> writeData, 
            Action<long, long> copy)
        {
            var fileLength = reader.BaseStream.Length;

            EnsureMetadata();

            while (reader.BaseStream.Position != fileLength)
            {
                var b = reader.ReadByte();

                progressReport?.Report(new ProgressReport
                {
                    Operation = ProgressOperationType.ApplyingDelta,
                    CurrentPosition = reader.BaseStream.Position,
                    Total = fileLength
                });

                if (b == BinaryFormat.CopyCommand)
                {
                    var start = reader.ReadInt64();
                    var length = reader.ReadInt64();
                    copy(start, length);
                }
                else if (b == BinaryFormat.DataCommand)
                {
                    var length = reader.ReadInt64();
                    long soFar = 0;
                    while (soFar < length)
                    {
                        var bytes = reader.ReadBytes((int) Math.Min(length - soFar, 1024*1024*4));
                        soFar += bytes.Length;
                        writeData(bytes);
                    }
                }
            }
        }

        public async Task ApplyAsync(
            Func<byte[], Task> writeData,
            Func<long, long, Task> copy)
        {
            var fileLength = reader.BaseStream.Length;

            EnsureMetadata();

            while (reader.BaseStream.Position != fileLength)
            {
                var b = reader.ReadByte();

                progressReport?.Report(new ProgressReport
                {
                    Operation = ProgressOperationType.ApplyingDelta,
                    CurrentPosition = reader.BaseStream.Position,
                    Total = fileLength
                });

                if (b == BinaryFormat.CopyCommand)
                {
                    var start = reader.ReadInt64();
                    var length = reader.ReadInt64();
                    await copy(start, length).ConfigureAwait(false);
                }
                else if (b == BinaryFormat.DataCommand)
                {
                    var length = reader.ReadInt64();
                    long soFar = 0;
                    while (soFar < length)
                    {
                        var bytes = reader.ReadBytes((int)Math.Min(length - soFar, 1024 * 1024 * 4));
                        soFar += bytes.Length;
                        await writeData(bytes).ConfigureAwait(false);
                    }
                }
            }
        }
    }
}
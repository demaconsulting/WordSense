using System.Buffers.Binary;
using System.Security.Cryptography;

namespace WordSense.Builder.Tests;

/// <summary>
///     Tests that the embedding cache is read from a stream, record by record, so that a cache file of more than 2 GiB
///     (the limit of one array) loads.
/// </summary>
public sealed class EmbeddingCacheStreamTests
{
    private const int Dimension = 65536;
    private const int Records = 4100;
    private const int HeaderSize = 4 + 1 + 32 + 4 + 4 + 8;
    private const int RecordSize = 32 + (Dimension * sizeof(double)) + 8;

    /// <summary>
    ///     A cache file of more than 2 GiB loads without a buffer of the size of the file: the stream is read in the
    ///     chunks of one record.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_EmbeddingCache_OpenStream_FileLargerThanTwoGiB_LoadsRecordByRecord()
    {
        // Arrange: 4,100 records of 65,536 numbers (about 2.15 GB); the first and the last record are valid, the others have no valid checksum
        var digest = SHA256.HashData("digest"u8);
        using var stream = new SyntheticCacheStream(digest);

        // Act
        var cache = EmbeddingCache.OpenStream("unused", digest, stream);

        // Assert: both valid records are there, every other record is counted as damaged, and the stream was never read in one piece
        Assert.True(stream.Length > int.MaxValue);
        Assert.False(cache.Invalidated);
        Assert.Equal(2, cache.Loaded);
        Assert.Equal(Records - 2, cache.Discarded);
        Assert.True(cache.TryGet(SyntheticCacheStream.KeyOf(0), out var first));
        Assert.True(cache.TryGet(SyntheticCacheStream.KeyOf(Records - 1), out var last));
        Assert.Equal(1.0, first[0]);
        Assert.Equal(Records, last[0]);
        Assert.Equal(Dimension, first.Length);
        Assert.True(stream.LargestRead <= RecordSize);
        Assert.Equal(stream.Length, stream.Position);
    }

    /// <summary>
    ///     A stream that ends inside a record keeps the complete records and counts the rest as damaged.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_EmbeddingCache_OpenStream_CutOffStream_KeepsCompleteRecords()
    {
        // Arrange: a stream of three records, cut off after the first and 20 bytes of the second
        var digest = SHA256.HashData("digest"u8);
        using var whole = new SyntheticCacheStream(digest, 3, 4);
        var bytes = new byte[HeaderSize + (32 + (4 * 8) + 8) + 20];
        whole.ReadExactly(bytes);
        using var cut = new MemoryStream(bytes);

        // Act
        var cache = EmbeddingCache.OpenStream("unused", digest, cut);

        // Assert: the first record is valid; the cut-off second record and the missing third are lost
        Assert.Equal(1, cache.Loaded);
        Assert.Equal(2, cache.Discarded);
    }

    /// <summary>
    ///     A stream of another digest is refused as a whole.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_EmbeddingCache_OpenStream_OtherDigest_IsInvalidated()
    {
        // Arrange
        using var stream = new SyntheticCacheStream(SHA256.HashData("digest"u8), 3, 4);

        // Act
        var cache = EmbeddingCache.OpenStream("unused", SHA256.HashData("other"u8), stream);

        // Assert
        Assert.True(cache.Invalidated);
        Assert.Equal(0, cache.Count);
    }

    /// <summary>
    ///     A cache file as the writer saves it: the header and records of <c>count</c> vectors of <c>dimension</c> numbers,
    ///     of which the first and the last have a valid checksum, produced on demand.
    /// </summary>
    private sealed class SyntheticCacheStream : Stream
    {
        private readonly byte[] _header;
        private readonly int _count;
        private readonly int _recordSize;
        private readonly byte[] _first;
        private readonly byte[] _last;
        private long _position;

        public SyntheticCacheStream(byte[] digest, int count = Records, int dimension = Dimension)
        {
            _count = count;
            _recordSize = 32 + (dimension * sizeof(double)) + 8;
            var header = new byte[HeaderSize];
            "WSEC"u8.CopyTo(header);
            header[4] = 1;
            digest.CopyTo(header, 5);
            BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(37), dimension);
            BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(41), count);
            Prefix(header[..(HeaderSize - 8)]).CopyTo(header, HeaderSize - 8);
            _header = header;
            _first = ValidRecord(0);
            _last = ValidRecord(count - 1);
        }

        public int LargestRead { get; private set; }

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => HeaderSize + ((long)_count * _recordSize);

        public override long Position
        {
            get => _position;
            set => throw new NotSupportedException();
        }

        public static string KeyOf(int index) => Convert.ToHexString(Key(index));

        public override int Read(byte[] buffer, int offset, int count)
        {
            LargestRead = Math.Max(LargestRead, count);
            var written = 0;
            while (written < count && _position < Length)
            {
                if (_position < HeaderSize)
                {
                    var n = (int)Math.Min(count - written, HeaderSize - _position);
                    Array.Copy(_header, _position, buffer, offset + written, n);
                    written += n;
                    _position += n;
                    continue;
                }

                var record = (int)((_position - HeaderSize) / _recordSize);
                var inside = (int)((_position - HeaderSize) % _recordSize);
                var take = Math.Min(count - written, _recordSize - inside);
                var target = buffer.AsSpan(offset + written, take);
                if (record == 0 || record == _count - 1)
                {
                    (record == 0 ? _first : _last).AsSpan(inside, take).CopyTo(target);
                }
                else
                {
                    // Zeros with the key of the record; the checksum is zero, which does not match
                    target.Clear();
                    var key = Key(record);
                    for (var i = Math.Max(inside, 0); i < Math.Min(inside + take, 32); i++)
                    {
                        target[i - inside] = key[i];
                    }
                }

                written += take;
                _position += take;
            }

            return written;
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        private static byte[] Prefix(byte[] bytes) => SHA256.HashData(bytes)[..8];

        private static byte[] Key(int index)
        {
            var key = new byte[32];
            BinaryPrimitives.WriteInt32LittleEndian(key, index);
            return key;
        }

        private byte[] ValidRecord(int index)
        {
            var record = new byte[_recordSize];
            Key(index).CopyTo(record, 0);
            BinaryPrimitives.WriteDoubleLittleEndian(record.AsSpan(32), index == 0 ? 1.0 : _count);
            Prefix(record[..(_recordSize - 8)]).CopyTo(record, _recordSize - 8);
            return record;
        }
    }
}

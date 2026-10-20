using System.Collections.Immutable;
using System.Text;

namespace DemaConsulting.WordSense;

/// <summary>
///     The sense catalog part of the word image: the senses, the groups and the lemma blocks (see
///     <see cref="ImageFormat"/>), decoded on first use.
/// </summary>
internal sealed partial class WordImage
{
    private readonly object _catalogGate = new();

    /// <summary>
    ///     Gets the number of lemma blocks (a lemma with a part of speech and at least one sense).
    /// </summary>
    public int BlockCount => Count(_meta[ImageFormat.MetaBlockCount]);

    /// <summary>
    ///     Lists the senses of a lemma and part of speech in inventory order.
    /// </summary>
    /// <param name="lemma">The lemma exactly as it is written in the catalog.</param>
    /// <param name="pos">The part of speech.</param>
    /// <returns>The senses, the most frequent first; empty when the lemma and part of speech have none.</returns>
    /// <exception cref="InvalidDataException">Thrown when the catalog is corrupt.</exception>
    public ImmutableArray<SenseInfo> SensesOf(string lemma, PartOfSpeech pos)
    {
        ArgumentNullException.ThrowIfNull(lemma);
        var catalog = GetCatalog();
        var block = catalog.FindBlock(lemma, pos);
        if (block < 0)
        {
            return [];
        }

        var builder = ImmutableArray.CreateBuilder<SenseInfo>((int)(catalog.BlockStart[block + 1] - catalog.BlockStart[block]));
        for (var k = catalog.BlockStart[block]; k < catalog.BlockStart[block + 1]; k++)
        {
            builder.Add(catalog.Sense((int)catalog.BlockSenses[k]));
        }

        return builder.MoveToImmutable();
    }

    /// <summary>
    ///     Counts the senses of a lemma and part of speech.
    /// </summary>
    /// <param name="lemma">The lemma exactly as it is written in the catalog.</param>
    /// <param name="pos">The part of speech.</param>
    /// <returns>The number of senses; 0 when there are none.</returns>
    /// <exception cref="InvalidDataException">Thrown when the catalog is corrupt.</exception>
    public int SenseCountOf(string lemma, PartOfSpeech pos)
    {
        ArgumentNullException.ThrowIfNull(lemma);
        var catalog = GetCatalog();
        var block = catalog.FindBlock(lemma, pos);
        return block < 0 ? 0 : (int)(catalog.BlockStart[block + 1] - catalog.BlockStart[block]);
    }

    /// <summary>
    ///     Finds a sense by its id.
    /// </summary>
    /// <param name="senseId">The sense id.</param>
    /// <param name="sense">Receives the sense.</param>
    /// <returns>True when the sense exists.</returns>
    /// <exception cref="InvalidDataException">Thrown when the catalog is corrupt.</exception>
    public bool TryGetSense(string senseId, out SenseInfo sense)
    {
        ArgumentNullException.ThrowIfNull(senseId);
        var catalog = GetCatalog();
        var index = catalog.FindSense(senseId);
        sense = index < 0 ? null! : catalog.Sense(index);
        return index >= 0;
    }

    /// <summary>
    ///     Finds a sense by its Open English WordNet sense key.
    /// </summary>
    /// <param name="senseKey">The full sense key.</param>
    /// <param name="sense">Receives the sense.</param>
    /// <returns>True when a sense has the key; a project sense has none.</returns>
    /// <exception cref="InvalidDataException">Thrown when the catalog is corrupt.</exception>
    public bool TryGetSenseByKey(string senseKey, out SenseInfo sense)
    {
        ArgumentNullException.ThrowIfNull(senseKey);
        var catalog = GetCatalog();
        var index = catalog.FindKey(senseKey);
        sense = index < 0 ? null! : catalog.Sense(index);
        return index >= 0;
    }

    /// <summary>
    ///     Finds a group by its id.
    /// </summary>
    /// <param name="groupId">The group id.</param>
    /// <param name="group">Receives the group.</param>
    /// <returns>True when the group exists.</returns>
    /// <exception cref="InvalidDataException">Thrown when the catalog is corrupt.</exception>
    public bool TryGetGroup(string groupId, out SenseGroup group)
    {
        ArgumentNullException.ThrowIfNull(groupId);
        var catalog = GetCatalog();
        var index = catalog.FindGroup(groupId);
        group = index < 0 ? null! : catalog.Group(index);
        return index >= 0;
    }

    /// <summary>
    ///     Lists every sense of the catalog in the order of their ids (for tests and tools).
    /// </summary>
    /// <returns>The senses.</returns>
    /// <exception cref="InvalidDataException">Thrown when the catalog is corrupt.</exception>
    public IEnumerable<SenseInfo> AllSenses()
    {
        var catalog = GetCatalog();
        for (var s = 0; s < SenseCount; s++)
        {
            yield return catalog.Sense(s);
        }
    }

    /// <summary>
    ///     Lists every group of the catalog in the order of their ids (for tests and tools).
    /// </summary>
    /// <returns>The groups.</returns>
    /// <exception cref="InvalidDataException">Thrown when the catalog is corrupt.</exception>
    public IEnumerable<SenseGroup> AllGroups()
    {
        var catalog = GetCatalog();
        for (var g = 0; g < GroupCount; g++)
        {
            yield return catalog.Group(g);
        }
    }

    /// <summary>
    ///     Decodes the catalog when that has not happened yet and checks it fully, including the order of the key index
    ///     (for tests and tools; the reader itself checks the structure and the order of the ids only).
    /// </summary>
    /// <exception cref="InvalidDataException">Thrown when the catalog is corrupt.</exception>
    public void ValidateCatalog()
    {
        var catalog = GetCatalog();
        catalog.ValidateKeyOrder();
    }

    private Catalog GetCatalog()
    {
        var catalog = Volatile.Read(ref _catalog);
        if (catalog is not null)
        {
            return catalog;
        }

        lock (_catalogGate)
        {
            _catalog ??= new Catalog(this);
            return _catalog;
        }
    }

    private static ReadOnlySpan<byte> GroupMarker => "#group:"u8;

    private static int CompareBytes(ReadOnlySpan<byte> a, ReadOnlySpan<byte> b) => a.SequenceCompareTo(b);

    private static InvalidDataException Bad(string what) => new($"The word image has an invalid catalog: {what}.");

    private sealed class Catalog
    {
        private readonly byte[] _idBytes;
        private readonly uint[] _idOffset;
        private readonly byte[] _keyBytes;
        private readonly uint[] _keyOffset;
        private readonly uint[] _senseGroup;
        private readonly uint[] _senseRespelling;
        private readonly string[] _respellings;
        private readonly byte[] _groupIdBytes;
        private readonly uint[] _groupIdOffset;
        private readonly byte[] _groupPos;
        private readonly uint[] _groupStart;
        private readonly uint[] _groupMembers;
        private readonly byte[] _blockLemmaBytes;
        private readonly uint[] _blockLemmaOffset;
        private readonly byte[] _blockPos;
        private readonly uint[] _keyIndex;
        private readonly int[] _senseBlock;
        private readonly string?[] _blockLemmas;
        private readonly int _senseCount;

        public Catalog(WordImage image)
        {
            _senseCount = image.SenseCount;
            var groupCount = image.GroupCount;
            var blockCount = image.BlockCount;
            var respellingCount = Count(image._meta[ImageFormat.MetaRespellingCount]);

            _idBytes = image.Section(ImageFormat.SectionSenseIdBytes);
            _idOffset = Offsets(image.Section(ImageFormat.SectionSenseIdLength), _senseCount, _idBytes.Length, "sense id");
            _keyBytes = image.Section(ImageFormat.SectionSenseKeyBytes);
            _keyOffset = Offsets(image.Section(ImageFormat.SectionSenseKeyLength), _senseCount, _keyBytes.Length, "sense key");
            _senseGroup = image.Uints(ImageFormat.SectionSenseGroup);
            _senseRespelling = image.Uints(ImageFormat.SectionSenseRespelling);
            if (_senseGroup.Length != _senseCount || _senseRespelling.Length != _senseCount)
            {
                throw Bad("a sense table has the wrong length");
            }

            _respellings = image.Strings(ImageFormat.SectionRespellingLength, ImageFormat.SectionRespellingBytes, respellingCount);
            _groupIdBytes = image.Section(ImageFormat.SectionGroupIdBytes);
            _groupIdOffset = Offsets(image.Section(ImageFormat.SectionGroupIdLength), groupCount, _groupIdBytes.Length, "group id");
            _groupPos = image.Section(ImageFormat.SectionGroupPos);
            _groupStart = image.Uints(ImageFormat.SectionGroupMemberStart);
            _groupMembers = image.Uints(ImageFormat.SectionGroupMembers);
            _blockLemmaBytes = image.Section(ImageFormat.SectionBlockLemmaBytes);
            _blockLemmaOffset = Offsets(image.Section(ImageFormat.SectionBlockLemmaLength), blockCount, _blockLemmaBytes.Length, "block lemma");
            _blockPos = image.Section(ImageFormat.SectionBlockPos);
            BlockStart = image.Uints(ImageFormat.SectionBlockSenseStart);
            BlockSenses = image.Uints(ImageFormat.SectionBlockSenses);
            _keyIndex = image.Uints(ImageFormat.SectionKeyIndex);
            _senseBlock = new int[_senseCount];
            _blockLemmas = new string?[blockCount];
            Validate(groupCount, blockCount, respellingCount);
        }

        public uint[] BlockStart { get; }

        public uint[] BlockSenses { get; }

        public int FindBlock(string lemma, PartOfSpeech pos)
        {
            var count = Encoding.UTF8.GetByteCount(lemma);
            if (count is 0 or > MaxSpellingBytes)
            {
                return -1;
            }

            Span<byte> query = stackalloc byte[MaxSpellingBytes];
            query = query[..Encoding.UTF8.GetBytes(lemma, query)];
            int lo = 0, hi = _blockPos.Length;
            while (lo < hi)
            {
                var mid = (int)((uint)(lo + hi) >> 1);
                var c = CompareBytes(BlockLemmaSpan(mid), query);
                if (c == 0)
                {
                    c = ((int)_blockPos[mid]).CompareTo((int)pos);
                }

                if (c == 0)
                {
                    return mid;
                }

                if (c < 0)
                {
                    lo = mid + 1;
                }
                else
                {
                    hi = mid;
                }
            }

            return -1;
        }

        public int FindSense(string id)
        {
            var query = Encoding.UTF8.GetBytes(id);
            return query.Length is 0 or > MaxSpellingBytes ? -1 : Search(_senseCount, i => CompareBytes(IdSpan(i), query));
        }

        public int FindGroup(string id)
        {
            var query = Encoding.UTF8.GetBytes(id);
            return query.Length is 0 or > MaxSpellingBytes
                ? -1
                : Search(_groupPos.Length, i => CompareBytes(_groupIdBytes.AsSpan((int)_groupIdOffset[i], (int)(_groupIdOffset[i + 1] - _groupIdOffset[i])), query));
        }

        public int FindKey(string key)
        {
            var query = Encoding.UTF8.GetBytes(key);
            var found = Search(_keyIndex.Length, i => CompareKey((int)_keyIndex[i], query));
            return found < 0 ? -1 : (int)_keyIndex[found];
        }

        public SenseInfo Sense(int index)
        {
            var block = _senseBlock[index];
            var lemma = BlockLemma(block);
            var group = _senseGroup[index];
            var respelling = _senseRespelling[index];
            return new SenseInfo(
                Encoding.UTF8.GetString(IdSpan(index)),
                group == 0 ? null : GroupId((int)group - 1),
                lemma,
                (PartOfSpeech)_blockPos[block],
                KeyOf(index, lemma),
                respelling == 0 ? null : _respellings[(int)respelling - 1]);
        }

        public SenseGroup Group(int index)
        {
            var first = (int)_groupMembers[_groupStart[index]];
            var members = ImmutableArray.CreateBuilder<string>((int)(_groupStart[index + 1] - _groupStart[index]));
            uint shared = _senseRespelling[first];
            for (var k = _groupStart[index]; k < _groupStart[index + 1]; k++)
            {
                var member = (int)_groupMembers[k];
                members.Add(Encoding.UTF8.GetString(IdSpan(member)));
                if (_senseRespelling[member] != shared)
                {
                    shared = 0;
                }
            }

            return new SenseGroup(
                GroupId(index),
                BlockLemma(_senseBlock[first]),
                (PartOfSpeech)_groupPos[index],
                members.MoveToImmutable(),
                shared == 0 ? null : _respellings[(int)shared - 1]);
        }

        public void ValidateKeyOrder()
        {
            byte[]? previous = null;
            foreach (var index in _keyIndex)
            {
                var key = Encoding.UTF8.GetBytes(KeyOf((int)index, BlockLemma(_senseBlock[(int)index]))!);
                if (previous is not null && CompareBytes(previous, key) >= 0)
                {
                    throw Bad("the key index is not in key order");
                }

                previous = key;
            }
        }

        private static int Search(int count, Func<int, int> compareToQuery)
        {
            int lo = 0, hi = count;
            while (lo < hi)
            {
                var mid = (int)((uint)(lo + hi) >> 1);
                var c = compareToQuery(mid);
                if (c == 0)
                {
                    return mid;
                }

                if (c < 0)
                {
                    lo = mid + 1;
                }
                else
                {
                    hi = mid;
                }
            }

            return -1;
        }

        private ReadOnlySpan<byte> IdSpan(int i) => _idBytes.AsSpan((int)_idOffset[i], (int)(_idOffset[i + 1] - _idOffset[i]));

        private ReadOnlySpan<byte> BlockLemmaSpan(int i) =>
            _blockLemmaBytes.AsSpan((int)_blockLemmaOffset[i], (int)(_blockLemmaOffset[i + 1] - _blockLemmaOffset[i]));

        private string BlockLemma(int block) => _blockLemmas[block] ??= Encoding.UTF8.GetString(BlockLemmaSpan(block));

        private string GroupId(int group) =>
            Encoding.UTF8.GetString(_groupIdBytes, (int)_groupIdOffset[group], (int)(_groupIdOffset[group + 1] - _groupIdOffset[group]));

        private string? KeyOf(int sense, string lemma)
        {
            var length = (int)(_keyOffset[sense + 1] - _keyOffset[sense]);
            return length == 0
                ? null
                : lemma.ToLowerInvariant() + "%" + Encoding.UTF8.GetString(_keyBytes, (int)_keyOffset[sense], length);
        }

        private int CompareKey(int sense, byte[] query)
        {
            var key = Encoding.UTF8.GetBytes(KeyOf(sense, BlockLemma(_senseBlock[sense]))!);
            return CompareBytes(key, query);
        }

        private void Validate(int groupCount, int blockCount, int respellingCount)
        {
            for (var i = 1; i < _senseCount; i++)
            {
                var c = CompareBytes(IdSpan(i - 1), IdSpan(i));
                if (c == 0)
                {
                    throw Bad("a sense id occurs twice");
                }

                if (c > 0)
                {
                    throw Bad("the sense ids are not in order");
                }
            }

            if (_blockPos.Length != blockCount || BlockStart.Length != blockCount + 1 || BlockStart[^1] != BlockSenses.Length || BlockSenses.Length != _senseCount)
            {
                throw Bad("the lemma blocks do not cover the senses");
            }

            Array.Fill(_senseBlock, -1);
            for (var b = 0; b < blockCount; b++)
            {
                ValidateBlock(b);
            }

            for (var s = 0; s < _senseCount; s++)
            {
                if (_senseGroup[s] > groupCount)
                {
                    throw Bad("a sense names a group that does not exist");
                }

                if (_senseRespelling[s] > respellingCount)
                {
                    throw Bad("a sense names a respelling that does not exist");
                }
            }

            ValidateGroups(groupCount);
            ValidateKeys();
        }

        private void ValidateBlock(int b)
        {
            if (_blockPos[b] > (byte)PartOfSpeech.Auxiliary || BlockStart[b + 1] <= BlockStart[b])
            {
                throw Bad("a lemma block is invalid");
            }

            if (b > 0)
            {
                var c = CompareBytes(BlockLemmaSpan(b - 1), BlockLemmaSpan(b));
                if (c > 0 || (c == 0 && _blockPos[b - 1] >= _blockPos[b]))
                {
                    throw Bad("the lemma blocks are not in order");
                }
            }

            var lemma = BlockLemmaSpan(b);
            for (var k = BlockStart[b]; k < BlockStart[b + 1]; k++)
            {
                var sense = BlockSenses[k];
                if (sense >= _senseCount || _senseBlock[sense] != -1)
                {
                    throw Bad("a sense is in no block or in two blocks");
                }

                _senseBlock[sense] = b;
                var id = IdSpan((int)sense);
                if (id.Length <= lemma.Length || !id.StartsWith(lemma) || id[lemma.Length] != (byte)'#')
                {
                    throw Bad("a sense id does not start with the lemma of its block");
                }

                if (id.IndexOf(GroupMarker) >= 0)
                {
                    throw Bad("a sense id has the group marker");
                }
            }
        }

        private void ValidateGroups(int groupCount)
        {
            if (_groupPos.Length != groupCount || _groupStart.Length != groupCount + 1 || _groupStart[^1] != _groupMembers.Length)
            {
                throw Bad("the group table has the wrong length");
            }

            var grouped = 0;
            for (var g = 0; g < groupCount; g++)
            {
                if (g > 0 && CompareBytes(_groupIdBytes.AsSpan((int)_groupIdOffset[g - 1], (int)(_groupIdOffset[g] - _groupIdOffset[g - 1])), _groupIdBytes.AsSpan((int)_groupIdOffset[g], (int)(_groupIdOffset[g + 1] - _groupIdOffset[g]))) >= 0)
                {
                    throw Bad("the group ids are not in order");
                }

                var (start, end) = (_groupStart[g], _groupStart[g + 1]);
                var groupId = _groupIdBytes.AsSpan((int)_groupIdOffset[g], (int)(_groupIdOffset[g + 1] - _groupIdOffset[g]));
                if (end < start + 2 || _groupPos[g] > (byte)PartOfSpeech.Auxiliary)
                {
                    throw Bad("a group has fewer than two senses");
                }

                var block = -1;
                for (var k = start; k < end; k++)
                {
                    var member = _groupMembers[k];
                    if (member >= _senseCount || _senseGroup[member] != g + 1 || (block != -1 && _senseBlock[member] != block))
                    {
                        throw Bad("the members of a group do not agree with the senses");
                    }

                    block = _senseBlock[member];
                }

                var lemma = BlockLemmaSpan(block);
                if (!groupId.StartsWith(lemma) || !groupId[lemma.Length..].StartsWith(GroupMarker))
                {
                    throw Bad("a group id is not the lemma of its senses followed by the group marker");
                }

                if (_blockPos[block] != _groupPos[g] || end - start >= BlockStart[block + 1] - BlockStart[block])
                {
                    throw Bad("a group mixes parts of speech or holds all the senses of its lemma and part of speech");
                }

                grouped += (int)(end - start);
            }

            if (grouped != _senseGroup.Count(g => g != 0))
            {
                throw Bad("a sense names a group that does not list it");
            }
        }

        private void ValidateKeys()
        {
            var keyed = 0;
            for (var s = 0; s < _senseCount; s++)
            {
                keyed += _keyOffset[s + 1] > _keyOffset[s] ? 1 : 0;
            }

            if (_keyIndex.Length != keyed || _keyIndex.Any(i => i >= _senseCount || _keyOffset[i + 1] == _keyOffset[i]))
            {
                throw Bad("the key index does not list the keyed senses");
            }
        }
    }
}

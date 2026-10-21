using System.Security.Cryptography;
using System.Text;

namespace WordSense.Builder;

/// <summary>
///     The side of a held-out row in the real-text split.
/// </summary>
internal enum HoldoutSide
{
    /// <summary>The row is not real text with a known document (written text, or no provenance row).</summary>
    None,

    /// <summary>A real-text row whose document is on the calibration side: it may choose thresholds.</summary>
    Calibration,

    /// <summary>A real-text row whose document is on the test side: it never influences a threshold.</summary>
    Test,
}

/// <summary>
///     The origin of one sentence, read from a row of <c>corpus/provenance</c>.
/// </summary>
/// <param name="Source">The source column (<c>gutenberg</c>, <c>tatoeba-cc0</c>, <c>voa</c>, <c>llm-corpus</c>, ...; see
/// <see cref="ProvenanceSources" />).</param>
/// <param name="SourceKey">The source key column (<c>pg135#s6340</c>, a Tatoeba sentence id, ...).</param>
internal sealed record ProvenanceEntry(string Source, string SourceKey)
{
    /// <summary>
    ///     Gets a value indicating whether the sentence is real text (not written for the project).
    /// </summary>
    public bool IsReal => ProvenanceSources.IsReal(Source);

    /// <summary>
    ///     Gets the identifier of the document of the sentence: the source and the part of the source key before the
    ///     <c>#</c> (a book, an article, a page); a Tatoeba sentence is its own document.
    /// </summary>
    public string Document
    {
        get
        {
            if (Source.StartsWith("tatoeba", StringComparison.Ordinal))
            {
                return Source + ":" + SourceKey;
            }

            var hash = SourceKey.IndexOf('#', StringComparison.Ordinal);
            return Source + ":" + (hash < 0 ? SourceKey : SourceKey[..hash]);
        }
    }
}

/// <summary>
///     The held-out rows of a corpus sorted into the real-text splits.
/// </summary>
/// <param name="Calibration">The real rows of the calibration documents.</param>
/// <param name="Test">The real rows of the test documents.</param>
/// <param name="TestWithoutAnchorDocuments">The test rows whose document supplies no anchor.</param>
/// <param name="Written">The rows written by a model.</param>
/// <param name="Other">The rows without a provenance row.</param>
internal sealed record HoldoutSplit(
    IReadOnlyList<CorpusRow> Calibration,
    IReadOnlyList<CorpusRow> Test,
    IReadOnlyList<CorpusRow> TestWithoutAnchorDocuments,
    IReadOnlyList<CorpusRow> Written,
    IReadOnlyList<CorpusRow> Other);

/// <summary>
///     Splits the real-text held-out sentences by document into a calibration and a test split.
/// </summary>
/// <remarks>
///     <para>
///     The rule is fixed by the data: the side of a document is the first bit of the SHA-256 of
///     <c>document|&lt;document identifier&gt;</c> (0 calibration, 1 test), so every sentence of a document is on one
///     side and no seed is involved. Written sentences and rows without a provenance row are in neither split. The
///     provenance row of a corpus row is found by its id, the first 12 hexadecimal digits of the SHA-256 of
///     <c>lemma|sentence</c> (the sentence with its braces); its columns are <c>id</c>, <c>split</c>, <c>source</c> and
///     <c>source_key</c>, and whether the sentence is real text follows from its source (<see cref="ProvenanceSources" />).
///     </para>
/// </remarks>
internal static class RealSplit
{
    /// <summary>
    ///     Reads every provenance file of a corpus into a map from the id of a row to its origin.
    /// </summary>
    /// <param name="corpus">The corpus directory; a missing <c>provenance</c> folder gives an empty map.</param>
    /// <returns>The entries by id.</returns>
    public static Dictionary<string, ProvenanceEntry> ReadProvenance(string corpus)
    {
        var result = new Dictionary<string, ProvenanceEntry>(StringComparer.Ordinal);
        var directory = Path.Join(corpus, "provenance");
        if (!Directory.Exists(directory))
        {
            return result;
        }

        foreach (var file in Directory.GetFiles(directory, "*.tsv").OrderBy(f => f, StringComparer.Ordinal))
        {
            var lines = File.ReadAllLines(file, new UTF8Encoding(false));
            if (lines.Length == 0)
            {
                continue;
            }

            var header = lines[0].Split('\t');
            var id = Array.IndexOf(header, "id");
            var source = Array.IndexOf(header, "source");
            var key = Array.IndexOf(header, "source_key");
            if (id < 0 || source < 0 || key < 0)
            {
                continue;
            }

            foreach (var line in lines.Skip(1))
            {
                var columns = line.Split('\t');
                if (columns.Length > Math.Max(Math.Max(id, source), key))
                {
                    result[columns[id]] = new ProvenanceEntry(columns[source], columns[key]);
                }
            }
        }

        return result;
    }

    /// <summary>
    ///     Computes the provenance id of a corpus row.
    /// </summary>
    /// <param name="row">The row.</param>
    /// <returns>The first 12 hexadecimal digits (lower case) of the SHA-256 of <c>lemma|sentence</c>.</returns>
    public static string IdOf(CorpusRow row)
    {
        ArgumentNullException.ThrowIfNull(row);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(row.Lemma + "|" + row.Sentence));
        return Convert.ToHexString(hash)[..12].ToLowerInvariant();
    }

    /// <summary>
    ///     Gives the side of a document.
    /// </summary>
    /// <param name="document">The document identifier.</param>
    /// <returns>Calibration for a document whose hash starts with a clear bit, test otherwise.</returns>
    public static HoldoutSide SideOf(string document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes("document|" + document));
        return (hash[0] & 1) == 0 ? HoldoutSide.Calibration : HoldoutSide.Test;
    }

    /// <summary>
    ///     Sorts the held-out rows into the real-text splits.
    /// </summary>
    /// <param name="corpus">The corpus directory with the provenance.</param>
    /// <param name="holdout">The held-out rows.</param>
    /// <param name="anchors">The anchor rows, to find the documents that supply anchors, or null for none.</param>
    /// <returns>The split; every list is empty when the corpus has no provenance.</returns>
    public static HoldoutSplit Classify(string corpus, IReadOnlyList<CorpusRow> holdout, IReadOnlyList<CorpusRow>? anchors = null)
    {
        ArgumentNullException.ThrowIfNull(holdout);
        var provenance = ReadProvenance(corpus);
        var anchorDocuments = new HashSet<string>(StringComparer.Ordinal);
        foreach (var anchor in anchors ?? [])
        {
            if (provenance.TryGetValue(IdOf(anchor), out var entry) && entry.IsReal)
            {
                anchorDocuments.Add(entry.Document);
            }
        }

        List<CorpusRow> calibration = [], test = [], testFree = [], written = [], other = [];
        foreach (var row in holdout)
        {
            if (!provenance.TryGetValue(IdOf(row), out var entry))
            {
                other.Add(row);
            }
            else if (!entry.IsReal)
            {
                written.Add(row);
            }
            else if (SideOf(entry.Document) == HoldoutSide.Calibration)
            {
                calibration.Add(row);
            }
            else
            {
                test.Add(row);
                if (!anchorDocuments.Contains(entry.Document))
                {
                    testFree.Add(row);
                }
            }
        }

        return new HoldoutSplit(calibration, test, testFree, written, other);
    }
}

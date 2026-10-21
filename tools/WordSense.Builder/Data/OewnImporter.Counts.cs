using System.Globalization;
using System.IO.Compression;
using System.Text;

namespace WordSense.Builder.Data;

/// <summary>
///     The part of the OEWN importer that reads the WordNet 3.0 sense frequency counts.
/// </summary>
internal static partial class OewnImporter
{
    /// <summary>
    ///     The SHA-256 of the pinned WordNet 3.0 sense index <c>dict/index.sense</c> (the file of the Princeton
    ///     distribution <c>WordNet-3.0.tar.gz</c>, whose own SHA-256 is recorded in <c>data/PROVENANCE.md</c>), as
    ///     upper-case hexadecimal.
    /// </summary>
    public const string PinnedCountsSha256 = "68B3A468CDDFD8E92134B9B0624339A02A1B837159243C297C5F138A3D618392";

    /// <summary>
    ///     The source name written to the <c>source</c> column of the <c>sense_frequency</c> table.
    /// </summary>
    public const string CountsSource = "wn30";

    /// <summary>
    ///     The outcome of the count import.
    /// </summary>
    /// <param name="Counted">The senses that got a count above 0.</param>
    /// <param name="Zero">The senses with the count 0: listed with 0 or not listed at all.</param>
    /// <param name="Unmatched">The lines of the counts file whose sense key is not a sense of the data (counted, not written).</param>
    internal sealed record CountsResult(int Counted, int Zero, int Unmatched)
    {
        /// <summary>
        ///     Gets the number of rows written.
        /// </summary>
        public int Rows => Counted + Zero;
    }

    /// <summary>
    ///     Writes the <c>sense_frequency</c> table from the WordNet 3.0 sense index.
    /// </summary>
    /// <param name="input">The <c>index.sense</c> file (plain text, or gzip-compressed when the name ends in <c>.gz</c>).</param>
    /// <param name="dataDirectory">The data directory with the <c>sense</c> table; its <c>sense_frequency</c> is replaced.</param>
    /// <param name="expectedSha256">The SHA-256 the file must have, or null to skip the check.</param>
    /// <returns>The counts.</returns>
    /// <exception cref="InvalidDataException">Thrown when the hash does not match, a line has not four fields, a count is not a non-negative integer or a key appears twice; nothing is written.</exception>
    public static CountsResult ImportCounts(string input, string dataDirectory, string? expectedSha256 = PinnedCountsSha256)
    {
        CheckHash(input, expectedSha256);
        var counts = ReadCounts(input);
        var schema = DataSchema.Load(Path.Join(dataDirectory, "schema.yaml"));
        var entries = DataTables.Read(dataDirectory, schema.Table("entry")).ToDictionary(r => r[0], r => r[1], StringComparer.Ordinal);
        var senses = DataTables.Read(dataDirectory, schema.Table("sense")).ToList();
        var resolver = new PartitionResolver(schema.FileSplit);
        foreach (var (id, lemma) in entries)
        {
            resolver.AddEntry(id, lemma);
        }

        var rows = new List<string[]>();
        var counted = 0;
        var matched = 0;
        foreach (var sense in senses)
        {
            resolver.AddSense(sense[0], sense[2], sense[3], sense[1]);
            if (sense[1].Length == 0)
            {
                continue;
            }

            var count = counts.TryGetValue(sense[1], out var value) ? value : 0;
            matched += counts.ContainsKey(sense[1]) ? 1 : 0;
            counted += count > 0 ? 1 : 0;
            rows.Add([sense[1], count.ToString(CultureInfo.InvariantCulture), CountsSource]);
        }

        DataTables.Write(dataDirectory, schema.Table("sense_frequency"), rows, resolver);
        return new CountsResult(counted, rows.Count - counted, counts.Count - matched);
    }

    /// <summary>
    ///     Formats the counts of a count import.
    /// </summary>
    /// <param name="result">The counts.</param>
    /// <returns>A summary line.</returns>
    public static string Describe(CountsResult result) =>
        string.Create(CultureInfo.InvariantCulture, $"sense_frequency: {result.Rows} rows, {result.Counted} with a count, {result.Zero} with the count 0, {result.Unmatched} lines of the counts file match no sense key.");

    private static Dictionary<string, int> ReadCounts(string input)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        using var file = File.OpenRead(input);
        using var stream = input.EndsWith(".gz", StringComparison.OrdinalIgnoreCase) ? new GZipStream(file, CompressionMode.Decompress) : (Stream)file;
        using var reader = new StreamReader(stream, new UTF8Encoding(false, true));
        var number = 0;
        while (reader.ReadLine() is { } line)
        {
            number++;
            var fields = line.Split(' ');
            if (fields.Length != 4 || fields[0].Length == 0 || !int.TryParse(fields[3], NumberStyles.None, CultureInfo.InvariantCulture, out var count))
            {
                throw new InvalidDataException($"{input}:{number.ToString(CultureInfo.InvariantCulture)}: expected 'sense_key synset_offset sense_number tag_count' with a non-negative integer count but found '{line}'.");
            }

            if (!counts.TryAdd(fields[0], count))
            {
                throw new InvalidDataException($"{input}:{number.ToString(CultureInfo.InvariantCulture)}: the sense key '{fields[0]}' appears twice.");
            }
        }

        return counts;
    }
}

using System.Text;
using DemaConsulting.WordSense;

namespace WordSense.Builder.Data;

/// <summary>
///     Reads and writes the table files of the data folder for the commands that process the committed data
///     (the bootstrap and the image compiler); the validator has its own reader that reports every problem.
/// </summary>
internal static class DataTables
{
    private static readonly UTF8Encoding Utf8 = new(false, true);

    /// <summary>
    ///     Reads every row of a table, in file order (the files in the order of their names).
    /// </summary>
    /// <param name="root">The data directory.</param>
    /// <param name="table">The table.</param>
    /// <returns>The rows without the header.</returns>
    public static IEnumerable<string[]> Read(string root, TableDef table)
    {
        string[] files;
        if (table.Partition == PartitionKind.None)
        {
            files = [DataLayout.FilePath(root, table, string.Empty)];
        }
        else
        {
            var directory = Path.Combine(root, table.Name);
            files = Directory.Exists(directory)
                ? Directory.GetFiles(directory, "*" + DataLayout.Extension).OrderBy(f => f, StringComparer.Ordinal).ToArray()
                : [];
        }

        foreach (var file in files.Where(File.Exists))
        {
            var first = true;
            foreach (var line in File.ReadLines(file, Utf8))
            {
                if (first)
                {
                    first = false;
                    continue;
                }

                yield return line.Split('\t');
            }
        }
    }

    /// <summary>
    ///     Writes a table, replacing its files, sorted by the primary key and placed by the partition rule.
    /// </summary>
    /// <param name="root">The data directory.</param>
    /// <param name="table">The table.</param>
    /// <param name="rows">The rows.</param>
    /// <param name="resolver">The resolver that places rows of a partitioned table.</param>
    /// <returns>The number of files written.</returns>
    public static int Write(string root, TableDef table, List<string[]> rows, PartitionResolver resolver)
    {
        var header = table.Columns.Select(c => c.Name).ToArray();
        var comparer = DataLayout.RowComparer(table);
        if (table.Partition == PartitionKind.None)
        {
            rows.Sort(comparer);
            DataLayout.WriteTable(DataLayout.FilePath(root, table, string.Empty), header, rows);
            return 1;
        }

        var directory = Path.Combine(root, table.Name);
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, true);
        }

        var groups = new SortedDictionary<string, List<string[]>>(StringComparer.Ordinal);
        foreach (var row in rows)
        {
            var key = resolver.KeyFor(table, row) ?? throw new InvalidDataException($"A row of table '{table.Name}' refers to a missing parent: {string.Join('\t', row)}");
            if (!groups.TryGetValue(key, out var list))
            {
                groups[key] = list = [];
            }

            list.Add(row);
        }

        foreach (var (key, list) in groups)
        {
            list.Sort(comparer);
            DataLayout.WriteTable(DataLayout.FilePath(root, table, key), header, list);
        }

        return groups.Count;
    }

    /// <summary>
    ///     Gets the letter of a part of speech in the <c>pos</c> vocabulary of the data schema.
    /// </summary>
    /// <param name="pos">The part of speech.</param>
    /// <returns>The letter.</returns>
    public static string PosLetter(PartOfSpeech pos) => pos switch
    {
        PartOfSpeech.Noun => "n",
        PartOfSpeech.Verb => "v",
        PartOfSpeech.Adjective => "a",
        PartOfSpeech.Adverb => "r",
        PartOfSpeech.Pronoun => "u",
        PartOfSpeech.Preposition => "p",
        PartOfSpeech.Conjunction => "c",
        PartOfSpeech.Interjection => "i",
        PartOfSpeech.Determiner => "d",
        PartOfSpeech.Auxiliary => "x",
        _ => throw new ArgumentOutOfRangeException(nameof(pos)),
    };

    /// <summary>
    ///     Gets the part of speech of a letter of the <c>pos</c> vocabulary.
    /// </summary>
    /// <param name="letter">The letter.</param>
    /// <returns>The part of speech.</returns>
    public static PartOfSpeech PosOf(string letter) => letter switch
    {
        "n" => PartOfSpeech.Noun,
        "v" => PartOfSpeech.Verb,
        "a" => PartOfSpeech.Adjective,
        "r" => PartOfSpeech.Adverb,
        "u" => PartOfSpeech.Pronoun,
        "p" => PartOfSpeech.Preposition,
        "c" => PartOfSpeech.Conjunction,
        "i" => PartOfSpeech.Interjection,
        "d" => PartOfSpeech.Determiner,
        "x" => PartOfSpeech.Auxiliary,
        _ => throw new InvalidDataException($"Unknown part of speech letter '{letter}'."),
    };
}

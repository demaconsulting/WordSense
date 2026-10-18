using System.Globalization;
using System.Text;

namespace WordSense.Builder.Data;

/// <summary>
///     The physical layout of the committed data tables: which file holds a row, how rows and bytes are ordered, and
///     the canonical TSV writer.
/// </summary>
/// <remarks>
///     <para>
///     Every rule here exists so that two machines produce byte-identical files and so that a change to one lemma
///     touches one small file: files are split by the first letter of the lemma (<c>FileFor</c>), letters with many
///     entries by the first two letters (a data-driven plan in <c>schema.yaml</c>, never inferred when validating),
///     rows are sorted by the UTF-8 bytes of their primary key, and cells are NFC without tab, newline or edge spaces.
///     </para>
///     <para>
///     Non-ASCII lemma starts, digits and punctuation all map to the key <c>_</c> so that file names stay portable.
///     Case folding is ASCII-only on purpose: it cannot differ between platforms or Unicode versions.
///     The type is stateless and thread-safe.
///     </para>
/// </remarks>
internal static class DataLayout
{
    /// <summary>
    ///     The file name extension of a table file.
    /// </summary>
    public const string Extension = ".tsv";

    /// <summary>
    ///     The key of lemmas that do not start with an ASCII letter, and the filler of a two-letter key.
    /// </summary>
    public const char OtherKey = '_';

    /// <summary>
    ///     Gets the first-letter key of a lemma: the lower-cased ASCII letter it starts with (after NFC), or <c>_</c>.
    /// </summary>
    /// <param name="lemma">The lemma or other key text.</param>
    /// <returns>A one-character key, <c>a</c> to <c>z</c> or <c>_</c>.</returns>
    public static string KeyOf(string lemma) => FirstKey(lemma, out _).ToString();

    /// <summary>
    ///     Gets the file key of a lemma: its first-letter key, or its two-letter key when the first letter is in the
    ///     split plan.
    /// </summary>
    /// <remarks>
    ///     The second character of a two-letter key is the lower-cased second ASCII letter, or <c>_</c> when the lemma
    ///     has no second character or it is not an ASCII letter, so a split letter's files are <c>sa</c> to <c>sz</c>
    ///     and <c>s_</c>.
    /// </remarks>
    /// <param name="lemma">The lemma.</param>
    /// <param name="splitLetters">The first letters whose files are split by two letters.</param>
    /// <returns>The key: one character, or two characters for a split letter.</returns>
    public static string FileFor(string lemma, IReadOnlyCollection<char> splitLetters)
    {
        var first = FirstKey(lemma, out var rest);
        if (first == OtherKey || !splitLetters.Contains(first))
        {
            return first.ToString();
        }

        var second = rest.Length > 0 ? AsciiLetter(rest[0]) : OtherKey;
        return string.Create(2, (first, second), static (span, state) =>
        {
            span[0] = state.first;
            span[1] = state.second;
        });
    }

    /// <summary>
    ///     Tests whether a file key is valid for a split plan: <c>a</c> to <c>z</c> or <c>_</c>, or two characters whose
    ///     first is a split letter and whose second is <c>a</c> to <c>z</c> or <c>_</c>.
    /// </summary>
    /// <param name="key">The file key (the file name without extension).</param>
    /// <param name="splitLetters">The split plan.</param>
    /// <returns>True when the key can be produced by <see cref="FileFor"/>.</returns>
    public static bool IsValidKey(string key, IReadOnlyCollection<char> splitLetters)
    {
        if (key.Length == 1)
        {
            return key[0] == OtherKey || (key[0] is >= 'a' and <= 'z' && !splitLetters.Contains(key[0]));
        }

        return key.Length == 2 && splitLetters.Contains(key[0]) && (key[1] == OtherKey || key[1] is >= 'a' and <= 'z');
    }

    /// <summary>
    ///     Compares two strings by their UTF-8 bytes (which is code point order).
    /// </summary>
    /// <remarks>
    ///     <c>string.CompareOrdinal</c> compares UTF-16 code units and therefore sorts astral characters (surrogates)
    ///     before U+E000 to U+FFFF; the committed files and the future runtime index sort by UTF-8 bytes, so this is
    ///     the only comparison used for ordering rows.
    /// </remarks>
    /// <param name="left">The first string.</param>
    /// <param name="right">The second string.</param>
    /// <returns>A negative, zero or positive number.</returns>
    public static int CompareUtf8(string left, string right)
    {
        var length = Math.Min(left.Length, right.Length);
        for (var i = 0; i < length; i++)
        {
            if (left[i] != right[i])
            {
                return CodePointOrder(left[i]) - CodePointOrder(right[i]);
            }
        }

        return left.Length - right.Length;
    }

    /// <summary>
    ///     Creates the comparer that orders the rows of a table by its primary key.
    /// </summary>
    /// <param name="table">The table.</param>
    /// <returns>A comparer: key columns in declaration order, <c>int</c> columns numerically, others by UTF-8 bytes.</returns>
    public static IComparer<string[]> RowComparer(TableDef table)
    {
        var columns = table.PrimaryKey.Select(name => table.IndexOf(name)).ToArray();
        var numeric = columns.Select(i => table.Columns[i].Kind == ColumnKind.Int).ToArray();
        return Comparer<string[]>.Create((a, b) => CompareKeys(a, b, columns, numeric));
    }

    /// <summary>
    ///     Compares two rows on their key columns.
    /// </summary>
    /// <param name="left">The first row.</param>
    /// <param name="right">The second row.</param>
    /// <param name="columns">The key column indexes.</param>
    /// <param name="numeric">Whether each key column is numeric.</param>
    /// <returns>A negative, zero or positive number.</returns>
    public static int CompareKeys(string[] left, string[] right, int[] columns, bool[] numeric)
    {
        for (var k = 0; k < columns.Length; k++)
        {
            var a = left[columns[k]];
            var b = right[columns[k]];
            var result = numeric[k] && long.TryParse(a, NumberStyles.None, CultureInfo.InvariantCulture, out var x) && long.TryParse(b, NumberStyles.None, CultureInfo.InvariantCulture, out var y)
                ? x.CompareTo(y)
                : CompareUtf8(a, b);
            if (result != 0)
            {
                return result;
            }
        }

        return 0;
    }

    /// <summary>
    ///     Gets the path of the file of a table that holds a key.
    /// </summary>
    /// <param name="root">The data directory.</param>
    /// <param name="table">The table.</param>
    /// <param name="key">The file key; ignored for a table that is not partitioned.</param>
    /// <returns>The full path.</returns>
    public static string FilePath(string root, TableDef table, string key) =>
        table.Partition == PartitionKind.None
            ? Path.Combine(root, table.Name + Extension)
            : Path.Combine(root, table.Name, key + Extension);

    /// <summary>
    ///     Describes why a cell value is not allowed in a table file.
    /// </summary>
    /// <param name="value">The cell value.</param>
    /// <returns>The problem, or null when the value is valid.</returns>
    public static string? CheckCell(string value)
    {
        if (value.AsSpan().IndexOfAny('\t', '\n', '\r') >= 0)
        {
            return "contains a tab or line break";
        }

        if (value.Length > 0 && (value[0] == ' ' || value[^1] == ' '))
        {
            return "has a leading or trailing space";
        }

        return value.IsNormalized(NormalizationForm.FormC) ? null : "is not in Unicode normalization form C";
    }

    /// <summary>
    ///     Writes a table file in the canonical format: a header line and one line per row, cells separated by tabs, no
    ///     quoting, UTF-8 without byte-order mark, LF line ends.
    /// </summary>
    /// <remarks>
    ///     The rows are written in the order given; callers sort them with <see cref="RowComparer"/> first. The
    ///     directory is created when missing.
    /// </remarks>
    /// <param name="path">The file to write.</param>
    /// <param name="header">The column names.</param>
    /// <param name="rows">The rows, each with one cell per column.</param>
    /// <exception cref="InvalidDataException">Thrown when a row has the wrong width or a cell is not allowed.</exception>
    public static void WriteTable(string path, IReadOnlyList<string> header, IEnumerable<string[]> rows)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        using var writer = new StreamWriter(path, false, new UTF8Encoding(false)) { NewLine = "\n" };
        writer.WriteLine(string.Join('\t', header));
        foreach (var row in rows)
        {
            if (row.Length != header.Count)
            {
                throw new InvalidDataException($"A row for {path} has {row.Length.ToString(CultureInfo.InvariantCulture)} cells; expected {header.Count.ToString(CultureInfo.InvariantCulture)}.");
            }

            foreach (var cell in row)
            {
                var problem = CheckCell(cell);
                if (problem is not null)
                {
                    throw new InvalidDataException($"A cell of {path} {problem}: '{cell}'.");
                }
            }

            writer.WriteLine(string.Join('\t', row));
        }
    }

    /// <summary>
    ///     Folds an ASCII upper-case letter to lower case and leaves every other character unchanged.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <returns>The folded text.</returns>
    public static string AsciiLower(string text)
    {
        return string.Create(text.Length, text, static (span, source) =>
        {
            for (var i = 0; i < span.Length; i++)
            {
                var c = source[i];
                span[i] = c is >= 'A' and <= 'Z' ? (char)(c + ('a' - 'A')) : c;
            }
        });
    }

    private static char FirstKey(string lemma, out string rest)
    {
        // NFC first, so that a decomposed accented letter is one non-ASCII scalar and not an ASCII letter plus a mark
        var text = lemma.Normalize(NormalizationForm.FormC);
        if (text.Length == 0)
        {
            rest = string.Empty;
            return OtherKey;
        }

        var width = char.IsHighSurrogate(text[0]) && text.Length > 1 ? 2 : 1;
        rest = text[width..];
        return width == 1 ? AsciiLetter(text[0]) : OtherKey;
    }

    private static char AsciiLetter(char c) => c switch
    {
        >= 'a' and <= 'z' => c,
        >= 'A' and <= 'Z' => (char)(c + ('a' - 'A')),
        _ => OtherKey,
    };

    /// <summary>
    ///     Maps a UTF-16 code unit to a value whose order equals the order of the UTF-8 bytes of its code point, by
    ///     moving the surrogate range above U+E000 to U+FFFF.
    /// </summary>
    private static int CodePointOrder(char c)
    {
        if (c >= '\uE000')
        {
            return c - 0x800;
        }

        return c >= '\uD800' ? c + 0x2000 : c;
    }
}

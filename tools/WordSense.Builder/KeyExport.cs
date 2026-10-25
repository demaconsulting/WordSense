using System.Globalization;
using System.Text;
using DemaConsulting.WordSense;

namespace WordSense.Builder;

/// <summary>
///     Writes every word key of the lexicon as a sorted UTF-8 text file, the input of a downstream library that builds a
///     map from key to its own data.
/// </summary>
/// <remarks>
///     The keys are those of <see cref="WordLexicon.Keys"/>, so the file lists every key that any model can return. The
///     first line is a comment (it starts with <c>#</c>; no key starts with an unescaped <c>#</c>) that names the format,
///     the inventory version of the lexicon and the number of
///     keys. The other lines are the keys, one for each line, in ordinal order, with LF line endings and no byte-order
///     mark.
/// </remarks>
internal static class KeyExport
{
    /// <summary>
    ///     Renders the file.
    /// </summary>
    /// <param name="keys">The keys, in any order.</param>
    /// <param name="version">The inventory version of the lexicon (<see cref="WordLexicon.Version"/>).</param>
    /// <returns>The text: the header comment, then the sorted keys, each followed by a line feed.</returns>
    public static string Render(IEnumerable<string> keys, string version)
    {
        var sorted = keys.ToList();
        sorted.Sort(StringComparer.Ordinal);
        var text = new StringBuilder();
        text.Append("# WordSense keys; format: {spelling}|{lemma}/{Part}[/{Features}][#{label}]; inventory: ")
            .Append(version).Append("; count: ").Append(sorted.Count.ToString(CultureInfo.InvariantCulture)).Append('\n');
        foreach (var key in sorted)
        {
            text.Append(key).Append('\n');
        }

        return text.ToString();
    }

    /// <summary>
    ///     Writes the keys of the embedded lexicon to a file.
    /// </summary>
    /// <param name="output">The file to write.</param>
    /// <returns>The number of keys written.</returns>
    public static int Write(string output)
    {
        var keys = WordLexicon.Keys().ToList();
        var text = Render(keys, WordLexicon.Version);
        var directory = Path.GetDirectoryName(Path.GetFullPath(output));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(output, text, new UTF8Encoding(false));
        return keys.Count;
    }
}

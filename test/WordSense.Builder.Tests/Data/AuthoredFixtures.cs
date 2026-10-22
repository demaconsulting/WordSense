using System.Text;

namespace WordSense.Builder.Tests;

/// <summary>
///     Builds the provenance sidecar that goes with a small file of authored forms in a test: the reader of the authored forms
///     refuses an authored row without its decision.
/// </summary>
internal static class AuthoredFixtures
{
    /// <summary>
    ///     The header of the provenance sidecar, with its line feed.
    /// </summary>
    public const string ProvenanceHeader = "lemma\tpos\trule\tbatch_id\twriter\tverifier\tverdict\tresult\tlicence\tevidence\n";

    /// <summary>
    ///     Writes the sidecar of authored forms: the header and, for every authored row, an agreed decision whose result is the forms.
    /// </summary>
    /// <param name="authoredLines">The lines of the authored forms (comment lines are skipped).</param>
    /// <returns>The content of the sidecar.</returns>
    public static string Sidecar(IEnumerable<string> authoredLines)
    {
        var text = new StringBuilder(ProvenanceHeader);
        foreach (var line in authoredLines.Select(l => l.TrimEnd('\r')).Where(l => l.Length > 0 && l[0] != '#'))
        {
            var columns = line.Split('\t');
            text.Append(columns[0]).Append('\t').Append(columns[1]).Append('\t').Append(columns[2])
                .Append("\ta1+v1\tw\tv\tagree\tforms\tCC0 1.0 (project-authored)\t-\n");
        }

        return text.ToString();
    }

    /// <summary>
    ///     Writes the sidecar of authored forms given as one text.
    /// </summary>
    /// <param name="authored">The authored forms, one row on each line.</param>
    /// <returns>The content of the sidecar.</returns>
    public static string Sidecar(string authored) => Sidecar(authored.Split('\n'));
}

using System.Text;

namespace WordSense.Builder;

/// <summary>
///     The model comparison table of the README, kept in <c>docs/user_guide/model-table.md</c>: one row per model and
///     one column per measurement, each column written by the command that measures it.
/// </summary>
/// <remarks>
///     <para>
///     <c>evaluate</c> writes the real-text sense column and the cost columns (download, weights, RAM, latency, first
///     use), <c>evaluate-running</c> the two part-of-speech columns and the challenge-set column, <c>evaluate-senses</c>
///     the independent sense column. A command changes only its own cells, so the file records the last run of every
///     measurement; a cell that was never measured reads <c>not measured</c>.
///     </para>
///     <para>
///     The latency, memory and first-use columns are machine dependent: they come from the last <c>evaluate</c> run on
///     the development machine described in the user guide chapter on accuracy.
///     </para>
/// </remarks>
internal static class ModelTable
{
    /// <summary>The column of the model name.</summary>
    public const string ModelColumn = "Model";

    /// <summary>The column of the sense share and precision on the real-text test split.</summary>
    public const string RealSenseColumn = "Real-text sense (share @ precision)";

    /// <summary>The column of the part-of-speech accuracy on the representative test split of the open corpus.</summary>
    public const string RunningColumn = "Running-text part of speech (open corpus test)";

    /// <summary>The column of the part-of-speech accuracy on the convention-mapped test file of the treebank.</summary>
    public const string TreebankColumn = "UD-EWT test part of speech (convention-mapped)";

    /// <summary>The column of the independent sense evaluation.</summary>
    public const string IndependentSenseColumn = "Independent sense evaluation (SemCor)";

    /// <summary>The column of the challenge set.</summary>
    public const string ChallengeColumn = "STE challenge set (part of speech)";

    /// <summary>The column of the download size.</summary>
    public const string DownloadColumn = "Download";

    /// <summary>The column of the weights or table size.</summary>
    public const string WeightsColumn = "Weights / table";

    /// <summary>The column of the memory.</summary>
    public const string RamColumn = "RAM";

    /// <summary>The column of the latency.</summary>
    public const string LatencyColumn = "Latency per sentence";

    /// <summary>The column of the first use.</summary>
    public const string FirstUseColumn = "First use (model + word table)";

    /// <summary>The column of the download requirement.</summary>
    public const string NeededColumn = "Download needed";

    /// <summary>The text of a cell that no command has measured yet.</summary>
    public const string NotMeasured = "not measured";

    /// <summary>The marker that starts the table in the file and in the README.</summary>
    public const string Begin = "<!-- model-table:begin -->";

    /// <summary>The marker that ends the table in the file and in the README.</summary>
    public const string End = "<!-- model-table:end -->";

    /// <summary>
    ///     The columns of the table in order.
    /// </summary>
    public static readonly IReadOnlyList<string> Columns =
    [
        ModelColumn, RealSenseColumn, RunningColumn, TreebankColumn, IndependentSenseColumn, ChallengeColumn,
        DownloadColumn, WeightsColumn, RamColumn, LatencyColumn, FirstUseColumn, NeededColumn,
    ];

    private static readonly string[] ModelOrder = ["Basic", "MiniLM", "BERT base cased"];

    /// <summary>
    ///     Gets the name of a model in the table.
    /// </summary>
    /// <param name="model">The command-line name of the model.</param>
    /// <returns>The display name.</returns>
    public static string DisplayName(string model) => model switch
    {
        "basic" => "Basic",
        "minilm" => "MiniLM",
        "bert-base-cased" => "BERT base cased",
        _ => model,
    };

    /// <summary>
    ///     Reads the rows of a table file by model name and column name.
    /// </summary>
    /// <param name="path">The table file; a missing file has no rows.</param>
    /// <returns>The cells of every model.</returns>
    public static Dictionary<string, Dictionary<string, string>> Read(string path)
    {
        var rows = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
        if (!File.Exists(path))
        {
            return rows;
        }

        string[]? header = null;
        foreach (var line in File.ReadAllLines(path))
        {
            if (!line.StartsWith("| ", StringComparison.Ordinal) || line.StartsWith("| ---", StringComparison.Ordinal))
            {
                continue;
            }

            var cells = line.Trim().Trim('|').Split('|').Select(c => c.Trim()).ToArray();
            if (header is null)
            {
                header = cells;
                continue;
            }

            var row = new Dictionary<string, string>(StringComparer.Ordinal);
            for (var i = 0; i < Math.Min(header.Length, cells.Length); i++)
            {
                row[header[i]] = cells[i];
            }

            rows[cells[0]] = row;
        }

        return rows;
    }

    /// <summary>
    ///     Replaces some cells of the row of a model, keeping its other cells and the rows of the other models.
    /// </summary>
    /// <param name="path">The table file; it is created when it does not exist.</param>
    /// <param name="model">The command-line name of the model.</param>
    /// <param name="cells">The new cells by column name; every name must be one of <see cref="Columns"/>.</param>
    /// <exception cref="ArgumentException">Thrown when a column name is not one of the columns.</exception>
    public static void Update(string path, string model, IReadOnlyDictionary<string, string> cells)
    {
        ArgumentNullException.ThrowIfNull(cells);
        var unknown = cells.Keys.FirstOrDefault(k => !Columns.Contains(k));
        if (unknown is not null)
        {
            throw new ArgumentException($"'{unknown}' is not a column of the model table.", nameof(cells));
        }

        var rows = Read(path);
        var name = DisplayName(model);
        if (!rows.TryGetValue(name, out var row))
        {
            row = [];
            rows[name] = row;
        }

        foreach (var (column, value) in cells)
        {
            row[column] = value;
        }

        File.WriteAllText(path, Render(rows), new UTF8Encoding(false));
    }

    /// <summary>
    ///     Renders the table file.
    /// </summary>
    /// <param name="rows">The cells of every model.</param>
    /// <returns>The file text with line feeds.</returns>
    public static string Render(IReadOnlyDictionary<string, Dictionary<string, string>> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        var text = new StringBuilder();
        text.Append("# Model Comparison Table\n\n");
        text.Append("This file is generated by `WordSense.Builder evaluate <model>`, `evaluate-running` and `evaluate-senses` with\n");
        text.Append("`--table docs/user_guide/model-table.md`; every command writes the columns it measures and keeps the others.\n");
        text.Append("Do not edit it by hand: the table of the root README must equal the table below (a doc-test checks it).\n\n");
        text.Append("The size, memory, latency and first-use columns are machine dependent: they come from the last `evaluate` run and\n");
        text.Append("are to be remeasured on a quiet machine when a model changes. The chapter \"Accuracy and Model Choice\" explains the\n");
        text.Append("measurement conditions and how to read the columns.\n\n");
        text.Append("<!-- markdownlint-disable MD013 -->\n\n");
        text.Append(Begin).Append('\n');
        text.Append("| ").Append(string.Join(" | ", Columns)).Append(" |\n");
        text.Append("| --- |").Append(string.Concat(Columns.Skip(1).Select(c => c == NeededColumn ? " --- |" : " ---: |"))).Append('\n');
        foreach (var name in ModelOrder.Where(rows.ContainsKey).Concat(rows.Keys.Where(k => Array.IndexOf(ModelOrder, k) < 0)))
        {
            var row = rows[name];
            text.Append("| ").Append(name);
            foreach (var column in Columns.Skip(1))
            {
                text.Append(" | ").Append(row.GetValueOrDefault(column, NotMeasured));
            }

            text.Append(" |\n");
        }

        text.Append(End).Append("\n\n<!-- markdownlint-enable MD013 -->\n");
        return text.ToString();
    }
}

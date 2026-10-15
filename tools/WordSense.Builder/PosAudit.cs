using System.Security.Cryptography;
using System.Text;

namespace WordSense.Builder;

/// <summary>
///     One answer of a judge for one sentence.
/// </summary>
/// <param name="Id">The row id.</param>
/// <param name="Pos">The judged part of speech.</param>
/// <param name="Form">The judged word form.</param>
/// <param name="Confidence">The confidence: H, M or L.</param>
/// <param name="Note">The free-text note.</param>
internal sealed record JudgeAnswer(string Id, string Pos, string Form, string Confidence, string Note);

/// <summary>
///     One row of the corpus with its audit identity.
/// </summary>
/// <param name="Id">The stable row id.</param>
/// <param name="Lemma">The lemma.</param>
/// <param name="Sense">The current sense label.</param>
/// <param name="Split">Either <c>anchors</c> or <c>holdout</c>.</param>
/// <param name="Sentence">The sentence with the braced target.</param>
internal sealed record AuditRow(string Id, string Lemma, string Sense, string Split, string Sentence);

/// <summary>
///     One merged row of <c>pos-audit.tsv</c>.
/// </summary>
/// <param name="Row">The corpus row.</param>
/// <param name="A">Judge A.</param>
/// <param name="B">Judge B.</param>
/// <param name="C">The adjudicator, when consulted.</param>
/// <param name="FinalPos">The final part of speech, or <c>undecided</c>.</param>
/// <param name="FinalForm">The final form.</param>
/// <param name="FinalConfidence">The final confidence.</param>
/// <param name="Resolution">One of <c>agree</c>, <c>adjudicated</c> and <c>undecided</c>.</param>
internal sealed record AuditResult(
    AuditRow Row,
    JudgeAnswer A,
    JudgeAnswer B,
    JudgeAnswer? C,
    string FinalPos,
    string FinalForm,
    string FinalConfidence,
    string Resolution);

/// <summary>
///     The blind part-of-speech audit of the corpus: chunk export, strict judge-file parsing and resolution.
/// </summary>
/// <remarks>
///     Two independent judges see only <c>id</c>, <c>lemma</c> and sentence of every row; an adjudicator settles
///     disagreements, low confidence and non-word usages. The merged result is <c>corpus/audit/pos-audit.tsv</c>.
/// </remarks>
internal static class PosAudit
{
    /// <summary>
    ///     The accepted values of the part-of-speech column.
    /// </summary>
    public static readonly string[] PosValues = ["noun", "verb", "adj", "adv", "other"];

    /// <summary>
    ///     The accepted values of the form column.
    /// </summary>
    public static readonly string[] FormValues =
    [
        "base", "third", "past", "pastpart", "presentpart", "infinitive", "comparative", "superlative",
        "positive", "singular", "plural", "none"
    ];

    /// <summary>
    ///     The accepted values of the confidence column.
    /// </summary>
    public static readonly string[] ConfidenceValues = ["H", "M", "L"];

    /// <summary>
    ///     The marker of a row that the audit could not settle.
    /// </summary>
    public const string Undecided = "undecided";

    private const string ResultHeader =
        "id\tlemma\tsense\tsplit\ta_pos\ta_form\ta_conf\tb_pos\tb_form\tb_conf\tc_pos\tc_form\tc_conf\tfinal_pos\tfinal_form\tfinal_conf\tresolution";

    /// <summary>
    ///     Computes the stable row id: the first 12 hexadecimal characters of SHA-256 over <c>lemma|sentence</c>.
    /// </summary>
    /// <param name="lemma">The lemma.</param>
    /// <param name="sentence">The sentence.</param>
    /// <returns>The id.</returns>
    public static string RowId(string lemma, string sentence)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(lemma + "|" + sentence));
        return Convert.ToHexString(hash, 0, 6).ToLowerInvariant();
    }

    /// <summary>
    ///     Reads every anchor and holdout row with its id, ordered by lemma and sentence.
    /// </summary>
    /// <param name="corpus">The corpus directory.</param>
    /// <returns>The rows.</returns>
    /// <exception cref="InvalidDataException">Thrown when two rows share an id.</exception>
    public static List<AuditRow> ReadRows(string corpus)
    {
        var rows = new List<AuditRow>();
        foreach (var split in new[] { "anchors", "holdout" })
        {
            rows.AddRange(Corpus.ReadDirectory(Path.Combine(corpus, split))
                .Select(r => new AuditRow(RowId(r.Lemma, r.Sentence), r.Lemma, r.Sense, split, r.Sentence)));
        }

        rows.Sort((a, b) => a.Lemma == b.Lemma ? string.CompareOrdinal(a.Sentence, b.Sentence) : string.CompareOrdinal(a.Lemma, b.Lemma));
        var collision = rows.GroupBy(r => r.Id, StringComparer.Ordinal).FirstOrDefault(g => g.Count() > 1);
        return collision is null ? rows : throw new InvalidDataException($"Row id {collision.Key} collides: '{collision.First().Sentence}' and '{collision.Last().Sentence}'.");
    }

    /// <summary>
    ///     Splits rows into chunks that keep the rows of a lemma together unless a lemma exceeds the chunk size.
    /// </summary>
    /// <param name="rows">The rows, ordered by lemma.</param>
    /// <param name="size">The target chunk size.</param>
    /// <returns>The chunks.</returns>
    public static List<List<AuditRow>> Chunk(IReadOnlyList<AuditRow> rows, int size)
    {
        var chunks = new List<List<AuditRow>>();
        var current = new List<AuditRow>();
        foreach (var lemmaRows in rows.GroupBy(r => r.Lemma))
        {
            var group = lemmaRows.ToList();
            if (current.Count > 0 && current.Count + group.Count > size)
            {
                chunks.Add(current);
                current = [];
            }

            while (group.Count > size)
            {
                chunks.Add(group.GetRange(0, size));
                group.RemoveRange(0, size);
            }

            current.AddRange(group);
        }

        if (current.Count > 0)
        {
            chunks.Add(current);
        }

        return chunks;
    }

    /// <summary>
    ///     Writes the blind judge inputs: <c>chunk-NNN-a.tsv</c> (corpus order) and <c>chunk-NNN-b.tsv</c> (reversed),
    ///     plus <c>index.tsv</c> that maps ids to the current labels (never shown to the judges).
    /// </summary>
    /// <param name="corpus">The corpus directory.</param>
    /// <param name="work">The work directory.</param>
    /// <param name="size">The chunk size.</param>
    /// <returns>The number of rows and chunks.</returns>
    public static (int Rows, int Chunks) Export(string corpus, string work, int size)
    {
        var rows = ReadRows(corpus);
        var chunks = Chunk(rows, size);
        Directory.CreateDirectory(Path.Combine(work, "judged"));
        var index = new StringBuilder();
        for (var i = 0; i < chunks.Count; i++)
        {
            var name = $"chunk-{i + 1:D3}";
            WriteBlind(Path.Combine(work, name + "-a.tsv"), chunks[i]);
            WriteBlind(Path.Combine(work, name + "-b.tsv"), Enumerable.Reverse(chunks[i]));
            foreach (var row in chunks[i])
            {
                index.Append(name).Append('\t').Append(row.Id).Append('\t').Append(row.Lemma).Append('\t')
                    .Append(row.Sense).Append('\t').Append(row.Split).Append('\n');
            }
        }

        File.WriteAllText(Path.Combine(work, "index.tsv"), index.ToString(), new UTF8Encoding(false));
        return (rows.Count, chunks.Count);
    }

    /// <summary>
    ///     Parses a judge file strictly.
    /// </summary>
    /// <param name="path">The file.</param>
    /// <param name="expectedIds">The ids the file must contain exactly once each.</param>
    /// <returns>The answers by id.</returns>
    /// <exception cref="InvalidDataException">Thrown with every problem found, one per line.</exception>
    public static Dictionary<string, JudgeAnswer> ParseJudgeFile(string path, IReadOnlyCollection<string> expectedIds)
    {
        var answers = new Dictionary<string, JudgeAnswer>(StringComparer.Ordinal);
        var errors = new List<string>();
        var name = Path.GetFileName(path);
        var number = 0;
        foreach (var raw in File.ReadAllLines(path, new UTF8Encoding(false)))
        {
            number++;
            var line = raw.TrimEnd('\r');
            if (line.Length == 0)
            {
                continue;
            }

            var columns = line.Split('\t');
            if (columns.Length is < 4 or > 5)
            {
                errors.Add($"{name}:{number}: expected 4 or 5 tab-separated columns but found {columns.Length}.");
                continue;
            }

            var answer = new JudgeAnswer(columns[0].Trim(), columns[1].Trim(), columns[2].Trim(), columns[3].Trim(), columns.Length == 5 ? columns[4].Trim() : string.Empty);
            var problem = ValidateAnswer(answer, expectedIds);
            if (problem is null && !answers.TryAdd(answer.Id, answer))
            {
                problem = $"duplicate id '{answer.Id}'";
            }

            if (problem is not null)
            {
                errors.Add($"{name}:{number}: {problem}.");
            }
        }

        errors.AddRange(expectedIds.Where(id => !answers.ContainsKey(id)).Select(id => $"{name}: missing id '{id}'."));
        return errors.Count > 0 ? throw new InvalidDataException(string.Join(Environment.NewLine, errors)) : answers;
    }

    /// <summary>
    ///     Determines whether two judge answers can be accepted without adjudication.
    /// </summary>
    /// <param name="a">Judge A.</param>
    /// <param name="b">Judge B.</param>
    /// <returns>True when both agree on a word part of speech with confidence H or M.</returns>
    public static bool AutoAccept(JudgeAnswer a, JudgeAnswer b)
    {
        return a.Pos == b.Pos && a.Pos != "other" && a.Confidence != "L" && b.Confidence != "L";
    }

    /// <summary>
    ///     Resolves a row after adjudication.
    /// </summary>
    /// <param name="a">Judge A.</param>
    /// <param name="b">Judge B.</param>
    /// <param name="c">The adjudicator, or null when the rows were auto-accepted.</param>
    /// <returns>The final pos, form, confidence and the resolution.</returns>
    public static (string Pos, string Form, string Confidence, string Resolution) Resolve(JudgeAnswer a, JudgeAnswer b, JudgeAnswer? c)
    {
        if (c is null)
        {
            return AutoAccept(a, b)
                ? (a.Pos, a.Form, Weakest(a.Confidence, b.Confidence), "agree")
                : throw new InvalidDataException($"Row {a.Id} needs adjudication.");
        }

        var matches = c.Pos == a.Pos || c.Pos == b.Pos;
        return c.Confidence != "L" && c.Pos != "other" && matches
            ? (c.Pos, c.Form, c.Confidence, "adjudicated")
            : (Undecided, "none", "L", "undecided");
    }

    /// <summary>
    ///     Writes the adjudication inputs for rows that need them: <c>adjudicate-NNN.tsv</c> with
    ///     <c>id, lemma, sentence, current label, A answer, B answer</c>; and the blind probe inputs.
    /// </summary>
    /// <param name="corpus">The corpus directory.</param>
    /// <param name="work">The work directory.</param>
    /// <param name="size">The chunk size.</param>
    /// <param name="probeFraction">The fraction of auto-accepted rows to re-judge blind.</param>
    /// <returns>The number of rows to adjudicate and to probe.</returns>
    public static (int Adjudicate, int Probe) PrepareAdjudication(string corpus, string work, int size, double probeFraction)
    {
        var rows = ReadRows(corpus).ToDictionary(r => r.Id, StringComparer.Ordinal);
        var (a, b) = LoadJudged(work, rows.Keys);
        var need = new List<string>();
        var accepted = new List<string>();
        foreach (var id in rows.Keys.OrderBy(k => k, StringComparer.Ordinal))
        {
            (AutoAccept(a[id], b[id]) ? accepted : need).Add(id);
        }

        var stride = Math.Max(1, (int)Math.Round(1 / probeFraction));
        var probe = accepted.Where((_, k) => k % stride == 0).ToList();
        var text = new StringBuilder();
        var count = 0;
        var chunk = 0;
        foreach (var id in need.OrderBy(i => rows[i].Lemma, StringComparer.Ordinal).ThenBy(i => rows[i].Sentence, StringComparer.Ordinal))
        {
            var row = rows[id];
            text.Append(id).Append('\t').Append(row.Lemma).Append('\t').Append(row.Sentence).Append('\t').Append(Describe(a[id])).Append('\t').Append(Describe(b[id])).Append('\n');
            if (++count % size == 0)
            {
                Flush(Path.Combine(work, $"adjudicate-{++chunk:D3}.tsv"), text);
            }
        }

        if (text.Length > 0)
        {
            Flush(Path.Combine(work, $"adjudicate-{chunk + 1:D3}.tsv"), text);
        }

        var probeRows = probe.Select(i => rows[i]).OrderBy(r => r.Lemma, StringComparer.Ordinal).ThenBy(r => r.Sentence, StringComparer.Ordinal).ToList();
        var chunks = Chunk(probeRows, size);
        for (var i = 0; i < chunks.Count; i++)
        {
            WriteBlind(Path.Combine(work, $"probe-{i + 1:D3}.tsv"), chunks[i]);
        }

        return (need.Count, probe.Count);
    }

    /// <summary>
    ///     Merges the judge files into the audit result list.
    /// </summary>
    /// <param name="corpus">The corpus directory.</param>
    /// <param name="work">The work directory with <c>judged/</c>.</param>
    /// <returns>The results ordered by lemma and sentence.</returns>
    /// <exception cref="InvalidDataException">Thrown when judge files are missing or invalid.</exception>
    public static List<AuditResult> Merge(string corpus, string work)
    {
        var rows = ReadRows(corpus);
        var ids = rows.Select(r => r.Id).ToList();
        var (a, b) = LoadJudged(work, ids);
        var c = new Dictionary<string, JudgeAnswer>(StringComparer.Ordinal);
        foreach (var file in Directory.GetFiles(Path.Combine(work, "judged"), "adjudicate-*.tsv").OrderBy(f => f, StringComparer.Ordinal))
        {
            foreach (var answer in ParseAnyJudgeFile(file))
            {
                c[answer.Id] = answer;
            }
        }

        var results = new List<AuditResult>();
        var missing = new List<string>();
        foreach (var row in rows)
        {
            c.TryGetValue(row.Id, out var adjudication);
            if (adjudication is null && !AutoAccept(a[row.Id], b[row.Id]))
            {
                missing.Add(row.Id);
                continue;
            }

            var (pos, form, confidence, resolution) = Resolve(a[row.Id], b[row.Id], AutoAccept(a[row.Id], b[row.Id]) ? null : adjudication);
            results.Add(new AuditResult(row, a[row.Id], b[row.Id], adjudication, pos, form, confidence, resolution));
        }

        return missing.Count > 0
            ? throw new InvalidDataException($"{missing.Count} rows still need adjudication, for example {missing[0]}.")
            : results;
    }

    /// <summary>
    ///     Writes the merged results as <c>pos-audit.tsv</c>.
    /// </summary>
    /// <param name="path">The file.</param>
    /// <param name="results">The results.</param>
    public static void WriteResults(string path, IEnumerable<AuditResult> results)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var text = new StringBuilder(ResultHeader).Append('\n');
        foreach (var r in results)
        {
            text.Append(string.Join('\t', r.Row.Id, r.Row.Lemma, r.Row.Sense, r.Row.Split, r.A.Pos, r.A.Form, r.A.Confidence,
                r.B.Pos, r.B.Form, r.B.Confidence, r.C?.Pos ?? "-", r.C?.Form ?? "-", r.C?.Confidence ?? "-",
                r.FinalPos, r.FinalForm, r.FinalConfidence, r.Resolution)).Append('\n');
        }

        File.WriteAllText(path, text.ToString(), new UTF8Encoding(false));
    }

    /// <summary>
    ///     A merged row read back from <c>pos-audit.tsv</c>.
    /// </summary>
    /// <param name="Id">The row id.</param>
    /// <param name="Lemma">The lemma.</param>
    /// <param name="Sense">The sense label at audit time.</param>
    /// <param name="Split">The split.</param>
    /// <param name="FinalPos">The final part of speech or <c>undecided</c>.</param>
    /// <param name="FinalForm">The final form.</param>
    /// <param name="Resolution">The resolution.</param>
    public sealed record AuditLine(string Id, string Lemma, string Sense, string Split, string FinalPos, string FinalForm, string Resolution);

    /// <summary>
    ///     Reads <c>pos-audit.tsv</c>.
    /// </summary>
    /// <param name="path">The file.</param>
    /// <returns>The lines.</returns>
    public static List<AuditLine> ReadResults(string path)
    {
        return File.ReadAllLines(path, new UTF8Encoding(false)).Skip(1).Where(l => l.Length > 0)
            .Select(l => l.Split('\t'))
            .Select(c => new AuditLine(c[0], c[1], c[2], c[3], c[13], c[14], c[16])).ToList();
    }

    private static string? ValidateAnswer(JudgeAnswer answer, IReadOnlyCollection<string> expectedIds)
    {
        if (!expectedIds.Contains(answer.Id))
        {
            return $"unknown id '{answer.Id}'";
        }

        if (!PosValues.Contains(answer.Pos))
        {
            return $"invalid pos '{answer.Pos}'";
        }

        if (!FormValues.Contains(answer.Form))
        {
            return $"invalid form '{answer.Form}'";
        }

        if (!ConfidenceValues.Contains(answer.Confidence))
        {
            return $"invalid confidence '{answer.Confidence}'";
        }

        return (answer.Pos == "other" || answer.Confidence == "L") && answer.Note.Length == 0
            ? "a note is required for pos other or confidence L"
            : null;
    }

    private static string Weakest(string x, string y) => x == "M" || y == "M" ? "M" : "H";

    private static string Describe(JudgeAnswer answer)
    {
        return $"{answer.Pos}/{answer.Form}/{answer.Confidence}" + (answer.Note.Length > 0 ? ": " + answer.Note : string.Empty);
    }

    private static void Flush(string path, StringBuilder text)
    {
        File.WriteAllText(path, text.ToString(), new UTF8Encoding(false));
        text.Clear();
    }

    private static void WriteBlind(string path, IEnumerable<AuditRow> rows)
    {
        var text = new StringBuilder();
        foreach (var row in rows)
        {
            text.Append(row.Id).Append('\t').Append(row.Lemma).Append('\t').Append(row.Sentence).Append('\n');
        }

        File.WriteAllText(path, text.ToString(), new UTF8Encoding(false));
    }

    private static List<JudgeAnswer> ParseAnyJudgeFile(string path)
    {
        var ids = File.ReadAllLines(path, new UTF8Encoding(false)).Where(l => l.Length > 0).Select(l => l.Split('\t')[0].Trim()).ToList();
        return [.. ParseJudgeFile(path, ids).Values];
    }

    private static (Dictionary<string, JudgeAnswer> A, Dictionary<string, JudgeAnswer> B) LoadJudged(string work, IReadOnlyCollection<string> allIds)
    {
        var chunkIds = File.ReadAllLines(Path.Combine(work, "index.tsv"), new UTF8Encoding(false)).Where(l => l.Length > 0)
            .Select(l => l.Split('\t')).GroupBy(c => c[0], c => c[1]).OrderBy(g => g.Key, StringComparer.Ordinal).ToList();
        var a = new Dictionary<string, JudgeAnswer>(StringComparer.Ordinal);
        var b = new Dictionary<string, JudgeAnswer>(StringComparer.Ordinal);
        foreach (var chunk in chunkIds)
        {
            var ids = chunk.ToList();
            foreach (var (side, target) in new[] { ("a", a), ("b", b) })
            {
                foreach (var (id, answer) in ParseJudgeFile(Path.Combine(work, "judged", $"{chunk.Key}-{side}.tsv"), ids))
                {
                    target[id] = answer;
                }
            }
        }

        return allIds.All(a.ContainsKey) && allIds.All(b.ContainsKey) ? (a, b) : throw new InvalidDataException("The judge files do not cover every row.");
    }
}

using System.Text;

namespace WordSense.Builder;

/// <summary>
///     One candidate sentence for the sense judges.
/// </summary>
/// <param name="Id">The row id (first 12 hex characters of SHA-256 over <c>lemma|sentence</c>).</param>
/// <param name="Lemma">The lemma.</param>
/// <param name="Sentence">The braced sentence.</param>
/// <param name="Source">The source: <c>tatoeba-cc0</c>, <c>tatoeba-by</c>, <c>ud-ewt</c>, <c>wikipedia</c>, <c>gutenberg</c>, <c>web-excerpt</c>, <c>authored</c> or <c>llm-corpus</c>.</param>
/// <param name="Licence">The licence.</param>
/// <param name="SourceKey">The source key (sentence id, book id and paragraph, or page).</param>
/// <param name="Url">The URL of the source.</param>
/// <param name="Contributor">The contributor or the author and work.</param>
/// <param name="Split">The split of a reinstated project-authored row; empty for new rows.</param>
internal sealed record Candidate(string Id, string Lemma, string Sentence, string Source, string Licence, string SourceKey, string Url, string Contributor, string Split = "");

/// <summary>
///     One answer of a sense judge.
/// </summary>
/// <param name="Id">The row id.</param>
/// <param name="Sense">A sense label of the lemma's menu, or <c>none</c> when the word is not a usage of any listed sense.</param>
/// <param name="Confidence">H, M or L.</param>
/// <param name="Note">The note, required for L and none.</param>
internal sealed record SenseAnswer(string Id, string Sense, string Confidence, string Note);

/// <summary>
///     One merged sense judgement.
/// </summary>
/// <param name="Id">The row id.</param>
/// <param name="Lemma">The lemma.</param>
/// <param name="A">Judge A's sense.</param>
/// <param name="AConf">Judge A's confidence.</param>
/// <param name="B">Judge B's sense.</param>
/// <param name="BConf">Judge B's confidence.</param>
/// <param name="C">The adjudicator's sense, or <c>-</c>.</param>
/// <param name="CConf">The adjudicator's confidence, or <c>-</c>.</param>
/// <param name="Final">The final sense, <c>none</c> or <c>undecided</c>.</param>
/// <param name="Resolution">One of <c>agree</c>, <c>adjudicated</c>, <c>undecided</c>.</param>
internal sealed record SenseJudgement(string Id, string Lemma, string A, string AConf, string B, string BConf, string C, string CConf, string Final, string Resolution);

/// <summary>
///     The blind sense judging of candidate sentences: chunk export, strict answer parsing, adjudication and merge.
/// </summary>
/// <remarks>
///     The judges see only the id, the lemma, the braced sentence and the lemma's sense menu (labels, parts of speech, short
///     definition and up to three example sentences). They never see the source, the split or the intended sense.
/// </remarks>
internal static class RealJudge
{
    /// <summary>
    ///     The sense that means "not a usage of any listed sense".
    /// </summary>
    public const string None = "none";

    /// <summary>
    ///     The accepted confidence values.
    /// </summary>
    public static readonly string[] Confidences = ["H", "M", "L"];

    private const string JudgementHeader = "id\tlemma\ta_sense\ta_conf\tb_sense\tb_conf\tc_sense\tc_conf\tfinal_sense\tresolution";

    private const string MenusFile = "menus.tsv";

    /// <summary>
    ///     Reads a candidate file written by the harvest tool.
    /// </summary>
    /// <param name="path">The file.</param>
    /// <returns>The candidates.</returns>
    public static List<Candidate> ReadCandidates(string path)
    {
        var result = new List<Candidate>();
        var lines = File.ReadAllLines(path, new UTF8Encoding(false));
        for (var i = 1; i < lines.Length; i++)
        {
            var c = lines[i].TrimEnd('\r').Split('\t');
            if (c.Length != 9)
            {
                throw new InvalidDataException($"{Path.GetFileName(path)}:{i + 1}: expected 9 columns but found {c.Length}.");
            }

            result.Add(new Candidate(c[0], c[1], c[3], c[4], c[5], c[6], c[7], c[8]));
        }

        return result;
    }

    /// <summary>
    ///     Builds the sense menu text of every lemma that the rows need.
    /// </summary>
    /// <param name="corpus">The corpus directory.</param>
    /// <param name="lemmas">The lemmas.</param>
    /// <param name="gapPlan">The name of a gap plan whose senses and definitions form the menus, or null for the restore plan and the inventory.</param>
    /// <returns>The menu lines per lemma.</returns>
    public static Dictionary<string, List<string>> BuildMenus(string corpus, IReadOnlyCollection<string> lemmas, string? gapPlan = null)
    {
        var inventory = SenseInventory.ReadDirectory(Path.Combine(corpus, "senses"));
        var plan = gapPlan is null ? RealPlan.ReadPlan(corpus) : [];
        var gap = gapPlan is null ? [] : RealGap.ReadPlan(corpus, gapPlan);
        var planLemmas = plan.Select(p => p.Lemma).ToHashSet(StringComparer.Ordinal);
        var anchors = Corpus.ReadDirectory(Path.Combine(corpus, "anchors")).Where(r => lemmas.Contains(r.Lemma))
            .Select(r => (r.Lemma, r.Sense, r.Sentence)).ToList();
        if (gapPlan is null)
        {
            var (mapped, _, _) = RealPlan.ReadPool(corpus, plan);
            anchors.AddRange(mapped.Where(r => r.Split == "anchors").Select(r => (r.Lemma, r.Sense!, r.Sentence)));
        }

        var menus = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var lemma in lemmas.OrderBy(l => l, StringComparer.Ordinal))
        {
            string Tags(string sense) => inventory.RowsOf(lemma).First(d => d.Label == sense).Pos.ToString().ToLowerInvariant();
            List<(string Sense, string Tags, string Definition)> senses;
            if (gap.Any(g => g.Lemma == lemma))
            {
                senses = gap.Where(g => g.Lemma == lemma).Select(g => (g.Sense, Tags(g.Sense), g.Definition)).ToList();
            }
            else if (planLemmas.Contains(lemma))
            {
                senses = plan.Where(p => p.Lemma == lemma).Select(p => (p.Sense, string.Join('|', p.Tags), p.Definition)).ToList();
            }
            else
            {
                senses = inventory.RowsOf(lemma).Select(d => (d.Label, d.Pos.ToString().ToLowerInvariant(), string.Empty)).ToList();
            }
            var lines = new List<string>();
            foreach (var (sense, tags, definition) in senses)
            {
                var examples = anchors.Where(a => a.Lemma == lemma && a.Sense == sense)
                    .OrderBy(a => PosAudit.RowId(a.Lemma, a.Sentence), StringComparer.Ordinal).Take(3).Select(a => a.Sentence);
                lines.Add($"#menu\t{lemma}\t{sense}\t{tags}\t{definition}\t{string.Join(" // ", examples)}");
            }

            menus[lemma] = lines;
        }

        return menus;
    }

    /// <summary>
    ///     Reads the frozen menus that the export wrote to the work directory, or builds them when the directory has none.
    /// </summary>
    /// <param name="corpus">The corpus directory.</param>
    /// <param name="work">The work directory.</param>
    /// <param name="lemmas">The lemmas.</param>
    /// <returns>The menu lines per lemma.</returns>
    public static Dictionary<string, List<string>> LoadMenus(string corpus, string work, IReadOnlyCollection<string> lemmas)
    {
        var path = Path.Combine(work, MenusFile);
        if (!File.Exists(path))
        {
            return BuildMenus(corpus, lemmas);
        }

        var menus = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var line in File.ReadAllLines(path, new UTF8Encoding(false)).Where(l => l.Length > 0))
        {
            var parts = line.TrimEnd('\r').Split('\t');
            if (parts.Length < 3 || parts[0] != "#menu")
            {
                throw new InvalidDataException($"{MenusFile}: '{line}' is not a menu line.");
            }

            if (!menus.TryAdd(parts[1], [line.TrimEnd('\r')]))
            {
                menus[parts[1]].Add(line.TrimEnd('\r'));
            }
        }

        var absent = lemmas.FirstOrDefault(l => !menus.ContainsKey(l));
        return absent is null ? menus : throw new InvalidDataException($"{MenusFile}: no menu for lemma '{absent}'.");
    }

    /// <summary>
    ///     Writes the judge chunks for A (as is) and B (reversed), plus the id list used by the merge.
    /// </summary>
    /// <param name="corpus">The corpus directory.</param>
    /// <param name="candidates">The candidates to judge.</param>
    /// <param name="work">The work directory.</param>
    /// <param name="size">The chunk size.</param>
    /// <param name="gapPlan">The name of a gap plan that supplies the menus, or null.</param>
    /// <returns>The number of rows and chunks.</returns>
    public static (int Rows, int Chunks) Export(string corpus, IReadOnlyList<Candidate> candidates, string work, int size, string? gapPlan = null)
    {
        Directory.CreateDirectory(work);
        var rows = candidates.GroupBy(c => c.Id, StringComparer.Ordinal).Select(g => g.First())
            .OrderBy(c => c.Lemma, StringComparer.Ordinal).ThenBy(c => c.Id, StringComparer.Ordinal).ToList();
        var menus = BuildMenus(corpus, rows.Select(r => r.Lemma).Distinct(StringComparer.Ordinal).ToList(), gapPlan);
        File.WriteAllText(
            Path.Combine(work, MenusFile),
            string.Concat(menus.OrderBy(m => m.Key, StringComparer.Ordinal).SelectMany(m => m.Value).Select(l => l + "\n")),
            new UTF8Encoding(false));
        var chunks = new List<List<Candidate>>();
        foreach (var lemmaRows in rows.GroupBy(r => r.Lemma, StringComparer.Ordinal))
        {
            var current = chunks.Count > 0 ? chunks[^1] : null;
            foreach (var part in lemmaRows.Chunk(size))
            {
                if (current is null || current.Count + part.Length > size)
                {
                    current = [];
                    chunks.Add(current);
                }

                current.AddRange(part);
            }
        }

        var index = new StringBuilder();
        for (var i = 0; i < chunks.Count; i++)
        {
            foreach (var (judge, ordered) in new[] { ("a", chunks[i]), ("b", chunks[i].AsEnumerable().Reverse().ToList()) })
            {
                var text = new StringBuilder();
                foreach (var lemma in chunks[i].Select(r => r.Lemma).Distinct(StringComparer.Ordinal))
                {
                    foreach (var line in menus[lemma])
                    {
                        text.Append(line).Append('\n');
                    }
                }

                foreach (var row in ordered)
                {
                    text.Append(row.Id).Append('\t').Append(row.Lemma).Append('\t').Append(row.Sentence).Append('\n');
                }

                File.WriteAllText(Path.Combine(work, $"{judge}-{i + 1:D3}.tsv"), text.ToString(), new UTF8Encoding(false));
            }
        }

        foreach (var row in rows)
        {
            index.Append(row.Id).Append('\t').Append(row.Lemma).Append('\t').Append(row.Sentence).Append('\n');
        }

        File.WriteAllText(Path.Combine(work, "rows.tsv"), index.ToString(), new UTF8Encoding(false));
        return (rows.Count, chunks.Count);
    }

    /// <summary>
    ///     Parses one judge answer file strictly: one line per expected id, valid menu senses.
    /// </summary>
    /// <param name="path">The answer file.</param>
    /// <param name="expectedIds">The ids that must be answered exactly once.</param>
    /// <param name="menu">The valid sense labels per lemma.</param>
    /// <param name="lemmaOf">The lemma of every id.</param>
    /// <returns>The answers by id.</returns>
    /// <exception cref="InvalidDataException">Thrown when the file does not follow the format.</exception>
    public static Dictionary<string, SenseAnswer> ParseAnswers(
        string path,
        IReadOnlyCollection<string> expectedIds,
        IReadOnlyDictionary<string, HashSet<string>> menu,
        IReadOnlyDictionary<string, string> lemmaOf)
    {
        var name = Path.GetFileName(path);
        var answers = new Dictionary<string, SenseAnswer>(StringComparer.Ordinal);
        var number = 0;
        foreach (var raw in File.ReadAllLines(path, new UTF8Encoding(false)))
        {
            number++;
            var line = raw.TrimEnd('\r');
            if (line.Length == 0)
            {
                continue;
            }

            var c = line.Split('\t');
            if (c.Length < 3 || c.Length > 4)
            {
                throw new InvalidDataException($"{name}:{number}: expected 'id<TAB>sense<TAB>conf<TAB>note'.");
            }

            if (!lemmaOf.TryGetValue(c[0], out var lemma) || !expectedIds.Contains(c[0]))
            {
                throw new InvalidDataException($"{name}:{number}: unexpected id '{c[0]}'.");
            }

            if (c[1] != None && !menu[lemma].Contains(c[1]))
            {
                throw new InvalidDataException($"{name}:{number}: '{c[1]}' is not a sense of '{lemma}' (or none).");
            }

            var note = c.Length == 4 ? c[3] : string.Empty;
            if (!Confidences.Contains(c[2]) || ((c[2] == "L" || c[1] == None) && note.Length == 0))
            {
                throw new InvalidDataException($"{name}:{number}: confidence must be H, M or L; L and none need a note.");
            }

            if (!answers.TryAdd(c[0], new SenseAnswer(c[0], c[1], c[2], note)))
            {
                throw new InvalidDataException($"{name}:{number}: duplicate id '{c[0]}'.");
            }
        }

        var missing = expectedIds.Where(i => !answers.ContainsKey(i)).ToList();
        return missing.Count > 0 ? throw new InvalidDataException($"{name}: {missing.Count} ids are not answered, first '{missing[0]}'.") : answers;
    }

    /// <summary>
    ///     Decides whether two answers settle a row without an adjudicator.
    /// </summary>
    /// <param name="a">Judge A.</param>
    /// <param name="b">Judge B.</param>
    /// <returns>True when both give the same sense and neither is low confidence.</returns>
    public static bool AutoAccept(SenseAnswer a, SenseAnswer b) => a.Sense == b.Sense && a.Confidence != "L" && b.Confidence != "L";

    /// <summary>
    ///     Resolves a row from the judges' answers.
    /// </summary>
    /// <param name="a">Judge A.</param>
    /// <param name="b">Judge B.</param>
    /// <param name="c">The adjudicator, when consulted.</param>
    /// <returns>The final sense (or <c>undecided</c>) and the resolution.</returns>
    public static (string Final, string Resolution) Resolve(SenseAnswer a, SenseAnswer b, SenseAnswer? c)
    {
        if (AutoAccept(a, b))
        {
            return (a.Sense, "agree");
        }

        return c is null || c.Confidence == "L" ? ("undecided", "undecided") : (c.Sense, "adjudicated");
    }

    /// <summary>
    ///     Writes the adjudication chunks: rows that are not auto-accepted, with both answers.
    /// </summary>
    /// <param name="corpus">The corpus directory.</param>
    /// <param name="work">The work directory with the judge outputs.</param>
    /// <param name="size">The chunk size.</param>
    /// <returns>The number of rows that need adjudication.</returns>
    public static int PrepareAdjudication(string corpus, string work, int size)
    {
        var (rows, menu, lemmaOf) = ReadRows(corpus, work);
        var a = ReadJudge(work, "a", rows, menu, lemmaOf);
        var b = ReadJudge(work, "b", rows, menu, lemmaOf);
        var open = rows.Where(r => !AutoAccept(a[r.Id], b[r.Id])).ToList();
        var menus = LoadMenus(corpus, work, open.Select(r => r.Lemma).Distinct(StringComparer.Ordinal).ToList());
        var number = 0;
        foreach (var part in open.Chunk(size))
        {
            var text = new StringBuilder();
            foreach (var lemma in part.Select(r => r.Lemma).Distinct(StringComparer.Ordinal))
            {
                foreach (var line in menus[lemma])
                {
                    text.Append(line).Append('\n');
                }
            }

            foreach (var row in part)
            {
                text.Append($"{row.Id}\t{row.Lemma}\t{row.Sentence}\tA={a[row.Id].Sense}/{a[row.Id].Confidence}: {a[row.Id].Note}\tB={b[row.Id].Sense}/{b[row.Id].Confidence}: {b[row.Id].Note}\n");
            }

            File.WriteAllText(Path.Combine(work, $"c-{++number:D3}.tsv"), text.ToString(), new UTF8Encoding(false));
        }

        return open.Count;
    }

    /// <summary>
    ///     Merges the judge outputs of a work directory.
    /// </summary>
    /// <param name="corpus">The corpus directory.</param>
    /// <param name="work">The work directory.</param>
    /// <returns>One judgement per row.</returns>
    public static List<SenseJudgement> Merge(string corpus, string work)
    {
        var (rows, menu, lemmaOf) = ReadRows(corpus, work);
        var a = ReadJudge(work, "a", rows, menu, lemmaOf);
        var b = ReadJudge(work, "b", rows, menu, lemmaOf);
        var open = rows.Where(r => !AutoAccept(a[r.Id], b[r.Id])).ToList();
        var c = new Dictionary<string, SenseAnswer>(StringComparer.Ordinal);
        foreach (var file in Directory.GetFiles(work, "c-*.out.tsv").OrderBy(f => f, StringComparer.Ordinal))
        {
            var ids = ReadInputIds(InputPathOf(file));
            foreach (var (id, answer) in ParseAnswers(file, ids, menu, lemmaOf))
            {
                c[id] = answer;
            }
        }

        var missing = open.Where(r => !c.ContainsKey(r.Id)).ToList();
        if (missing.Count > 0)
        {
            throw new InvalidDataException($"{missing.Count} rows need adjudication but have no judge C answer, first '{missing[0].Id}'.");
        }

        return rows.Select(r =>
        {
            var answerC = c.GetValueOrDefault(r.Id);
            var (final, resolution) = Resolve(a[r.Id], b[r.Id], answerC);
            return new SenseJudgement(
                r.Id, r.Lemma, a[r.Id].Sense, a[r.Id].Confidence, b[r.Id].Sense, b[r.Id].Confidence,
                answerC?.Sense ?? "-", answerC?.Confidence ?? "-", final, resolution);
        }).ToList();
    }

    /// <summary>
    ///     Writes merged judgements as a tab-separated file with a header.
    /// </summary>
    /// <param name="path">The file.</param>
    /// <param name="results">The judgements.</param>
    public static void WriteJudgements(string path, IEnumerable<SenseJudgement> results)
    {
        var text = new StringBuilder(JudgementHeader).Append('\n');
        foreach (var r in results)
        {
            text.Append($"{r.Id}\t{r.Lemma}\t{r.A}\t{r.AConf}\t{r.B}\t{r.BConf}\t{r.C}\t{r.CConf}\t{r.Final}\t{r.Resolution}\n");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllText(path, text.ToString(), new UTF8Encoding(false));
    }

    /// <summary>
    ///     Reads merged judgements from one or more files.
    /// </summary>
    /// <param name="paths">The files.</param>
    /// <returns>The final sense per row id.</returns>
    public static Dictionary<string, SenseJudgement> ReadJudgements(IEnumerable<string> paths)
    {
        var result = new Dictionary<string, SenseJudgement>(StringComparer.Ordinal);
        foreach (var path in paths)
        {
            var lines = File.ReadAllLines(path, new UTF8Encoding(false));
            for (var i = 1; i < lines.Length; i++)
            {
                var c = lines[i].TrimEnd('\r').Split('\t');
                if (c.Length != 10)
                {
                    throw new InvalidDataException($"{Path.GetFileName(path)}:{i + 1}: expected 10 columns but found {c.Length}.");
                }

                result[c[0]] = new SenseJudgement(c[0], c[1], c[2], c[3], c[4], c[5], c[6], c[7], c[8], c[9]);
            }
        }

        return result;
    }

    private static (List<(string Id, string Lemma, string Sentence)> Rows, Dictionary<string, HashSet<string>> Menu, Dictionary<string, string> LemmaOf) ReadRows(string corpus, string work)
    {
        var rows = new List<(string, string, string)>();
        var number = 0;
        foreach (var line in File.ReadAllLines(Path.Combine(work, "rows.tsv"), new UTF8Encoding(false)))
        {
            number++;
            if (line.Length == 0)
            {
                continue;
            }

            var c = line.TrimEnd('\r').Split('\t');
            if (c.Length != 3)
            {
                throw new InvalidDataException($"rows.tsv:{number}: expected 3 columns but found {c.Length}.");
            }

            rows.Add((c[0], c[1], c[2]));
        }

        var menus = LoadMenus(corpus, work, rows.Select(r => r.Item2).Distinct(StringComparer.Ordinal).ToList());
        var menu = menus.ToDictionary(m => m.Key, m => m.Value.Select(l => l.Split('\t')[2]).ToHashSet(StringComparer.Ordinal), StringComparer.Ordinal);
        return (rows, menu, rows.ToDictionary(r => r.Item1, r => r.Item2, StringComparer.Ordinal));
    }

    private static Dictionary<string, SenseAnswer> ReadJudge(
        string work,
        string judge,
        List<(string Id, string Lemma, string Sentence)> rows,
        Dictionary<string, HashSet<string>> menu,
        Dictionary<string, string> lemmaOf)
    {
        var answers = new Dictionary<string, SenseAnswer>(StringComparer.Ordinal);
        foreach (var file in Directory.GetFiles(work, judge + "-*.out.tsv").OrderBy(f => f, StringComparer.Ordinal))
        {
            var ids = ReadInputIds(InputPathOf(file));
            foreach (var (id, answer) in ParseAnswers(file, ids, menu, lemmaOf))
            {
                answers[id] = answer;
            }
        }

        var missing = rows.Where(r => !answers.ContainsKey(r.Id)).ToList();
        return missing.Count > 0 ? throw new InvalidDataException($"Judge {judge}: {missing.Count} rows have no answer, first '{missing[0].Id}'.") : answers;
    }

    private static string InputPathOf(string outputFile) => outputFile[..^".out.tsv".Length] + ".tsv";

    private static List<string> ReadInputIds(string input)
    {
        var ids = new List<string>();
        var number = 0;
        foreach (var line in File.ReadAllLines(input, new UTF8Encoding(false)))
        {
            number++;
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            var c = line.TrimEnd('\r').Split('\t');
            if (c.Length < 3)
            {
                throw new InvalidDataException($"{Path.GetFileName(input)}:{number}: expected at least 3 columns but found {c.Length}.");
            }

            ids.Add(c[0]);
        }

        return ids;
    }
}

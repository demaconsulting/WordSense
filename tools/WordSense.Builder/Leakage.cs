using System.Globalization;
using System.Text;
using DemaConsulting.WordSense;

namespace WordSense.Builder;

/// <summary>
///     The sentences of one corpus part, for the leakage check.
/// </summary>
/// <param name="Name">The name of the part.</param>
/// <param name="Role">Whether the sentences train something or evaluate something.</param>
/// <param name="Sentences">The sentences with the place each one was read from (a file name or a row id).</param>
internal sealed record LeakageSet(string Name, LeakageRole Role, IReadOnlyList<(string Source, string Sentence)> Sentences);

/// <summary>
///     What the sentences of a corpus part are used for.
/// </summary>
internal enum LeakageRole
{
    /// <summary>The sentences train a classifier or give usage statistics.</summary>
    Training,

    /// <summary>The sentences evaluate a model or choose a threshold, and are part of the repository.</summary>
    Evaluation,

    /// <summary>The sentences evaluate a model and are not part of the repository (SemCor, the treebank test file).</summary>
    External,

    /// <summary>The sentences are reference text under another license (the example sentences of Open English WordNet, CC BY 4.0), which no part of the CC0 corpus may contain.</summary>
    Reference,
}

/// <summary>
///     Sentences that two parts share.
/// </summary>
/// <param name="First">The first part (in check order).</param>
/// <param name="Second">The second part.</param>
/// <param name="Exact">The number of sentences of the second part that the first part has too, after normalization.</param>
/// <param name="Gram">The number of sentences of the second part that share at least one run of <see cref="LeakageCheck.GramLength"/> words with the first part (the exact matches included).</param>
/// <param name="Examples">Up to five exact matches: the place in the first part, the place in the second part and the normalized sentence.</param>
/// <param name="LongExact">The exact matches that have at least <see cref="LeakageCheck.CreativeWords"/> words; a shorter sentence ("i dare you") is a stock phrase that nobody owns.</param>
internal sealed record LeakageOverlap(string First, string Second, int Exact, int Gram, IReadOnlyList<(string FirstSource, string SecondSource, string Sentence)> Examples, int LongExact = 0);

/// <summary>
///     The result of the leakage check.
/// </summary>
/// <param name="Sets">The checked parts with the number of sentences of each.</param>
/// <param name="Duplicates">The number of sentences that occur more than once inside a part, by part.</param>
/// <param name="Overlaps">The pairs of parts that share sentences (pairs without overlap are left out).</param>
internal sealed record LeakageReport(IReadOnlyList<(string Name, LeakageRole Role, int Sentences)> Sets, IReadOnlyDictionary<string, int> Duplicates, IReadOnlyList<LeakageOverlap> Overlaps)
{
    private static readonly (string, string)[] Views =
    [
        ("holdout", "chooser holdout"),
        ("anchors", "chooser anchors"),
        ("open corpus dev", "all-words gold dev"),
        ("open corpus test", "all-words gold test"),
    ];

    /// <summary>
    ///     Gets the overlaps that make the corpus invalid: a sentence that a training part shares with an evaluation part
    ///     (or an external evaluation set), that two evaluation parts share, or that a part of the corpus shares with the
    ///     reference text. Not leaks: two training parts (a duplicate, nothing is evaluated on it), a chooser part and the
    ///     sense part it is a view of, an all-words gold file and the split of the open corpus it is drawn from (the same
    ///     sentences by design), and two external sets.
    /// </summary>
    public IEnumerable<LeakageOverlap> Leaks => Overlaps.Where(IsLeak);

    private bool IsLeak(LeakageOverlap overlap)
    {
        var (first, second) = (Role(overlap.First), Role(overlap.Second));
        if (overlap.Exact == 0 || first == LeakageRole.External || second == LeakageRole.External || Views.Contains((overlap.First, overlap.Second)) || (first, second) is (LeakageRole.Training, LeakageRole.Training))
        {
            return false;
        }

        // Reference text is under another license: a sentence short enough to be a stock phrase is a coincidence
        var reference = first == LeakageRole.Reference || second == LeakageRole.Reference;
        return !reference || overlap.LongExact > 0;
    }

    private LeakageRole Role(string name) => Sets.First(s => s.Name == name).Role;

    /// <summary>
    ///     Renders the report as Markdown.
    /// </summary>
    /// <returns>The text with line feeds.</returns>
    public string Render()
    {
        var inv = CultureInfo.InvariantCulture;
        var text = new StringBuilder();
        text.Append("### Leakage check (normalized sentences)\n\n");
        text.Append("| Part | Role | Sentences | Repeated inside the part |\n| --- | --- | ---: | ---: |\n");
        foreach (var (name, role, count) in Sets)
        {
            text.Append("| ").Append(name).Append(" | ").Append(role.ToString().ToLowerInvariant()).Append(" | ")
                .Append(count.ToString("N0", inv)).Append(" | ").Append(Duplicates.GetValueOrDefault(name).ToString("N0", inv)).Append(" |\n");
        }

        text.Append('\n');
        if (Overlaps.Count == 0)
        {
            text.Append("No two parts share a sentence or a run of ").Append(LeakageCheck.GramLength.ToString(inv)).Append(" words.\n");
        }
        else
        {
            text.Append("| First part | Second part | Same sentence | Shares a run of ").Append(LeakageCheck.GramLength.ToString(inv)).Append(" words | Verdict |\n| --- | --- | ---: | ---: | --- |\n");
            foreach (var o in Overlaps)
            {
                var leak = Leaks.Contains(o);
                var verdict = "informational";
                if (leak)
                {
                    verdict = "LEAK";
                }
                else if (o.Exact > 0)
                {
                    verdict = "not a leak (training text, a view of the same sentences, a stock phrase, or an external set, which leaves such sentences out)";
                }

                text.Append("| ").Append(o.First).Append(" | ").Append(o.Second).Append(" | ").Append(o.Exact.ToString("N0", inv))
                    .Append(" | ").Append(o.Gram.ToString("N0", inv)).Append(" | ").Append(verdict).Append(" |\n");
            }

            foreach (var o in Leaks)
            {
                text.Append("\nSame sentence in ").Append(o.First).Append(" and ").Append(o.Second).Append(":\n\n");
                foreach (var (a, b, sentence) in o.Examples)
                {
                    text.Append("- ").Append(a).Append(" / ").Append(b).Append(": ").Append(sentence).Append('\n');
                }
            }
        }

        return text.ToString();
    }
}

/// <summary>
///     Checks that the training, held-out, real-text, suite, challenge and independent evaluation sentences share no sentence.
/// </summary>
/// <remarks>
///     <para>
///     Sentences are compared after normalization (<see cref="Corpus.Normalize"/>: case, spacing and punctuation do not
///     make a sentence new). A second, informational measure counts the sentences that share a run of
///     <see cref="GramLength"/> words with another part; a run of that length is rarely a coincidence, but a stock phrase
///     can be one, so it does not fail the check.
///     </para>
///     <para>
///     The parts are the fine-sense anchors, the chooser anchors, the training split of the open corpus (training); the
///     held-out sentences, the chooser held-out sentences, the real-text relabeling file, the reviewed suite, the shape probe
///     set, the development and test splits of the representative open corpus, the two halves of the all-words gold set (views of
///     those splits), the challenge set, the example sentences of Open
///     English WordNet (which the writers of the sentences saw, and which are CC BY 4.0, so none may be in the
///     CC0 corpus) and, when they are given, the independent evaluation sentences (SemCor, the treebank test file).
///     </para>
/// </remarks>
internal static class LeakageCheck
{
    /// <summary>
    ///     The length, in words, of the runs that the informational measure compares.
    /// </summary>
    public const int GramLength = 8;

    /// <summary>
    ///     The number of words from which a sentence that is also an example sentence of Open English WordNet counts as a copy
    ///     (a shorter one is a stock phrase).
    /// </summary>
    public const int CreativeWords = 5;

    /// <summary>
    ///     Reads the parts of the repository.
    /// </summary>
    /// <param name="corpus">The corpus directory.</param>
    /// <param name="data">The data directory (the example sentences of Open English WordNet).</param>
    /// <returns>The parts that exist; a missing part is left out.</returns>
    public static List<LeakageSet> Load(string corpus, string data)
    {
        List<LeakageSet> sets =
        [
            ReadDirectory("anchors", LeakageRole.Training, Path.Join(corpus, "anchors")),
            ReadDirectory("chooser anchors", LeakageRole.Training, Path.Join(corpus, "choosers", "anchors")),
            ReadDirectory("holdout", LeakageRole.Evaluation, Path.Join(corpus, "holdout")),
            ReadDirectory("chooser holdout", LeakageRole.Evaluation, Path.Join(corpus, "choosers", "holdout")),
            ReadDirectory("real text", LeakageRole.Evaluation, Path.Join(corpus, "realtext")),
            ReadSuite(Path.Join(corpus, "suite", SuiteFile.SuiteName)),
            ReadShapeProbe(corpus),
            ReadOpen(corpus, OpenCorpus.Representative, "train", "open corpus train", LeakageRole.Training),
            ReadOpen(corpus, OpenCorpus.Representative, "dev", "open corpus dev", LeakageRole.Evaluation),
            ReadOpen(corpus, OpenCorpus.Representative, "test", "open corpus test", LeakageRole.Evaluation),
            ReadOpen(corpus, OpenCorpus.Targeted, "train", "STE challenge train", LeakageRole.Training),
            ReadOpen(corpus, OpenCorpus.Targeted, "test", "STE challenge set", LeakageRole.Evaluation),
            .. AllWordsGold.LeakageSets(corpus),
            .. Gold2.LeakageSets(corpus),
            ReadExamples(Path.Join(data, "example")),
        ];
        return [.. sets.Where(s => s.Sentences.Count > 0)];
    }

    /// <summary>
    ///     Reads the gold file of a treebank test file as one sentence per sentence id.
    /// </summary>
    /// <param name="name">The name of the part.</param>
    /// <param name="goldFile">The gold file (see <see cref="TaggerEvaluator.ReadGold"/>).</param>
    /// <returns>The part.</returns>
    public static LeakageSet FromGold(string name, string goldFile) =>
        new(name, LeakageRole.External, [.. TaggerEvaluator.ReadGold(goldFile).Select((s, i) => ("sentence " + (i + 1).ToString(CultureInfo.InvariantCulture), string.Join(' ', s.Words)))]);

    /// <summary>
    ///     Reads the gold files of the treebank (train, development and test) that exist in a directory, as evaluation
    ///     sets that no training sentence may overlap.
    /// </summary>
    /// <param name="goldDirectory">The directory with <c>ud_train.tsv</c>, <c>ud_dev.tsv</c> and <c>ud_test.tsv</c>; a file that does not exist is left out.</param>
    /// <returns>The parts.</returns>
    public static List<LeakageSet> FromGoldDirectory(string goldDirectory)
    {
        ArgumentNullException.ThrowIfNull(goldDirectory);
        var sets = new List<LeakageSet>();
        foreach (var split in new[] { "train", "dev", "test" })
        {
            var file = Path.Join(goldDirectory, "ud_" + split + ".tsv");
            if (File.Exists(file))
            {
                sets.Add(FromGold("UD-EWT " + split, file));
            }
        }

        return sets;
    }

    /// <summary>
    ///     Reads the <c>read</c> set (the tense probe sentences of the heteronym work) as an external evaluation set.
    /// </summary>
    /// <param name="path">The TSV file: a header line, then <c>id</c>, <c>split</c>, <c>label</c>, <c>tokens</c>, <c>idx</c>, <c>family</c> and <c>sentence</c>.</param>
    /// <returns>The part.</returns>
    /// <exception cref="InvalidDataException">Thrown when a line is malformed.</exception>
    public static LeakageSet FromReadSet(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        var sentences = new List<(string, string)>();
        foreach (var line in File.ReadLines(path, new UTF8Encoding(false)).Skip(1))
        {
            if (line.Length == 0)
            {
                continue;
            }

            var columns = line.TrimEnd('\r').Split('\t');
            sentences.Add(columns.Length >= 7
                ? (columns[0], columns[^1])
                : throw new InvalidDataException($"The read set '{Path.GetFileName(path)}' has a line with fewer than seven columns."));
        }

        return new LeakageSet("read set", LeakageRole.External, sentences);
    }

    /// <summary>
    ///     Reads the treebank gold files of a directory that the caller named explicitly.
    /// </summary>
    /// <remarks>
    ///     An explicit path that cannot be used stops the run, because a check that quietly skips a part would pass on
    ///     fewer parts than the caller asked for. A single file of the three that is absent is still left out (see
    ///     <see cref="FromGoldDirectory"/>), as the treebank may be given in part.
    /// </remarks>
    /// <param name="goldDirectory">The directory named by <c>--gold-dir</c>.</param>
    /// <returns>The parts; at least one.</returns>
    /// <exception cref="DirectoryNotFoundException">Thrown when the directory does not exist.</exception>
    /// <exception cref="InvalidDataException">Thrown when the directory has none of the three gold files.</exception>
    public static List<LeakageSet> FromExplicitGoldDirectory(string goldDirectory)
    {
        ArgumentNullException.ThrowIfNull(goldDirectory);
        if (!Directory.Exists(goldDirectory))
        {
            throw new DirectoryNotFoundException($"The gold directory '{goldDirectory}' given by --gold-dir does not exist.");
        }

        var sets = FromGoldDirectory(goldDirectory);
        return sets.Count > 0
            ? sets
            : throw new InvalidDataException($"The gold directory '{goldDirectory}' given by --gold-dir has none of ud_train.tsv, ud_dev.tsv and ud_test.tsv.");
    }

    /// <summary>
    ///     Reads the <c>read</c> set file that the caller named explicitly.
    /// </summary>
    /// <param name="path">The file named by <c>--read-set</c>.</param>
    /// <returns>The part.</returns>
    /// <exception cref="FileNotFoundException">Thrown when the file does not exist.</exception>
    /// <exception cref="InvalidDataException">Thrown when a line is malformed.</exception>
    public static LeakageSet FromExplicitReadSet(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        return File.Exists(path)
            ? FromReadSet(path)
            : throw new FileNotFoundException($"The read set '{path}' given by --read-set does not exist.", path);
    }

    /// <summary>
    ///     Lists the sets that no training sentence of a part-of-speech head may overlap: every evaluation part of the
    ///     repository and every external evaluation set that is given.
    /// </summary>
    /// <remarks>
    ///     Without <paramref name="goldDirectory"/> and <paramref name="readSet"/> the head is checked against the parts
    ///     of the repository only. A path that is given must exist.
    /// </remarks>
    /// <param name="corpus">The corpus directory.</param>
    /// <param name="data">The data directory.</param>
    /// <param name="goldDirectory">The directory of the treebank gold files, or null.</param>
    /// <param name="readSet">The <c>read</c> set file, or null.</param>
    /// <returns>The held-out parts.</returns>
    /// <exception cref="DirectoryNotFoundException">Thrown when a given gold directory does not exist.</exception>
    /// <exception cref="FileNotFoundException">Thrown when a given read set does not exist.</exception>
    /// <exception cref="InvalidDataException">Thrown when a given gold directory has no gold file or the read set is malformed.</exception>
    public static List<LeakageSet> HeldOutSets(string corpus, string data, string? goldDirectory, string? readSet)
    {
        var sets = Load(corpus, data).Where(s => s.Role == LeakageRole.Evaluation).ToList();
        if (goldDirectory is not null)
        {
            sets.AddRange(FromExplicitGoldDirectory(goldDirectory));
        }

        if (readSet is not null)
        {
            sets.Add(FromExplicitReadSet(readSet));
        }

        return sets;
    }

    /// <summary>
    ///     Finds the training sentences that overlap a held-out part: the same sentence after normalization in any part,
    ///     or, for an external part (the treebank, the read set), a run of <see cref="GramLength"/> words in common.
    /// </summary>
    /// <param name="train">The training sentences with the place of each.</param>
    /// <param name="heldOut">The held-out parts.</param>
    /// <returns>One entry per overlap: the part, the place in the training file, the place in the part and the kind (<c>sentence</c> or <c>run of 8 words</c>).</returns>
    /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
    public static List<(string Part, string TrainSource, string HeldOutSource, string Kind)> FindTrainingOverlaps(IReadOnlyList<(string Source, string Sentence)> train, IReadOnlyList<LeakageSet> heldOut)
    {
        ArgumentNullException.ThrowIfNull(train);
        ArgumentNullException.ThrowIfNull(heldOut);
        var found = new List<(string, string, string, string)>();
        var normalizedTrain = train.Select(t => (t.Source, Text: Corpus.Normalize(t.Sentence))).Where(t => t.Text.Length > 0).ToList();
        foreach (var part in heldOut)
        {
            var sentences = new Dictionary<string, string>(StringComparer.Ordinal);
            var grams = new Dictionary<ulong, string>();
            foreach (var (source, sentence) in part.Sentences)
            {
                var text = Corpus.Normalize(sentence);
                if (text.Length == 0)
                {
                    continue;
                }

                sentences.TryAdd(text, source);
                if (part.Role == LeakageRole.External)
                {
                    foreach (var gram in GramsOf(text))
                    {
                        grams.TryAdd(gram, source);
                    }
                }
            }

            foreach (var (source, text) in normalizedTrain)
            {
                if (sentences.TryGetValue(text, out var same))
                {
                    found.Add((part.Name, source, same, "sentence"));
                }
                else if (part.Role == LeakageRole.External && GramsOf(text).Select(g => grams.GetValueOrDefault(g)).FirstOrDefault(s => s is not null) is { } shared)
                {
                    found.Add((part.Name, source, shared, "run of " + GramLength.ToString(CultureInfo.InvariantCulture) + " words"));
                }
            }
        }

        return found;
    }

    /// <summary>
    ///     Checks the parts against each other.
    /// </summary>
    /// <param name="sets">The parts, in check order.</param>
    /// <returns>The report.</returns>
    public static LeakageReport Check(IReadOnlyList<LeakageSet> sets)
    {
        ArgumentNullException.ThrowIfNull(sets);
        var normalized = sets.Select(s => s.Sentences.Select(x => (x.Source, Text: Corpus.Normalize(x.Sentence))).Where(x => x.Text.Length > 0).ToList()).ToList();

        // Exact: sentence -> the places of every part that has it
        var exact = new Dictionary<string, List<(int Set, string Source)>>(StringComparer.Ordinal);
        var duplicates = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var s = 0; s < sets.Count; s++)
        {
            var own = new HashSet<string>(StringComparer.Ordinal);
            foreach (var (source, text) in normalized[s])
            {
                if (!own.Add(text))
                {
                    duplicates[sets[s].Name] = duplicates.GetValueOrDefault(sets[s].Name) + 1;
                    continue;
                }

                if (!exact.TryGetValue(text, out var places))
                {
                    places = [];
                    exact[text] = places;
                }

                places.Add((s, source));
            }
        }

        // Runs of words: run -> the parts that have it
        var grams = new Dictionary<ulong, int>();
        for (var s = 0; s < sets.Count; s++)
        {
            foreach (var (_, text) in normalized[s])
            {
                foreach (var gram in GramsOf(text))
                {
                    grams[gram] = grams.GetValueOrDefault(gram) | (1 << s);
                }
            }
        }

        var exactCount = new int[sets.Count, sets.Count];
        var longCount = new int[sets.Count, sets.Count];
        var gramCount = new int[sets.Count, sets.Count];
        var examples = new Dictionary<(int, int), List<(string, string, string)>>();
        for (var second = 0; second < sets.Count; second++)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var (source, text) in normalized[second])
            {
                if (!seen.Add(text))
                {
                    continue;
                }

                var creative = text.Count(c => c == ' ') + 1 >= CreativeWords;
                foreach (var (first, firstSource) in exact[text].Where(p => p.Set < second))
                {
                    exactCount[first, second]++;
                    longCount[first, second] += creative ? 1 : 0;
                    if (!examples.TryGetValue((first, second), out var list))
                    {
                        list = [];
                        examples[(first, second)] = list;
                    }

                    if (list.Count < 5)
                    {
                        list.Add((firstSource, source, text));
                    }
                }

                var shared = 0;
                foreach (var gram in GramsOf(text))
                {
                    shared |= grams[gram];
                }

                for (var first = 0; first < second; first++)
                {
                    if ((shared & (1 << first)) != 0)
                    {
                        gramCount[first, second]++;
                    }
                }
            }
        }

        var overlaps = new List<LeakageOverlap>();
        for (var first = 0; first < sets.Count; first++)
        {
            for (var second = first + 1; second < sets.Count; second++)
            {
                if (exactCount[first, second] > 0 || gramCount[first, second] > 0)
                {
                    overlaps.Add(new LeakageOverlap(sets[first].Name, sets[second].Name, exactCount[first, second], gramCount[first, second], examples.GetValueOrDefault((first, second)) ?? [], longCount[first, second]));
                }
            }
        }

        return new LeakageReport([.. sets.Select((s, i) => (s.Name, s.Role, normalized[i].Count))], duplicates, overlaps);
    }

    private static IEnumerable<ulong> GramsOf(string normalized)
    {
        var words = normalized.Split(' ');
        for (var i = 0; i + GramLength <= words.Length; i++)
        {
            ulong hash = 14695981039346656037UL;
            for (var j = i; j < i + GramLength; j++)
            {
                foreach (var ch in words[j])
                {
                    hash = (hash ^ ch) * 1099511628211UL;
                }

                hash = (hash ^ ' ') * 1099511628211UL;
            }

            yield return hash;
        }
    }

    private static LeakageSet ReadDirectory(string name, LeakageRole role, string directory)
    {
        var sentences = new List<(string, string)>();
        if (Directory.Exists(directory))
        {
            foreach (var file in Directory.GetFiles(directory, "*.tsv").OrderBy(f => f, StringComparer.Ordinal))
            {
                foreach (var line in File.ReadLines(file, new UTF8Encoding(false)))
                {
                    var columns = line.TrimEnd('\r').Split('\t');
                    if (columns.Length >= 3)
                    {
                        sentences.Add((Path.GetFileName(file), columns[^1]));
                    }
                }
            }
        }

        return new LeakageSet(name, role, sentences);
    }

    private static LeakageSet ReadSuite(string path)
    {
        var sentences = new List<(string, string)>();
        if (File.Exists(path))
        {
            foreach (var row in SuiteFile.ReadRows(path))
            {
                sentences.Add((row.Id, string.Join(' ', row.Words)));
            }
        }

        return new LeakageSet("suite", LeakageRole.Evaluation, sentences);
    }

    private static LeakageSet ReadShapeProbe(string corpus) =>
        new("shape probe", LeakageRole.Evaluation, [.. ShapeProbeFile.Read(corpus).Select(r => (r.Id, string.Join(' ', r.Words)))]);

    private static LeakageSet ReadOpen(string corpus, string part, string split, string name, LeakageRole role)
    {
        var path = OpenCorpus.DataFile(corpus, part, split);
        return new LeakageSet(name, role, File.Exists(path) ? [.. OpenCorpus.ReadFile(path).Select(s => (s.SentenceId, s.Text))] : []);
    }

    private static LeakageSet ReadExamples(string directory)
    {
        var sentences = new List<(string, string)>();
        if (Directory.Exists(directory))
        {
            foreach (var file in Directory.GetFiles(directory, "*.tsv").OrderBy(f => f, StringComparer.Ordinal))
            {
                foreach (var line in File.ReadLines(file, new UTF8Encoding(false)).Skip(1))
                {
                    var columns = line.TrimEnd('\r').Split('\t');
                    if (columns.Length >= 4)
                    {
                        sentences.Add((columns[0] + "#" + columns[1], columns[3]));
                    }
                }
            }
        }

        return new LeakageSet("OEWN examples", LeakageRole.Reference, sentences);
    }
}

using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using DemaConsulting.WordSense;

#pragma warning disable S6966 // the tool writes small console and file outputs; asynchronous writes add nothing

namespace WordSense.Builder;

/// <summary>
///     One token of the all-words gold set: a word of an untrained polysemous lemma in a sentence of the open corpus, with
///     the sense that two independent labelers agreed on.
/// </summary>
/// <param name="Id">The id of the token: the split letter and a number (<c>d-0001</c>).</param>
/// <param name="Split">The split of the open corpus the sentence comes from: <c>dev</c> or <c>test</c>.</param>
/// <param name="SentenceId">The <c>sent_id</c> of the sentence in the open corpus.</param>
/// <param name="Target">The index of the token in <paramref name="Words"/>.</param>
/// <param name="Form">The token as written.</param>
/// <param name="Lemma">The lemma of the token.</param>
/// <param name="PartOfSpeech">The part of speech of the token.</param>
/// <param name="SenseId">The gold sense: the sense both labelers chose.</param>
/// <param name="Agreement"><c>high</c> when both labelers were highly confident, otherwise <c>low</c>.</param>
/// <param name="Words">The words of the sentence as a model receives them (a contraction is one word).</param>
internal sealed record AllWordsItem(string Id, string Split, string SentenceId, int Target, string Form, string Lemma, PartOfSpeech PartOfSpeech, string SenseId, string Agreement, IReadOnlyList<string> Words);

/// <summary>
///     One sampled token that is waiting for its labels.
/// </summary>
/// <param name="Id">The id of the token.</param>
/// <param name="Split">The split.</param>
/// <param name="SentenceId">The <c>sent_id</c> of the sentence.</param>
/// <param name="Target">The index of the token in <paramref name="Words"/>.</param>
/// <param name="Lemma">The lemma.</param>
/// <param name="PartOfSpeech">The part of speech.</param>
/// <param name="Words">The words of the sentence.</param>
internal sealed record AllWordsSample(string Id, string Split, string SentenceId, int Target, string Lemma, PartOfSpeech PartOfSpeech, IReadOnlyList<string> Words)
{
    /// <summary>
    ///     Gets the token as written.
    /// </summary>
    public string Form => Words[Target];
}

/// <summary>
///     The all-words gold set (<c>corpus/allwords</c>): tokens of untrained polysemous lemmas sampled from the development
///     and test splits of the representative open corpus and labeled blind by two independent labelers, the independent
///     evaluation of the ranking of the senses of the lemmas that no model scores.
/// </summary>
/// <remarks>
///     <para>
///     A file (<c>dev.tsv</c> or <c>test.tsv</c>) has header comment lines (<c>#</c>) and one token per line, nine tab-separated
///     columns: <c>id</c>, <c>sent_id</c>, <c>target</c> (the index of the word), <c>form</c>, <c>lemma</c>, <c>pos</c> (the
///     name of the part of speech), <c>sense</c> (the gold sense id), <c>agreement</c> (<c>high</c> or <c>low</c>) and
///     <c>sentence</c> (the words separated by single spaces). The dev file has tokens of the sentences of the open
///     corpus development split only and the test file of its test split only, so the two halves come from different
///     documents. The sentences are also in the open corpus, so the leakage check treats each file as a view of the
///     split it comes from: a sentence of the gold set that a training part has too is a leak.
///     </para>
///     <para>
///     The tag counts of WordNet 3.0 are counted on SemCor, so SemCor is circular for a prior that is made from them; the
///     tokens of this set are not in SemCor and no count was made on them, which is why they are the independent
///     measurement of the prior.
///     </para>
/// </remarks>
internal static class AllWordsGold
{
    /// <summary>The folder below the corpus directory.</summary>
    public const string Folder = "allwords";

    /// <summary>The name of the development half.</summary>
    public const string Dev = "dev";

    /// <summary>The name of the test half.</summary>
    public const string Test = "test";

    private const int ColumnCount = 9;

    /// <summary>
    ///     Gets the path of the file of a split.
    /// </summary>
    /// <param name="corpus">The corpus directory.</param>
    /// <param name="split"><see cref="Dev"/> or <see cref="Test"/>.</param>
    /// <returns>The path.</returns>
    public static string FilePath(string corpus, string split) => Path.Join(corpus, Folder, split + ".tsv");

    /// <summary>
    ///     Reads the gold tokens of a split and checks them against the sense catalog.
    /// </summary>
    /// <param name="corpus">The corpus directory.</param>
    /// <param name="split"><see cref="Dev"/> or <see cref="Test"/>.</param>
    /// <returns>The tokens; empty when the file does not exist.</returns>
    /// <exception cref="InvalidDataException">Thrown when a line is malformed (the wrong columns, a word that is not at its index, an index outside the sentence, negative or past its end, an agreement that is not high or low) or its sense is not a sense of its lemma and part of speech.</exception>
    public static List<AllWordsItem> Read(string corpus, string split)
    {
        var path = FilePath(corpus, split);
        var items = new List<AllWordsItem>();
        if (!File.Exists(path))
        {
            return items;
        }

        var name = Path.GetFileName(path);
        var number = 0;
        foreach (var raw in File.ReadLines(path, new UTF8Encoding(false)))
        {
            number++;
            var line = raw.TrimEnd('\r');
            if (line.Length == 0 || line[0] == '#')
            {
                continue;
            }

            var c = line.Split('\t');
            var where = $"{name}:{number.ToString(CultureInfo.InvariantCulture)}";
            if (c.Length != ColumnCount || !int.TryParse(c[2], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var target) || !Enum.TryParse<PartOfSpeech>(c[5], out var pos))
            {
                throw new InvalidDataException($"{where}: expected {ColumnCount.ToString(CultureInfo.InvariantCulture)} columns with a word index and a part of speech.");
            }

            var words = c[8].Split(' ');
            if (target < 0 || target >= words.Length || !string.Equals(words[target], c[3], StringComparison.Ordinal))
            {
                throw new InvalidDataException($"{where}: the word at index {c[2]} is not '{c[3]}'.");
            }

            if (c[7] is not ("high" or "low"))
            {
                throw new InvalidDataException($"{where}: the agreement must be high or low, not '{c[7]}'.");
            }

            if (!WordLexicon.TryGetSense(c[6], out var sense) || !string.Equals(sense.Lemma, c[4], StringComparison.Ordinal) || sense.PartOfSpeech != pos)
            {
                throw new InvalidDataException($"{where}: '{c[6]}' is not a sense of the lemma '{c[4]}' with the part of speech {c[5]}.");
            }

            items.Add(new AllWordsItem(c[0], split, c[1], target, c[3], c[4], pos, c[6], c[7], words));
        }

        return items;
    }

    /// <summary>
    ///     Renders the gold tokens of a split as the text of its file.
    /// </summary>
    /// <param name="split"><see cref="Dev"/> or <see cref="Test"/>.</param>
    /// <param name="items">The tokens.</param>
    /// <returns>The text with line feeds.</returns>
    public static string Render(string split, IEnumerable<AllWordsItem> items)
    {
        var text = new StringBuilder();
        text.Append("# corpus = all-words gold set of untrained polysemous lemmas, ").Append(split).Append(" half (independent evaluation of the usage prior)\n");
        text.Append("# license = annotations and selection CC0 1.0; sentences public domain or CC0 per corpus/open/sources.tsv\n");
        text.Append("# columns = id sent_id target form lemma pos sense agreement sentence\n");
        text.Append("# split = sentences of the ").Append(split).Append(" split of corpus/open/representative only\n");
        foreach (var i in items)
        {
            text.Append(string.Join('\t', i.Id, i.SentenceId, i.Target.ToString(CultureInfo.InvariantCulture), i.Form, i.Lemma, i.PartOfSpeech.ToString(), i.SenseId, i.Agreement, string.Join(' ', i.Words))).Append('\n');
        }

        return text.ToString();
    }

    /// <summary>
    ///     Reads the gold sets as the parts of the leakage check: each file is a view of the split of the open corpus it is
    ///     sampled from (the same sentences by design).
    /// </summary>
    /// <param name="corpus">The corpus directory.</param>
    /// <returns>The parts that have tokens.</returns>
    public static List<LeakageSet> LeakageSets(string corpus)
    {
        var sets = new List<LeakageSet>();
        foreach (var split in new[] { Dev, Test })
        {
            var items = Read(corpus, split);
            if (items.Count > 0)
            {
                sets.Add(new LeakageSet(LeakageName(split), LeakageRole.Evaluation, [.. items.Select(i => (i.Id, string.Join(' ', i.Words)))]));
            }
        }

        return sets;
    }

    /// <summary>
    ///     Gets the name of the part of the leakage check that holds the gold tokens of a split.
    /// </summary>
    /// <param name="split"><see cref="Dev"/> or <see cref="Test"/>.</param>
    /// <returns>The name.</returns>
    public static string LeakageName(string split) => $"all-words gold {split}";

    /// <summary>
    ///     Draws the tokens to label from one split of the representative open corpus.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     A token is a candidate when the open corpus tags it as a noun, a verb, an adjective or an adverb with a lemma
    ///     that is not trained (no sentence in <c>corpus/anchors</c>), the part of speech of the lemma has two or more
    ///     senses in the catalog, the lexicon lists that lemma and part of speech for the written form, the word is made
    ///     of letters, and the tag is not <c>disputed</c> between the two annotators of the corpus. The candidates are
    ///     put in a fixed pseudo-random order (a hash of the seed, the sentence and the position), so the draw is
    ///     frequency-weighted (a frequent word has more candidates) and reproducible, and the first candidates are taken
    ///     with at most <paramref name="maxPerLemma"/> for a lemma and one for a sentence.
    ///     </para>
    /// </remarks>
    /// <param name="corpus">The corpus directory.</param>
    /// <param name="split">The split of the representative part: <see cref="Dev"/> or <see cref="Test"/>.</param>
    /// <param name="count">The number of tokens to draw.</param>
    /// <param name="maxPerLemma">The most tokens of one lemma.</param>
    /// <param name="seed">The seed of the order.</param>
    /// <returns>The drawn tokens, numbered in the order of their sentences.</returns>
    public static List<AllWordsSample> Draw(string corpus, string split, int count, int maxPerLemma, int seed)
    {
        var trained = Corpus.ReadLemmas(Path.Join(corpus, "anchors"));
        var candidates = new List<(ulong Order, AllWordsSample Sample)>();
        foreach (var sentence in OpenCorpus.ReadSplit(corpus, OpenCorpus.Representative, split))
        {
            var (tagged, source) = OpenCorpus.ToTaggedWithSource(sentence);
            for (var i = 0; i < tagged.Words.Length; i++)
            {
                var token = sentence.Tokens[source[i]];
                if (CandidatePos(token, tagged.Words[i], trained) is { } pos)
                {
                    candidates.Add((OrderOf(seed, sentence.SentenceId, i), new AllWordsSample(string.Empty, split, sentence.SentenceId, i, token.Lemma, pos, tagged.Words)));
                }
            }
        }

        var perLemma = new Dictionary<string, int>(StringComparer.Ordinal);
        var sentences = new HashSet<string>(StringComparer.Ordinal);
        var drawn = new List<AllWordsSample>();
        foreach (var (_, sample) in candidates.OrderBy(c => c.Order).ThenBy(c => c.Sample.SentenceId, StringComparer.Ordinal))
        {
            if (drawn.Count >= count)
            {
                break;
            }

            if (perLemma.GetValueOrDefault(sample.Lemma) >= maxPerLemma || !sentences.Add(sample.SentenceId))
            {
                continue;
            }

            perLemma[sample.Lemma] = perLemma.GetValueOrDefault(sample.Lemma) + 1;
            drawn.Add(sample);
        }

        var letter = split[0];
        return [.. drawn.OrderBy(s => s.SentenceId, StringComparer.Ordinal).Select((s, n) => s with { Id = $"{letter}-{(n + 1).ToString("D4", CultureInfo.InvariantCulture)}" })];
    }

    private static ulong OrderOf(int seed, string sentenceId, int index) =>
        BitConverter.ToUInt64(SHA256.HashData(Encoding.UTF8.GetBytes($"{seed.ToString(CultureInfo.InvariantCulture)}|{sentenceId}|{index.ToString(CultureInfo.InvariantCulture)}")));

    private static PartOfSpeech? CandidatePos(OpenToken token, string form, HashSet<string> trained)
    {
        PartOfSpeech? pos = token.Upos switch
        {
            "NOUN" => PartOfSpeech.Noun,
            "VERB" => PartOfSpeech.Verb,
            "ADJ" => PartOfSpeech.Adjective,
            "ADV" => PartOfSpeech.Adverb,
            _ => null,
        };
        if (pos is null || token.Consensus == "disputed" || token.Lemma.Length == 0 || !form.All(char.IsLetter) || trained.Contains(token.Lemma))
        {
            return null;
        }

        if (WordLexicon.SensesOf(token.Lemma, pos.Value).Length < 2 || !WordLexicon.TryLookup(form, out var analyses))
        {
            return null;
        }

        return analyses.Any(a => a.PartOfSpeech == pos.Value && string.Equals(a.Lemma, token.Lemma, StringComparison.Ordinal)) ? pos : null;
    }

    /// <summary>
    ///     Writes the batches of the labelers and the table that maps them back to the tokens.
    /// </summary>
    /// <remarks>
    ///     A batch is a JSON file <c>{"batch": id, "groups": [{"senses": [...], "items": [...]}]}</c> in the format of the
    ///     labeling batches of the quota filling (<c>tools/quota-fill/prompts/sentence-labeler.md</c>): a group is a lemma
    ///     and a part of speech with all its senses (id, part of speech and gloss) and its sentences, the target word in
    ///     braces. The senses of a group are listed in the order of their ids, so the order of the inventory (which is the
    ///     order of frequency) is not shown, and no tag count is given. The batches hold at most
    ///     <paramref name="batchSize"/> tokens of one split.
    /// </remarks>
    /// <param name="data">The data directory (the glosses).</param>
    /// <param name="samples">The tokens.</param>
    /// <param name="directory">The directory that receives <c>batches/*.json</c> and <c>sample.tsv</c>.</param>
    /// <param name="batchSize">The most tokens in a batch.</param>
    /// <returns>The number of batches written.</returns>
    public static int WriteBatches(string data, IReadOnlyList<AllWordsSample> samples, string directory, int batchSize)
    {
        var batchDirectory = Path.Join(directory, "batches");
        Directory.CreateDirectory(batchDirectory);
        var lemmas = samples.Select(s => s.Lemma).Distinct(StringComparer.Ordinal).ToList();
        var glosses = AppendixGenerator.ReadGlosses(data, lemmas);
        var table = new StringBuilder("batch\tn\tid\tsplit\tsent_id\ttarget\tlemma\tpos\n");
        var batches = 0;
        foreach (var split in samples.Select(s => s.Split).Distinct(StringComparer.Ordinal))
        {
            var groups = samples.Where(s => s.Split == split)
                .GroupBy(s => (s.Lemma, s.PartOfSpeech))
                .OrderBy(g => g.Key.Lemma, StringComparer.Ordinal).ThenBy(g => g.Key.PartOfSpeech)
                .ToList();
            var current = new List<IGrouping<(string Lemma, PartOfSpeech PartOfSpeech), AllWordsSample>>();
            var size = 0;
            foreach (var group in groups)
            {
                if (size > 0 && size + group.Count() > batchSize)
                {
                    WriteBatch(batchDirectory, split, ++batches, current, glosses, table);
                    current.Clear();
                    size = 0;
                }

                current.Add(group);
                size += group.Count();
            }

            if (current.Count > 0)
            {
                WriteBatch(batchDirectory, split, ++batches, current, glosses, table);
            }
        }

        File.WriteAllText(Path.Join(directory, "sample.tsv"), table.ToString(), new UTF8Encoding(false));
        return batches;
    }

    private static void WriteBatch(string directory, string split, int number, List<IGrouping<(string Lemma, PartOfSpeech PartOfSpeech), AllWordsSample>> groups, Dictionary<string, string> glosses, StringBuilder table)
    {
        var id = $"aw-{split[0]}{number.ToString("D2", CultureInfo.InvariantCulture)}";
        var json = new StringBuilder();
        json.Append("{\n \"batch\": ").Append(Quote(id)).Append(",\n \"groups\": [\n");
        var n = 0;
        for (var g = 0; g < groups.Count; g++)
        {
            var (lemma, pos) = groups[g].Key;
            json.Append("  {\n   \"senses\": [\n");
            var senses = WordLexicon.SensesOf(lemma, pos).OrderBy(s => s.SenseId, StringComparer.Ordinal).ToList();
            for (var s = 0; s < senses.Count; s++)
            {
                var gloss = glosses.GetValueOrDefault(senses[s].SenseId, "(no gloss)");
                if (gloss.Length > 170)
                {
                    gloss = gloss[..167].TrimEnd() + "...";
                }

                json.Append("    {\"id\": ").Append(Quote(senses[s].SenseId)).Append(", \"pos\": ").Append(Quote(pos.ToString().ToLowerInvariant())).Append(", \"gloss\": ").Append(Quote(gloss)).Append('}').Append(s + 1 < senses.Count ? "," : string.Empty).Append('\n');
            }

            json.Append("   ],\n   \"items\": [\n");
            var items = groups[g].ToList();
            for (var k = 0; k < items.Count; k++)
            {
                n++;
                var sample = items[k];
                var words = sample.Words.Select((w, i) => i == sample.Target ? "{" + w + "}" : w);
                json.Append("    {\"n\": ").Append(n.ToString(CultureInfo.InvariantCulture)).Append(", \"sentence\": ").Append(Quote(string.Join(' ', words))).Append('}').Append(k + 1 < items.Count ? "," : string.Empty).Append('\n');
                table.Append(string.Join('\t', id, n.ToString(CultureInfo.InvariantCulture), sample.Id, sample.Split, sample.SentenceId, sample.Target.ToString(CultureInfo.InvariantCulture), sample.Lemma, sample.PartOfSpeech.ToString())).Append('\n');
            }

            json.Append("   ]\n  }").Append(g + 1 < groups.Count ? "," : string.Empty).Append('\n');
        }

        json.Append(" ]\n}\n");
        File.WriteAllText(Path.Join(directory, id + ".json"), json.ToString(), new UTF8Encoding(false));
    }

    private static string Quote(string text) => System.Text.Json.JsonSerializer.Serialize(text, JsonOptions);

    private static readonly System.Text.Json.JsonSerializerOptions JsonOptions = new() { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    /// <summary>
    ///     What the two labelers said about the sampled tokens of one split.
    /// </summary>
    /// <param name="Split">The split.</param>
    /// <param name="Sampled">The tokens that were given to the labelers.</param>
    /// <param name="BothAnswered">The tokens that both labelers answered with a sense.</param>
    /// <param name="Agreed">The tokens whose two senses are the same.</param>
    /// <param name="BothHigh">The agreed tokens that both labelers were highly confident of.</param>
    /// <param name="UnsureFirst">The tokens the first labeler answered <c>unsure</c> (or not at all).</param>
    /// <param name="UnsureSecond">The tokens the second labeler answered <c>unsure</c> (or not at all).</param>
    /// <param name="Lemmas">The distinct lemmas of the kept tokens.</param>
    internal sealed record LabelAgreement(string Split, int Sampled, int BothAnswered, int Agreed, int BothHigh, int UnsureFirst, int UnsureSecond, int Lemmas);

    /// <summary>
    ///     Merges the answers of two labelers into the gold tokens: a token is kept when both labelers chose the same sense
    ///     (with a high or a low confidence); an unsure answer and a disagreement drop the token.
    /// </summary>
    /// <param name="directory">The directory of the batches (<c>sample.tsv</c> and the answers).</param>
    /// <param name="firstSuffix">The suffix of the answer files of the first labeler: the answers of batch <c>aw-d01</c> are in <c>aw-d01.&lt;suffix&gt;.tsv</c>.</param>
    /// <param name="secondSuffix">The suffix of the answer files of the second labeler.</param>
    /// <returns>The kept tokens by split, and the agreement figures of each split.</returns>
    /// <exception cref="InvalidDataException">Thrown when an answer file is missing or malformed, or names a sense that the token cannot have.</exception>
    public static (Dictionary<string, List<AllWordsItem>> Gold, List<LabelAgreement> Agreement) Merge(string directory, string firstSuffix, string secondSuffix)
    {
        var rows = File.ReadAllLines(Path.Join(directory, "sample.tsv"), new UTF8Encoding(false)).Skip(1).Where(l => l.Length > 0).Select(l => l.Split('\t')).ToList();
        var sentences = ReadSampleSentences(directory, rows);
        var answers = new[] { firstSuffix, secondSuffix }.Select(suffix => ReadAnswers(directory, suffix, rows)).ToArray();
        var gold = new Dictionary<string, List<AllWordsItem>>(StringComparer.Ordinal);
        var stats = new Dictionary<string, int[]>(StringComparer.Ordinal);
        var lemmas = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var r in rows)
        {
            var (id, split, lemma, pos) = (r[2], r[3], r[6], Enum.Parse<PartOfSpeech>(r[7]));
            if (!stats.TryGetValue(split, out var s))
            {
                s = new int[7];
                stats[split] = s;
            }

            s[0]++;
            var key = (r[0], r[1]);
            var (a, b) = (answers[0][key], answers[1][key]);
            s[5] += a.Sense is null ? 1 : 0;
            s[6] += b.Sense is null ? 1 : 0;
            if (a.Sense is null || b.Sense is null)
            {
                continue;
            }

            s[1]++;
            if (!string.Equals(a.Sense, b.Sense, StringComparison.Ordinal))
            {
                continue;
            }

            s[2]++;
            var high = a.High && b.High;
            s[3] += high ? 1 : 0;
            var words = sentences[id];
            var item = new AllWordsItem(id, split, r[4], int.Parse(r[5], CultureInfo.InvariantCulture), words[int.Parse(r[5], CultureInfo.InvariantCulture)], lemma, pos, a.Sense, high ? "high" : "low", words);
            if (!gold.TryGetValue(split, out var list))
            {
                gold[split] = list = [];
            }

            list.Add(item);
            if (!lemmas.TryGetValue(split, out var set))
            {
                lemmas[split] = set = new HashSet<string>(StringComparer.Ordinal);
            }

            set.Add(lemma);
        }

        var agreement = stats.OrderBy(p => p.Key, StringComparer.Ordinal)
            .Select(p => new LabelAgreement(p.Key, p.Value[0], p.Value[1], p.Value[2], p.Value[3], p.Value[5], p.Value[6], lemmas.TryGetValue(p.Key, out var l) ? l.Count : 0))
            .ToList();
        return (gold, agreement);
    }

    // The words of every sampled token, from the batch files: the sentence with its braces is the only record of the words
    private static Dictionary<string, string[]> ReadSampleSentences(string directory, List<string[]> rows)
    {
        var words = new Dictionary<string, string[]>(StringComparer.Ordinal);
        foreach (var batch in rows.Select(r => r[0]).Distinct(StringComparer.Ordinal))
        {
            using var document = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Join(directory, "batches", batch + ".json"), new UTF8Encoding(false)));
            var ids = rows.Where(r => r[0] == batch).ToDictionary(r => int.Parse(r[1], CultureInfo.InvariantCulture), r => r[2]);
            foreach (var group in document.RootElement.GetProperty("groups").EnumerateArray())
            {
                foreach (var item in group.GetProperty("items").EnumerateArray())
                {
                    var sentence = item.GetProperty("sentence").GetString()!;
                    words[ids[item.GetProperty("n").GetInt32()]] = [.. sentence.Replace("{", string.Empty, StringComparison.Ordinal).Replace("}", string.Empty, StringComparison.Ordinal).Split(' ')];
                }
            }
        }

        return words;
    }

    private static Dictionary<(string Batch, string N), (string? Sense, bool High)> ReadAnswers(string directory, string suffix, List<string[]> rows)
    {
        var answers = new Dictionary<(string, string), (string?, bool)>();
        foreach (var batch in rows.Select(r => r[0]).Distinct(StringComparer.Ordinal))
        {
            var path = Path.Join(directory, $"{batch}.{suffix}.tsv");
            if (!File.Exists(path))
            {
                throw new InvalidDataException($"The answers of the labeler '{suffix}' for the batch '{batch}' are missing: {path}");
            }

            foreach (var line in File.ReadAllLines(path, new UTF8Encoding(false)).Where(l => l.Trim().Length > 0))
            {
                var c = line.TrimEnd('\r').Split('\t');
                answers[(batch, c[0].Trim())] = c.Length >= 3 && c[1] != "unsure" ? (c[1].Trim(), c[2].Trim() == "high") : (null, false);
            }
        }

        // A sense answer must be a sense of the lemma and part of speech of its token; a missing line counts as unsure
        var result = new Dictionary<(string Batch, string N), (string? Sense, bool High)>();
        foreach (var r in rows)
        {
            var (sense, high) = answers.GetValueOrDefault((r[0], r[1]), (null, false));
            if (sense is not null && !WordLexicon.SensesOf(r[6], Enum.Parse<PartOfSpeech>(r[7])).Any(s => string.Equals(s.SenseId, sense, StringComparison.Ordinal)))
            {
                throw new InvalidDataException($"The labeler '{suffix}' answered '{sense}' for {r[0]} #{r[1]}, which is not a sense of {r[6]} ({r[7]}).");
            }

            result[(r[0], r[1])] = (sense, high);
        }

        return result;
    }

    /// <summary>
    ///     Renders the agreement of the two labelers as Markdown.
    /// </summary>
    /// <param name="agreement">The figures of each split.</param>
    /// <returns>The text with line feeds.</returns>
    public static string RenderAgreement(IReadOnlyList<LabelAgreement> agreement)
    {
        ArgumentNullException.ThrowIfNull(agreement);
        var inv = CultureInfo.InvariantCulture;
        var text = new StringBuilder("| Split | Tokens sampled | Both answered a sense | Same sense | Agreement (of both answered) | Same sense, both high | First unsure | Second unsure | Kept (share of sampled) | Lemmas |\n");
        text.Append("| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |\n");
        foreach (var a in agreement.Concat([new LabelAgreement("all", agreement.Sum(x => x.Sampled), agreement.Sum(x => x.BothAnswered), agreement.Sum(x => x.Agreed), agreement.Sum(x => x.BothHigh), agreement.Sum(x => x.UnsureFirst), agreement.Sum(x => x.UnsureSecond), agreement.Sum(x => x.Lemmas))]))
        {
            text.Append("| ").Append(a.Split).Append(" | ").Append(a.Sampled.ToString("N0", inv)).Append(" | ").Append(a.BothAnswered.ToString("N0", inv))
                .Append(" | ").Append(a.Agreed.ToString("N0", inv)).Append(" | ").Append(EvaluationResult.Percent(a.Agreed, a.BothAnswered))
                .Append(" | ").Append(a.BothHigh.ToString("N0", inv)).Append(" | ").Append(a.UnsureFirst.ToString("N0", inv)).Append(" | ").Append(a.UnsureSecond.ToString("N0", inv))
                .Append(" | ").Append(EvaluationResult.Percent(a.Agreed, a.Sampled)).Append(" | ").Append(a.Split == "all" ? "n/a" : a.Lemmas.ToString("N0", inv)).Append(" |\n");
        }

        return text.ToString();
    }
}

using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using DemaConsulting.WordSense;

namespace WordSense.Builder;

/// <summary>
///     The raw answer of a labeler for one token: the analysis it chose, the sense and its confidence.
/// </summary>
/// <param name="Analysis">The analysis: <c>a1</c>, <c>a2</c> (the position in the list of the analyses of the form) or <c>other:Pos:lemma</c> when none fits.</param>
/// <param name="Sense">The sense id, <c>unsure</c>, <c>none</c> (no defensible single sense; the adjudicator only) or <c>-</c>.</param>
/// <param name="High">Whether the labeler was highly confident.</param>
internal sealed record Gold2Answer(string Analysis, string Sense, bool High);

/// <summary>
///     A label with the analysis resolved: what a labeler or the adjudicator said about one token.
/// </summary>
/// <param name="Pos">The part of speech: the name of a <see cref="PartOfSpeech"/> or <c>Other</c>.</param>
/// <param name="Lemma">The lemma.</param>
/// <param name="Features">The features of the analysis, or empty.</param>
/// <param name="Sense">The sense id, <c>unsure</c>, <c>none</c> or <c>-</c> (the analysis has fewer than two senses).</param>
/// <param name="High">Whether the labeler was highly confident.</param>
/// <param name="Scored">Whether the analysis is one of the lexicon with two or more senses, so that the sense is a choice.</param>
internal sealed record Gold2Label(string Pos, string Lemma, string Features, string Sense, bool High, bool Scored)
{
    /// <summary>
    ///     Gets the key of the analysis: part of speech, lemma and features.
    /// </summary>
    public string Analysis => $"{Pos}|{Lemma}|{Features}";

    /// <summary>
    ///     Renders the label for the labels file.
    /// </summary>
    /// <returns>The label as <c>Pos|lemma|features|sense|confidence</c>.</returns>
    public string Render() => string.Join('|', Pos, Lemma, Features.Length == 0 ? "-" : Features, Sense, High ? "high" : "low");
}

/// <summary>
///     What the two labelers and the adjudicator said about one token and how the label was settled.
/// </summary>
/// <param name="Token">The token.</param>
/// <param name="First">The label of the first labeler.</param>
/// <param name="Second">The label of the second labeler.</param>
/// <param name="Adjudicated">The label of the adjudicator, or null when the labelers agreed.</param>
/// <param name="Final">The gold label.</param>
/// <param name="Agreement">What the labelers agreed on (see <see cref="Gold2Row.Agreement"/>).</param>
internal sealed record Gold2Outcome(Gold2Token Token, Gold2Label First, Gold2Label Second, Gold2Label? Adjudicated, Gold2Label Final, string Agreement)
{
    /// <summary>
    ///     Gets how the label was settled: <c>agreed</c> or <c>adjudicated</c>.
    /// </summary>
    public string Status => Adjudicated is null ? "agreed" : "adjudicated";
}

/// <summary>
///     What the labelers and the adjudicator did on one split.
/// </summary>
/// <param name="Split">The split.</param>
/// <param name="Tokens">The tokens.</param>
/// <param name="AnalysisAgreed">The tokens on which the labelers chose the same analysis.</param>
/// <param name="PosAgreed">The tokens on which the labelers chose the same part of speech.</param>
/// <param name="LemmaAgreed">The tokens on which the labelers chose the same lemma.</param>
/// <param name="Verbs">The tokens that both labelers took for a verb of the lexicon.</param>
/// <param name="TenseAgreed">The verbs on which the labelers chose the same tense class.</param>
/// <param name="SenseChoices">The tokens on which the labelers chose the same analysis, of two or more senses.</param>
/// <param name="SenseBoth">The sense choices that both labelers answered with a sense (neither unsure).</param>
/// <param name="SenseAgreed">The sense choices on which both labelers chose the same sense.</param>
/// <param name="UnsureFirst">The tokens whose sense the first labeler left unsure.</param>
/// <param name="UnsureSecond">The tokens whose sense the second labeler left unsure.</param>
/// <param name="FullAgreed">The tokens that were settled by the labelers alone (the same analysis and the same sense).</param>
/// <param name="Adjudicated">The tokens that the adjudicator settled.</param>
/// <param name="AdjudicatedNone">The adjudicated tokens that have no defensible single sense.</param>
/// <param name="SidedFirst">The adjudicated tokens on which the adjudicator chose the analysis and sense of the first labeler.</param>
/// <param name="SidedSecond">The adjudicated tokens on which the adjudicator chose the analysis and sense of the second labeler.</param>
internal sealed record Gold2Agreement(string Split, int Tokens, int AnalysisAgreed, int PosAgreed, int LemmaAgreed, int Verbs, int TenseAgreed, int SenseChoices, int SenseBoth, int SenseAgreed, int UnsureFirst, int UnsureSecond, int FullAgreed, int Adjudicated, int AdjudicatedNone, int SidedFirst, int SidedSecond);

/// <summary>
///     The labeling of the second all-words gold set: the batches that the labelers and the adjudicator answer, the reading
///     of their answers and the merge into the gold rows.
/// </summary>
/// <remarks>
///     <para>
///     A token is labeled with the analyses of the lexicon for its form. A labeler sees the sentence with the token in
///     braces and the list of the analyses (<c>a1</c>, <c>a2</c>, ...) with their features and, for each analysis of two or
///     more senses, the senses with their glosses in the order of their ids (no tag count, no order of frequency). It answers
///     the analysis (or <c>other:Pos:lemma</c> when none fits), the sense (or <c>unsure</c>) and its confidence in a row
///     <c>n&lt;TAB&gt;analysis&lt;TAB&gt;sense&lt;TAB&gt;confidence</c>. The analysis gives the part of speech, the lemma and the
///     features (so the tense) and the sense gives the sense. A token is settled by the labelers when they chose the same analysis
///     and the same sense; every other token, including every <c>unsure</c>, goes to the adjudicator, who sees both answers and
///     may answer <c>none</c> when no single sense is defensible. Nothing is dropped.
///     </para>
/// </remarks>
internal static class Gold2Labeling
{
    private static readonly System.Text.Json.JsonSerializerOptions JsonOptions = new() { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    /// <summary>
    ///     Gets the analyses of the lexicon for a written form, in a fixed order that shows no frequency: lemma, part of speech, features.
    /// </summary>
    /// <param name="form">The token as written.</param>
    /// <returns>The distinct analyses.</returns>
    public static List<WordDetails> AnalysesOf(string form)
    {
        ArgumentNullException.ThrowIfNull(form);
        return [.. WordLexicon.Lookup(BasicClassifier.Normalize(form)).Distinct().OrderBy(a => a.Lemma, StringComparer.Ordinal).ThenBy(a => a.PartOfSpeech).ThenBy(a => a.Features, StringComparer.Ordinal)];
    }

    /// <summary>
    ///     Resolves the answer of a labeler to a label.
    /// </summary>
    /// <param name="token">The token.</param>
    /// <param name="answer">The answer.</param>
    /// <param name="adjudicator">Whether the answer is the adjudicator's (which may be <c>none</c>).</param>
    /// <param name="label">Receives the label.</param>
    /// <returns>Null when the answer is valid, else the problem.</returns>
    public static string? Resolve(Gold2Token token, Gold2Answer answer, bool adjudicator, out Gold2Label? label)
    {
        ArgumentNullException.ThrowIfNull(token);
        ArgumentNullException.ThrowIfNull(answer);
        label = null;
        var analyses = AnalysesOf(token.Form);
        string pos, lemma, features;
        var scored = false;
        var sense = answer.Sense;
        if (answer.Analysis.StartsWith("other:", StringComparison.Ordinal))
        {
            var parts = answer.Analysis.Split(':', 3);
            if (parts.Length != 3 || parts[2].Length == 0 || !IsPosName(parts[1]))
            {
                return $"'{answer.Analysis}' is not other:Pos:lemma with a part of speech of the lexicon or Other";
            }

            (pos, lemma, features) = (parts[1], parts[2], string.Empty);
            sense = "-";
        }
        else
        {
            if (answer.Analysis.Length < 2 || answer.Analysis[0] != 'a' || !int.TryParse(answer.Analysis.AsSpan(1), NumberStyles.None, CultureInfo.InvariantCulture, out var index) || index < 1 || index > analyses.Count)
            {
                return $"'{answer.Analysis}' is no analysis of '{token.Form}' (it has {analyses.Count.ToString(CultureInfo.InvariantCulture)})";
            }

            var chosen = analyses[index - 1];
            (pos, lemma, features) = (chosen.PartOfSpeech.ToString(), chosen.Lemma, chosen.Features);
            var senses = WordLexicon.SensesOf(chosen.Lemma, chosen.PartOfSpeech);
            if (senses.Length < 2)
            {
                sense = "-";
            }
            else
            {
                scored = true;
                var allowed = sense == "unsure" || (adjudicator && sense == "none") || senses.Any(s => string.Equals(s.SenseId, sense, StringComparison.Ordinal));
                if (!allowed)
                {
                    return $"'{sense}' is no sense of {chosen.Lemma} ({chosen.PartOfSpeech})";
                }
            }
        }

        label = new Gold2Label(pos, lemma, features, adjudicator && sense == "unsure" ? "none" : sense, answer.High, scored);
        return null;
    }

    private static bool IsPosName(string name) => name == "Other" || Enum.TryParse<PartOfSpeech>(name, false, out _);

    /// <summary>
    ///     Writes the batches of a labeling stage and the table that maps them back to the tokens.
    /// </summary>
    /// <param name="data">The data directory (the glosses).</param>
    /// <param name="tokens">The tokens, all of one split.</param>
    /// <param name="directory">The directory that receives <c>batches/*.json</c> and the table (<c>sample.tsv</c> or <c>adjudication.tsv</c>).</param>
    /// <param name="batchSize">The most tokens in a batch.</param>
    /// <param name="stage"><c>label</c> or <c>adjudicate</c>.</param>
    /// <param name="answers">The answers of the two labelers by token id (the adjudication stage only).</param>
    /// <param name="tag">A tag that makes the names of a further round of batches differ from the first round (<c>r1</c>), or empty.</param>
    /// <returns>The number of batches written.</returns>
    public static int WriteBatches(string data, IReadOnlyList<Gold2Token> tokens, string directory, int batchSize, string stage, IReadOnlyDictionary<string, (Gold2Answer First, Gold2Answer Second)>? answers = null, string tag = "")
    {
        ArgumentNullException.ThrowIfNull(tokens);
        var batchDirectory = Path.Join(directory, "batches");
        Directory.CreateDirectory(batchDirectory);
        var lemmas = tokens.SelectMany(t => AnalysesOf(t.Form)).Select(a => a.Lemma).Distinct(StringComparer.Ordinal).ToList();
        var glosses = AppendixGenerator.ReadGlosses(data, lemmas);
        var tableName = (stage == "adjudicate" ? "adjudication" : "sample") + tag + ".tsv";
        var table = new StringBuilder("batch\tn\tid\n");
        var prefix = (stage == "adjudicate" ? "g2a" : "g2") + tag;
        var batches = 0;
        foreach (var split in tokens.Select(t => t.Split).Distinct(StringComparer.Ordinal))
        {
            var chunks = new List<List<Gold2Token>>();
            var current = new List<Gold2Token>();
            foreach (var token in tokens.Where(t => t.Split == split).OrderBy(t => t.Form.ToLowerInvariant(), StringComparer.Ordinal).ThenBy(t => t.Id, StringComparer.Ordinal))
            {
                if (current.Count >= batchSize)
                {
                    chunks.Add(current);
                    current = [];
                }

                current.Add(token);
            }

            if (current.Count > 0)
            {
                chunks.Add(current);
            }

            foreach (var chunk in chunks)
            {
                batches++;
                var id = $"{prefix}-{split[0]}{batches.ToString("D2", CultureInfo.InvariantCulture)}";
                File.WriteAllText(Path.Join(batchDirectory, id + ".json"), BatchJson(id, stage, chunk, glosses, answers, table), new UTF8Encoding(false));
            }
        }

        File.WriteAllText(Path.Join(directory, tableName), table.ToString(), new UTF8Encoding(false));
        return batches;
    }

    private static string BatchJson(string id, string stage, List<Gold2Token> chunk, Dictionary<string, string> glosses, IReadOnlyDictionary<string, (Gold2Answer First, Gold2Answer Second)>? answers, StringBuilder table)
    {
        var json = new StringBuilder();
        json.Append("{\n \"batch\": ").Append(Quote(id)).Append(",\n \"stage\": ").Append(Quote(stage)).Append(",\n \"groups\": [\n");
        var groups = chunk.GroupBy(t => t.Form.ToLowerInvariant(), StringComparer.Ordinal).ToList();
        var n = 0;
        for (var g = 0; g < groups.Count; g++)
        {
            var analyses = AnalysesOf(groups[g].First().Form);
            json.Append("  {\n   \"form\": ").Append(Quote(groups[g].Key)).Append(",\n   \"analyses\": [\n");
            var shown = new Dictionary<(string, PartOfSpeech), string>();
            for (var a = 0; a < analyses.Count; a++)
            {
                var analysis = analyses[a];
                var key = $"a{(a + 1).ToString(CultureInfo.InvariantCulture)}";
                json.Append("    {\"a\": ").Append(Quote(key)).Append(", \"lemma\": ").Append(Quote(analysis.Lemma)).Append(", \"pos\": ").Append(Quote(analysis.PartOfSpeech.ToString())).Append(", \"features\": ").Append(Quote(analysis.Features));
                var senses = WordLexicon.SensesOf(analysis.Lemma, analysis.PartOfSpeech);
                if (senses.Length >= 2)
                {
                    if (shown.TryGetValue((analysis.Lemma, analysis.PartOfSpeech), out var same))
                    {
                        json.Append(", \"senses_of\": ").Append(Quote(same));
                    }
                    else
                    {
                        shown[(analysis.Lemma, analysis.PartOfSpeech)] = key;
                        json.Append(", \"senses\": [\n");
                        var ordered = senses.OrderBy(s => s.SenseId, StringComparer.Ordinal).ToList();
                        for (var s = 0; s < ordered.Count; s++)
                        {
                            var gloss = glosses.GetValueOrDefault(ordered[s].SenseId, "(no gloss)");
                            gloss = gloss.Length > 150 ? gloss[..147].TrimEnd() + "..." : gloss;
                            json.Append("      {\"id\": ").Append(Quote(ordered[s].SenseId)).Append(", \"gloss\": ").Append(Quote(gloss)).Append('}').Append(s + 1 < ordered.Count ? "," : string.Empty).Append('\n');
                        }

                        json.Append("    ]");
                    }
                }

                json.Append('}').Append(a + 1 < analyses.Count ? "," : string.Empty).Append('\n');
            }

            json.Append("   ],\n   \"items\": [\n");
            var items = groups[g].ToList();
            for (var k = 0; k < items.Count; k++)
            {
                n++;
                var token = items[k];
                var words = token.Words.Select((w, i) => i == token.Target ? "{" + w + "}" : w);
                json.Append("    {\"n\": ").Append(n.ToString(CultureInfo.InvariantCulture)).Append(", \"sentence\": ").Append(Quote(string.Join(' ', words)));
                if (answers is not null && answers.TryGetValue(token.Id, out var pair))
                {
                    json.Append(", \"labeler1\": ").Append(AnswerJson(pair.First)).Append(", \"labeler2\": ").Append(AnswerJson(pair.Second));
                }

                json.Append('}').Append(k + 1 < items.Count ? "," : string.Empty).Append('\n');
                table.Append(string.Join('\t', id, n.ToString(CultureInfo.InvariantCulture), token.Id)).Append('\n');
            }

            json.Append("   ]\n  }").Append(g + 1 < groups.Count ? "," : string.Empty).Append('\n');
        }

        json.Append(" ]\n}\n");
        return json.ToString();
    }

    private static string AnswerJson(Gold2Answer answer) =>
        "{\"analysis\": " + Quote(answer.Analysis) + ", \"sense\": " + Quote(answer.Sense) + ", \"confidence\": " + Quote(answer.High ? "high" : "low") + "}";

    private static string Quote(string text) => System.Text.Json.JsonSerializer.Serialize(text, JsonOptions);

    /// <summary>
    ///     Reads the answers of one labeler: the files <c>&lt;batch&gt;.&lt;suffix&gt;.tsv</c> of the directory, each with rows
    ///     <c>n&lt;TAB&gt;analysis&lt;TAB&gt;sense&lt;TAB&gt;confidence</c>.
    /// </summary>
    /// <param name="directory">The directory of the batches (with <c>sample.tsv</c> or <c>adjudication.tsv</c>).</param>
    /// <param name="suffix">The suffix of the labeler's files.</param>
    /// <param name="adjudication">Whether the answers are the adjudicator's.</param>
    /// <returns>The answers by token id and the problems found in the files.</returns>
    public static (Dictionary<string, Gold2Answer> Answers, List<string> Problems) ReadAnswers(string directory, string suffix, bool adjudication)
    {
        var tableStem = adjudication ? "adjudication" : "sample";
        var map = new Dictionary<(string Batch, string N), string>();
        foreach (var table in Directory.GetFiles(directory, tableStem + "*.tsv").Order(StringComparer.Ordinal))
        {
            foreach (var line in File.ReadLines(table, new UTF8Encoding(false)).Skip(1))
            {
                var c = line.TrimEnd('\r').Split('\t');
                if (c.Length == 3)
                {
                    map[(c[0], c[1])] = c[2];
                }
            }
        }

        var answers = new Dictionary<string, Gold2Answer>(StringComparer.Ordinal);
        var problems = new List<string>();
        var batchDirectory = Path.Join(directory, "batches");
        foreach (var batch in map.Keys.Select(k => k.Batch).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal))
        {
            var path = Path.Join(directory, $"{batch}.{suffix}.tsv");
            if (!File.Exists(path))
            {
                path = Path.Join(batchDirectory, $"{batch}.{suffix}.tsv");
            }

            if (!File.Exists(path))
            {
                continue;
            }

            foreach (var raw in File.ReadLines(path, new UTF8Encoding(false)))
            {
                var line = raw.TrimEnd('\r');
                if (line.Length == 0 || line[0] == '#' || line.StartsWith("n\t", StringComparison.Ordinal))
                {
                    continue;
                }

                var c = line.Split('\t');
                if (c.Length < 3 || !map.TryGetValue((batch, c[0].Trim()), out var id))
                {
                    problems.Add($"{Path.GetFileName(path)}: cannot read '{line}'.");
                    continue;
                }

                var high = c.Length > 3 && c[3].Trim().Equals("high", StringComparison.OrdinalIgnoreCase);
                answers[id] = new Gold2Answer(c[1].Trim(), c[2].Trim(), high);
            }
        }

        return (answers, problems);
    }

    /// <summary>
    ///     Compares the labels of the two labelers and finds the tokens that need the adjudicator.
    /// </summary>
    /// <param name="tokens">The tokens.</param>
    /// <param name="first">The answers of the first labeler.</param>
    /// <param name="second">The answers of the second labeler.</param>
    /// <returns>The tokens that are settled (the labels of the labelers agree) and the tokens that need adjudication (a disagreement, an unsure sense or an invalid answer), and the tokens without an answer of one of the labelers.</returns>
    public static (List<Gold2Outcome> Settled, List<Gold2Token> Pending, List<Gold2Token> Missing) Compare(IReadOnlyList<Gold2Token> tokens, IReadOnlyDictionary<string, Gold2Answer> first, IReadOnlyDictionary<string, Gold2Answer> second)
    {
        ArgumentNullException.ThrowIfNull(tokens);
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(second);
        var settled = new List<Gold2Outcome>();
        var pending = new List<Gold2Token>();
        var missing = new List<Gold2Token>();
        foreach (var token in tokens)
        {
            if (!first.TryGetValue(token.Id, out var a) || !second.TryGetValue(token.Id, out var b))
            {
                missing.Add(token);
                continue;
            }

            if (Resolve(token, a, false, out var la) is not null || Resolve(token, b, false, out var lb) is not null || la is null || lb is null)
            {
                pending.Add(token);
                continue;
            }

            if (AgreementOf(la, lb) == "full")
            {
                settled.Add(new Gold2Outcome(token, la, lb, null, la with { High = la.High && lb.High }, "full"));
            }
            else
            {
                pending.Add(token);
            }
        }

        return (settled, pending, missing);
    }

    /// <summary>
    ///     Tells what two labels agree on.
    /// </summary>
    /// <param name="first">The first label.</param>
    /// <param name="second">The second label.</param>
    /// <returns><c>full</c> (the same analysis and, when it has a choice of senses, the same sense, neither unsure), <c>analysis</c>, <c>pos</c> or <c>none</c>.</returns>
    public static string AgreementOf(Gold2Label first, Gold2Label second)
    {
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(second);
        if (first.Analysis == second.Analysis)
        {
            return first.Sense == second.Sense && first.Sense != "unsure" ? "full" : "analysis";
        }

        return first.Pos == second.Pos ? "pos" : "none";
    }

    /// <summary>
    ///     Merges the answers of the labelers and the adjudicator into the outcomes of every token.
    /// </summary>
    /// <param name="tokens">The tokens.</param>
    /// <param name="first">The answers of the first labeler.</param>
    /// <param name="second">The answers of the second labeler.</param>
    /// <param name="adjudicator">The answers of the adjudicator.</param>
    /// <returns>The outcomes in the order of the tokens, and the tokens that still lack an answer or have an invalid answer of the adjudicator.</returns>
    public static (List<Gold2Outcome> Outcomes, List<string> Problems) Merge(IReadOnlyList<Gold2Token> tokens, IReadOnlyDictionary<string, Gold2Answer> first, IReadOnlyDictionary<string, Gold2Answer> second, IReadOnlyDictionary<string, Gold2Answer> adjudicator)
    {
        ArgumentNullException.ThrowIfNull(tokens);
        ArgumentNullException.ThrowIfNull(adjudicator);
        var (settled, pending, missing) = Compare(tokens, first, second);
        var problems = missing.Select(t => $"{t.Id}: a labeler gave no answer.").ToList();
        var byId = settled.ToDictionary(o => o.Token.Id, StringComparer.Ordinal);
        foreach (var token in pending)
        {
            Gold2Label? la = null, lb = null;
            var firstProblem = Resolve(token, first[token.Id], false, out la);
            var secondProblem = Resolve(token, second[token.Id], false, out lb);
            if (!adjudicator.TryGetValue(token.Id, out var verdict))
            {
                problems.Add($"{token.Id}: the adjudicator gave no answer.");
                continue;
            }

            var problem = Resolve(token, verdict, true, out var final);
            if (problem is not null || final is null)
            {
                problems.Add($"{token.Id}: invalid answer of the adjudicator: {problem}.");
                continue;
            }

            // A labeler whose answer is invalid is shown as an unknown label; the adjudicator decides alone
            la ??= new Gold2Label("Other", firstProblem ?? "?", string.Empty, "-", false, false);
            lb ??= new Gold2Label("Other", secondProblem ?? "?", string.Empty, "-", false, false);
            byId[token.Id] = new Gold2Outcome(token, la, lb, final, final, AgreementOf(la, lb));
        }

        return ([.. tokens.Where(t => byId.ContainsKey(t.Id)).Select(t => byId[t.Id])], problems);
    }

    /// <summary>
    ///     Turns an outcome into a gold row.
    /// </summary>
    /// <param name="outcome">The outcome.</param>
    /// <param name="terms">The terms page of the source of the document.</param>
    /// <returns>The row.</returns>
    public static Gold2Row ToRow(Gold2Outcome outcome, string terms)
    {
        ArgumentNullException.ThrowIfNull(outcome);
        var f = outcome.Final;
        var tense = "-";
        if (f.Pos == nameof(PartOfSpeech.Verb) && f.Features.Length > 0)
        {
            tense = BasicClassifier.IsPast(new WordDetails(f.Lemma, PartOfSpeech.Verb, f.Features)) ? "past" : "nonpast";
        }

        return new Gold2Row(outcome.Token, terms, f.Pos, f.Lemma, f.Features, tense, f.Scored ? f.Sense : "-", outcome.Status, outcome.Agreement);
    }

    /// <summary>
    ///     Counts what the labelers and the adjudicator did on one split.
    /// </summary>
    /// <param name="split">The split.</param>
    /// <param name="outcomes">The outcomes of the split.</param>
    /// <returns>The counts.</returns>
    public static Gold2Agreement Count(string split, IReadOnlyList<Gold2Outcome> outcomes)
    {
        ArgumentNullException.ThrowIfNull(outcomes);
        int analysis = 0, pos = 0, lemma = 0, verbs = 0, tense = 0, choices = 0, both = 0, sense = 0, u1 = 0, u2 = 0, full = 0, adjudicated = 0, none = 0, sided1 = 0, sided2 = 0;
        foreach (var o in outcomes)
        {
            var (a, b) = (o.First, o.Second);
            analysis += a.Analysis == b.Analysis ? 1 : 0;
            pos += a.Pos == b.Pos ? 1 : 0;
            lemma += string.Equals(a.Lemma, b.Lemma, StringComparison.Ordinal) ? 1 : 0;
            if (a.Pos == nameof(PartOfSpeech.Verb) && b.Pos == nameof(PartOfSpeech.Verb) && a.Features.Length > 0 && b.Features.Length > 0)
            {
                verbs++;
                tense += BasicClassifier.IsPast(new WordDetails(a.Lemma, PartOfSpeech.Verb, a.Features)) == BasicClassifier.IsPast(new WordDetails(b.Lemma, PartOfSpeech.Verb, b.Features)) ? 1 : 0;
            }

            if (a.Analysis == b.Analysis && a.Scored)
            {
                choices++;
                u1 += a.Sense == "unsure" ? 1 : 0;
                u2 += b.Sense == "unsure" ? 1 : 0;
                if (a.Sense != "unsure" && b.Sense != "unsure")
                {
                    both++;
                    sense += a.Sense == b.Sense ? 1 : 0;
                }
            }

            full += o.Adjudicated is null ? 1 : 0;
            if (o.Adjudicated is { } j)
            {
                adjudicated++;
                none += j.Sense == "none" ? 1 : 0;
                sided1 += j.Analysis == a.Analysis && j.Sense == a.Sense ? 1 : 0;
                sided2 += j.Analysis == b.Analysis && j.Sense == b.Sense ? 1 : 0;
            }
        }

        return new Gold2Agreement(split, outcomes.Count, analysis, pos, lemma, verbs, tense, choices, both, sense, u1, u2, full, adjudicated, none, sided1, sided2);
    }

    /// <summary>
    ///     Renders the agreement of the labelers and the work of the adjudicator as Markdown.
    /// </summary>
    /// <param name="counts">The counts of each split.</param>
    /// <returns>The text with line feeds.</returns>
    public static string RenderAgreement(IReadOnlyList<Gold2Agreement> counts)
    {
        ArgumentNullException.ThrowIfNull(counts);
        var all = new Gold2Agreement("all", counts.Sum(c => c.Tokens), counts.Sum(c => c.AnalysisAgreed), counts.Sum(c => c.PosAgreed), counts.Sum(c => c.LemmaAgreed), counts.Sum(c => c.Verbs), counts.Sum(c => c.TenseAgreed), counts.Sum(c => c.SenseChoices), counts.Sum(c => c.SenseBoth), counts.Sum(c => c.SenseAgreed), counts.Sum(c => c.UnsureFirst), counts.Sum(c => c.UnsureSecond), counts.Sum(c => c.FullAgreed), counts.Sum(c => c.Adjudicated), counts.Sum(c => c.AdjudicatedNone), counts.Sum(c => c.SidedFirst), counts.Sum(c => c.SidedSecond));
        var text = new StringBuilder("### Agreement of the labelers and the adjudication\n\n");
        text.Append("| Split | Tokens | Same part of speech | Same lemma | Same tense class (verbs) | Same analysis | Same sense (both answered one, same analysis) | Sense unsure (first, second) | Settled by the labelers | Adjudicated | No defensible sense | Adjudicator sided with first, second |\n");
        text.Append("| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |\n");
        foreach (var c in counts.Append(all))
        {
            text.Append("| ").Append(c.Split)
                .Append(" | ").Append(c.Tokens.ToString("N0", CultureInfo.InvariantCulture))
                .Append(" | ").Append(EvaluationResult.Percent(c.PosAgreed, c.Tokens))
                .Append(" | ").Append(EvaluationResult.Percent(c.LemmaAgreed, c.Tokens))
                .Append(" | ").Append(EvaluationResult.Percent(c.TenseAgreed, c.Verbs))
                .Append(" | ").Append(EvaluationResult.Percent(c.AnalysisAgreed, c.Tokens))
                .Append(" | ").Append(EvaluationResult.Percent(c.SenseAgreed, c.SenseBoth))
                .Append(" | ").Append(c.UnsureFirst.ToString("N0", CultureInfo.InvariantCulture)).Append(", ").Append(c.UnsureSecond.ToString("N0", CultureInfo.InvariantCulture))
                .Append(" | ").Append(EvaluationResult.Percent(c.FullAgreed, c.Tokens))
                .Append(" | ").Append(EvaluationResult.Percent(c.Adjudicated, c.Tokens))
                .Append(" | ").Append(c.AdjudicatedNone.ToString("N0", CultureInfo.InvariantCulture))
                .Append(" | ").Append(c.SidedFirst.ToString("N0", CultureInfo.InvariantCulture)).Append(", ").Append(c.SidedSecond.ToString("N0", CultureInfo.InvariantCulture)).Append(" |\n");
        }

        return text.ToString();
    }

    /// <summary>
    ///     Renders the raw labels of every token.
    /// </summary>
    /// <param name="outcomes">The outcomes.</param>
    /// <returns>The text with line feeds.</returns>
    public static string RenderLabels(IEnumerable<Gold2Outcome> outcomes)
    {
        ArgumentNullException.ThrowIfNull(outcomes);
        var text = new StringBuilder();
        text.Append("# corpus = second all-words gold set (v2): the answer of each labeler and of the adjudicator for every token\n");
        text.Append("# license = labels CC0 1.0 by the project owner (model outputs; no human review)\n");
        text.Append("# columns = id first (gpt-6.1-sol) second (claude-sonnet-5.5) adjudicator (gpt-5.6-luna, empty when the labelers agreed); a label is Pos|lemma|features|sense|confidence\n");
        foreach (var o in outcomes)
        {
            text.Append(string.Join('\t', o.Token.Id, o.First.Render(), o.Second.Render(), o.Adjudicated?.Render() ?? string.Empty)).Append('\n');
        }

        return text.ToString();
    }
}

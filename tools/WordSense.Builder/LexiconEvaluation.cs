using System.Globalization;
using System.Text;
using DemaConsulting.WordSense;

namespace WordSense.Builder;

/// <summary>
///     One gold token: the surface form and its universal part of speech.
/// </summary>
/// <param name="Form">The surface form.</param>
/// <param name="Upos">The universal part-of-speech tag.</param>
internal readonly record struct GoldToken(string Form, string Upos);

/// <summary>
///     Hits and total of one measured group.
/// </summary>
internal sealed class Tally
{
    /// <summary>
    ///     Gets or sets the number of correct answers.
    /// </summary>
    public int Correct { get; set; }

    /// <summary>
    ///     Gets or sets the number of answers.
    /// </summary>
    public int Total { get; set; }

    /// <summary>
    ///     Gets the precision, or 1 when nothing was answered.
    /// </summary>
    public double Precision => Total == 0 ? 1.0 : (double)Correct / Total;

    /// <summary>
    ///     Adds one answer.
    /// </summary>
    /// <param name="correct">True when the answer was right.</param>
    public void Add(bool correct)
    {
        Total++;
        if (correct)
        {
            Correct++;
        }
    }
}

/// <summary>
///     The measurements of a dictionary evaluation.
/// </summary>
internal sealed class LexiconEvaluationResult
{
    /// <summary>
    ///     Gets the precision gate.
    /// </summary>
    public const double Gate = 0.95;

    /// <summary>
    ///     Gets the support below which a figure is flagged as thin evidence.
    /// </summary>
    public const int MinSupport = 100;

    /// <summary>
    ///     Gets the direct-answer tally of every enabled class, by part of speech.
    /// </summary>
    public SortedDictionary<PartOfSpeech, Tally> Classes { get; } = [];

    /// <summary>
    ///     Gets the tally of every morphology rule applied to an absent word, enabled or not.
    /// </summary>
    public SortedDictionary<LemmaMorphologyRules, Tally> Rules { get; } = [];

    /// <summary>
    ///     Gets the direct-answer tally the held-back classes would have had.
    /// </summary>
    public SortedDictionary<PartOfSpeech, Tally> Held { get; } = [];

    /// <summary>
    ///     Gets the candidate-set recall by number of candidates.
    /// </summary>
    public SortedDictionary<int, Tally> Recall { get; } = [];

    /// <summary>
    ///     Gets or sets the rules the evaluated table enables.
    /// </summary>
    public LemmaMorphologyRules Enabled { get; set; }

    /// <summary>
    ///     Gets the token census, label to count.
    /// </summary>
    public SortedDictionary<string, int> Census { get; } = new(StringComparer.Ordinal);

    /// <summary>
    ///     Gets or sets the number of sentences.
    /// </summary>
    public int Sentences { get; set; }

    /// <summary>
    ///     Gets or sets the number of sentences with no word that needs the encoder.
    /// </summary>
    public int NoEncoderSentences { get; set; }

    /// <summary>
    ///     Gets the top wrong direct answers, as "word gold->predicted" with the count.
    /// </summary>
    public Dictionary<string, int> Errors { get; } = new(StringComparer.Ordinal);

    /// <summary>
    ///     Lists the problems that fail an enforced evaluation: no sentences, an enabled class or enabled rule below
    ///     the gate, and an enabled class or enabled rule without a single scored token (an empty tally has no
    ///     evidence, so it must not pass as perfect). Thin evidence (fewer than <see cref="MinSupport"/> tokens) is
    ///     only marked in the report.
    /// </summary>
    /// <returns>One message per failure.</returns>
    public List<string> GateFailures()
    {
        var failures = new List<string>();
        if (Sentences == 0)
        {
            failures.Add("no sentences were evaluated, so nothing is proven");
        }

        foreach (var (pos, tally) in Classes)
        {
            if (tally.Total == 0)
            {
                failures.Add($"class {pos} has no scored tokens");
            }
            else if (tally.Precision < Gate)
            {
                failures.Add($"class {pos} precision {tally.Precision:P2} ({tally.Correct}/{tally.Total}) is below {Gate:P0}");
            }
        }

        foreach (var (rule, tally) in Rules)
        {
            if ((Enabled & rule) == 0)
            {
                continue;
            }

            if (tally.Total == 0)
            {
                failures.Add($"rule {rule} has no scored tokens");
            }
            else if (tally.Precision < Gate)
            {
                failures.Add($"rule {rule} precision {tally.Precision:P2} ({tally.Correct}/{tally.Total}) is below {Gate:P0}");
            }
        }

        return failures;
    }

    /// <summary>
    ///     Renders the report as Markdown.
    /// </summary>
    /// <returns>The report, with LF line ends.</returns>
    public string Render()
    {
        var sb = new StringBuilder();
        sb.Append("# Lexicon evaluation\n\n");
        sb.Append(CultureInfo.InvariantCulture, $"Tokens answered by the dictionary are scored against UD English-EWT. The gate is {Gate:P0} precision; ");
        sb.Append(CultureInfo.InvariantCulture, $"a figure over fewer than {MinSupport} tokens is marked thin.\n\n");

        sb.Append("## Direct answers per class\n\n| Class | Correct | Total | Precision | Gate |\n| --- | ---: | ---: | ---: | --- |\n");
        foreach (var (pos, tally) in Classes)
        {
            sb.Append(CultureInfo.InvariantCulture, $"| {pos} | {tally.Correct} | {tally.Total} | {tally.Precision:P2} | {Verdict(tally, true)} |\n");
        }

        sb.Append("\n## Morphology rules\n\n| Rule | Correct | Total | Precision | State | Gate |\n| --- | ---: | ---: | ---: | --- | --- |\n");
        foreach (var (rule, tally) in Rules)
        {
            var state = (Enabled & rule) != 0 ? "enabled" : "disabled";
            sb.Append(CultureInfo.InvariantCulture, $"| {rule} | {tally.Correct} | {tally.Total} | {tally.Precision:P2} | {state} | {Verdict(tally, (Enabled & rule) != 0)} |\n");
        }

        sb.Append("\n## Held-back classes\n\nWords whose only parts of speech are held back are not in the table; this is the precision they would have had.\n\n");
        sb.Append("| Class | Correct | Total | Precision |\n| --- | ---: | ---: | ---: |\n");
        foreach (var (pos, tally) in Held)
        {
            sb.Append(CultureInfo.InvariantCulture, $"| {pos} | {tally.Correct} | {tally.Total} | {tally.Precision:P2} |\n");
        }

        sb.Append("\n## Candidate-set recall of multi-part-of-speech words\n\n| Candidates | Gold in set | Total | Recall |\n| ---: | ---: | ---: | ---: |\n");
        var all = new Tally();
        foreach (var (size, tally) in Recall)
        {
            all.Correct += tally.Correct;
            all.Total += tally.Total;
            sb.Append(CultureInfo.InvariantCulture, $"| {size} | {tally.Correct} | {tally.Total} | {tally.Precision:P2} |\n");
        }

        sb.Append(CultureInfo.InvariantCulture, $"| all | {all.Correct} | {all.Total} | {all.Precision:P2} |\n");

        sb.Append("\n## Token census\n\n| Outcome | Tokens |\n| --- | ---: |\n");
        foreach (var (label, count) in Census)
        {
            sb.Append(CultureInfo.InvariantCulture, $"| {label} | {count} |\n");
        }

        var share = Sentences == 0 ? 0.0 : (double)NoEncoderSentences / Sentences;
        sb.Append(CultureInfo.InvariantCulture, $"\nSentences needing no encoder call: {NoEncoderSentences} of {Sentences} ({share:P1}).\n");

        sb.Append("\n## Most frequent wrong direct answers\n\n| Word (gold, answered) | Count |\n| --- | ---: |\n");
        foreach (var (key, count) in Errors.OrderByDescending(e => e.Value).ThenBy(e => e.Key, StringComparer.Ordinal).Take(20))
        {
            sb.Append(CultureInfo.InvariantCulture, $"| {key} | {count} |\n");
        }

        return sb.ToString();
    }

    private static string Verdict(Tally tally, bool active)
    {
        var thin = tally.Total < MinSupport ? " (thin)" : string.Empty;
        string verdict;
        if (tally.Precision >= Gate)
        {
            verdict = "pass";
        }
        else
        {
            verdict = active ? "FAIL" : "fail";
        }

        return verdict + thin;
    }
}

/// <summary>
///     Evaluates the dictionary of a table against Universal Dependencies English-EWT.
/// </summary>
internal static class LexiconEvaluation
{
    /// <summary>
    ///     Reads the gold sentences of every <c>*.conllu</c> file of a directory (or of one file).
    /// </summary>
    /// <param name="path">The directory or file.</param>
    /// <returns>The sentences in file-name then document order.</returns>
    public static List<List<GoldToken>> ReadGold(string path)
    {
        var files = Directory.Exists(path)
            ? Directory.GetFiles(path, "*.conllu").OrderBy(f => f, StringComparer.Ordinal).ToArray()
            : [path];
        var sentences = new List<List<GoldToken>>();
        foreach (var file in files)
        {
            sentences.AddRange(ParseConllu(File.ReadAllText(file, Encoding.UTF8)));
        }

        return sentences;
    }

    /// <summary>
    ///     Parses CoNLL-U text; multiword-token ranges and empty nodes are skipped.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <returns>The sentences.</returns>
    public static List<List<GoldToken>> ParseConllu(string text)
    {
        var sentences = new List<List<GoldToken>>();
        var current = new List<GoldToken>();
        var skip = 0;
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            if (line.Length == 0)
            {
                if (current.Count > 0)
                {
                    sentences.Add(current);
                    current = [];
                }

                continue;
            }

            if (line[0] == '#')
            {
                continue;
            }

            var cols = line.Split('\t');
            if (cols.Length < 4 || cols[0].Contains('.'))
            {
                continue;
            }

            // A multiword token is one word of running text: its parts are replaced by the surface form, which is not scored
            if (cols[0].Contains('-'))
            {
                var bounds = cols[0].Split('-');
                skip = int.Parse(bounds[1], CultureInfo.InvariantCulture) - int.Parse(bounds[0], CultureInfo.InvariantCulture) + 1;
                current.Add(new GoldToken(cols[1], "_MWT"));
                continue;
            }

            if (skip > 0)
            {
                skip--;
                continue;
            }

            current.Add(new GoldToken(cols[1], cols[3]));
        }

        if (current.Count > 0)
        {
            sentences.Add(current);
        }

        return sentences;
    }

    /// <summary>
    ///     The part-of-speech answers that count as right for a gold tag; null when the tag is not scored.
    /// </summary>
    /// <param name="upos">The universal tag.</param>
    /// <returns>The accepted parts of speech, or null.</returns>
    public static PartOfSpeech[]? Accepted(string upos) => upos switch
    {
        "NOUN" => [PartOfSpeech.Noun],
        "VERB" or "AUX" => [PartOfSpeech.Verb],
        "ADJ" => [PartOfSpeech.Adjective],
        "ADV" => [PartOfSpeech.Adverb],
        "PRON" => [PartOfSpeech.Pronoun],
        "INTJ" => [PartOfSpeech.Interjection],
        "ADP" => [PartOfSpeech.Preposition],
        "CCONJ" => [PartOfSpeech.Conjunction],
        "SCONJ" => [PartOfSpeech.Preposition, PartOfSpeech.Conjunction],
        _ => null,
    };

    /// <summary>
    ///     Evaluates the dictionary.
    /// </summary>
    /// <param name="table">The table under test.</param>
    /// <param name="sentences">The gold sentences.</param>
    /// <param name="held">The words held back by the class gate, with the mask they would have had.</param>
    /// <returns>The measurements.</returns>
    public static LexiconEvaluationResult Evaluate(
        WordSenseTable table,
        IReadOnlyList<List<GoldToken>> sentences,
        IReadOnlyList<(string Word, byte Mask)> held)
    {
        var result = new LexiconEvaluationResult { Enabled = table.Morphology };
        var heldMap = held.ToDictionary(h => h.Word, h => h.Mask, StringComparer.Ordinal);

        // A copy without rules, so that an absent word can be attributed to the single rule that derives it
        var exact = new WordSenseTable(
            table.ModelId, table.ModelSha256, table.Dimension, table.Mean.ToArray(), table.Lemmas.ToList(), table.Forms, LemmaMorphologyRules.None);
        LemmaMorphologyRules[] ruleOrder = [LemmaMorphologyRules.Plural, LemmaMorphologyRules.PastTense, LemmaMorphologyRules.Adverb];
        foreach (var rule in ruleOrder)
        {
            result.Rules[rule] = new Tally();
        }

        // Every class that the table answers directly (a dictionary word with exactly one part of speech) is enabled
        foreach (var lemma in table.Lemmas.Where(l => l.Weights is null))
        {
            var parts = lemma.Senses.Select(s => s.Pos).Distinct().ToArray();
            if (parts.Length == 1)
            {
                GetTally(result.Classes, parts[0]);
            }
        }

        foreach (var sentence in sentences)
        {
            var words = sentence.Select(t => t.Form).ToArray();
            result.Sentences++;
            var needsEncoder = false;
            for (var i = 0; i < sentence.Count; i++)
            {
                var token = sentence[i];
                if (!token.Form.All(char.IsLetter))
                {
                    continue;
                }

                // What the real classifier would do with the word
                if (table.TryResolve(token.Form, true, out var real) && (real.Entry.Weights is not null || WordSenseClassifier.CaseAllowed(words, i)))
                {
                    needsEncoder |= real.Entry.Weights is not null;
                }

                Score(result, exact, ruleOrder, heldMap, words, i, token);
            }

            if (!needsEncoder)
            {
                result.NoEncoderSentences++;
            }
        }

        return result;
    }

    private static void Score(
        LexiconEvaluationResult result,
        WordSenseTable exact,
        LemmaMorphologyRules[] ruleOrder,
        Dictionary<string, byte> heldMap,
        string[] words,
        int index,
        GoldToken token)
    {
        var accepted = Accepted(token.Upos);
        var lower = token.Form.ToLowerInvariant().Replace('\u2019', '\'');
        LemmaResolution resolution;
        LemmaMorphologyRules viaRule = LemmaMorphologyRules.None;
        if (exact.TryResolve(token.Form, true, out resolution))
        {
            // Exact: possessives of trained lemmas also land here
        }
        else
        {
            var found = false;
            resolution = default;
            foreach (var rule in ruleOrder)
            {
                if (LemmaMorphology.TryResolve(lower, rule, exact, out resolution))
                {
                    viaRule = rule;
                    found = true;
                    break;
                }
            }

            if (!found)
            {
                if (heldMap.TryGetValue(lower, out var mask) && accepted is not null && CaseOk(words, index))
                {
                    ScoreHeld(result, mask, accepted);
                }

                Count(result, CaseOk(words, index) || !char.IsUpper(token.Form[0]) ? "unknown" : "unknown (capitalized mid-sentence)");
                return;
            }
        }

        if (resolution.Entry.Weights is not null)
        {
            Count(result, "trained lemma (context)");
            return;
        }

        if (!WordSenseClassifier.CaseAllowed(words, index))
        {
            Count(result, "skipped by the case policy");
            return;
        }

        var senses = resolution.IsNarrowed
            ? [resolution.Entry.Senses[resolution.SenseIndex].Pos]
            : resolution.Entry.Senses.Select(s => s.Pos).Distinct().ToArray();
        if (senses.Length == 1)
        {
            Count(result, viaRule == LemmaMorphologyRules.None ? "dictionary, one part of speech" : "morphology, one part of speech");
            if (accepted is null)
            {
                Count(result, "  (answered, gold not scored)");
                return;
            }

            var ok = accepted.Contains(senses[0]);
            if (viaRule != LemmaMorphologyRules.None)
            {
                result.Rules[viaRule].Add(ok);
            }
            else
            {
                GetTally(result.Classes, senses[0]).Add(ok);
            }

            if (!ok)
            {
                var key = $"{lower} ({token.Upos}, {senses[0]})";
                result.Errors[key] = result.Errors.GetValueOrDefault(key) + 1;
            }

            return;
        }

        Count(result, "dictionary, several parts of speech");
        if (accepted is not null)
        {
            GetTally(result.Recall, senses.Length).Add(accepted.Any(a => senses.Contains(a)));
        }
    }

    private static void ScoreHeld(LexiconEvaluationResult result, byte mask, PartOfSpeech[] accepted)
    {
        var letters = LexiconSource.ToLetters(mask);
        if (letters.Length != 1)
        {
            return;
        }

        var pos = (PartOfSpeech)LexiconSource.Letters.IndexOf(letters[0], StringComparison.Ordinal);
        GetTally(result.Held, pos).Add(accepted.Contains(pos));
    }

    private static bool CaseOk(string[] words, int index) => WordSenseClassifier.CaseAllowed(words, index);

    private static void Count(LexiconEvaluationResult result, string label) => result.Census[label] = result.Census.GetValueOrDefault(label) + 1;

    private static Tally GetTally<TKey>(SortedDictionary<TKey, Tally> map, TKey key)
        where TKey : notnull
    {
        if (!map.TryGetValue(key, out var tally))
        {
            tally = new Tally();
            map[key] = tally;
        }

        return tally;
    }
}

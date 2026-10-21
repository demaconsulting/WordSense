using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using DemaConsulting.WordSense;

namespace WordSense.Builder;

/// <summary>
///     The part-of-speech accuracy of the basic classifier on one set of gold sentences.
/// </summary>
/// <param name="Name">The name of the set.</param>
/// <param name="Sentences">The number of sentences.</param>
/// <param name="Scored">The number of scored tokens: tokens the classifier answers (the lexicon knows the word) whose gold tag is a word class.</param>
/// <param name="Correct">The number of scored tokens whose selected analysis has the gold part of speech.</param>
/// <param name="Ambiguous">The number of scored tokens whose word has at least two parts of speech in the lexicon.</param>
/// <param name="AmbiguousCorrect">The number of right tokens among the ambiguous ones.</param>
/// <param name="Unanswered">The number of tokens the classifier does not answer (an unknown word, a number or punctuation) whose gold tag is a word class.</param>
/// <param name="Excluded">The number of tokens whose gold tag is punctuation or other (numbers, symbols, foreign words, particles other than <c>to</c>).</param>
internal sealed record TaggerScore(string Name, int Sentences, int Scored, int Correct, int Ambiguous, int AmbiguousCorrect, int Unanswered, int Excluded);

/// <summary>
///     Measures the part-of-speech accuracy of the basic classifier (the statistical tagger and the cue model, the path
///     that an application uses) on gold sentences.
/// </summary>
/// <remarks>
///     <para>
///     The predicted part of speech is the part of speech of the selected analysis. A token is scored when the classifier
///     answers it and its gold tag is a word class; the infinitive marker <c>to</c> counts as a preposition because the
///     lexicon has no particle class. The accuracy of the ambiguous words (at least two parts of speech in the lexicon)
///     is the one that says something about the tagger, because an unambiguous word is right by construction.
///     </para>
///     <para>
///     The headline set is the <c>test</c> split of the representative part of the open corpus. The test split of the
///     targeted part is a challenge set that is reported on its own line, never merged into a headline figure. An external
///     gold file (for example the convention-mapped development and test files of a treebank, which are downloaded for
///     evaluation only) has one token per line, <c>sentence-id</c>, <c>form</c> and the tag letter, with the sentence
///     ids grouping the tokens.
///     </para>
/// </remarks>
internal static class TaggerEvaluator
{
    /// <summary>
    ///     Evaluates the classifier on sentences.
    /// </summary>
    /// <param name="name">The name of the set.</param>
    /// <param name="tables">The statistics.</param>
    /// <param name="sentences">The gold sentences.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The score.</returns>
    public static Task<TaggerScore> EvaluateAsync(string name, BasicTables tables, IReadOnlyList<TaggedSentence> sentences, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(tables);
        ArgumentNullException.ThrowIfNull(sentences);
        var lexicon = (Func<string, ImmutableArray<WordDetails>>)(key => WordLexicon.TryLookup(key, out var found) ? found : []);
        var classifier = new BasicClassifier(tables, lexicon, (lemma, pos) => WordLexicon.Current.SenseCountOf(lemma, pos), WordLexicon.SensesOf);
        return ScoreAsync(name, sentences, classifier.ClassifyAsync, cancellationToken);
    }

    /// <summary>
    ///     Evaluates a model through its public interface, the way an application uses it.
    /// </summary>
    /// <param name="name">The name of the set.</param>
    /// <param name="model">The model.</param>
    /// <param name="sentences">The gold sentences.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The score.</returns>
    public static Task<TaggerScore> EvaluateAsync(string name, IWordSenseModel model, IReadOnlyList<TaggedSentence> sentences, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(sentences);
        return ScoreAsync(name, sentences, model.ClassifyAsync, cancellationToken);
    }

    private static async Task<TaggerScore> ScoreAsync(string name, IReadOnlyList<TaggedSentence> sentences, Func<IEnumerable<string>, CancellationToken, Task<ClassifyResult[]>> classify, CancellationToken cancellationToken)
    {
        int scored = 0, correct = 0, ambiguous = 0, ambiguousCorrect = 0, unanswered = 0, excluded = 0;
        foreach (var sentence in sentences)
        {
            var results = await classify(sentence.Words, cancellationToken).ConfigureAwait(false);
            for (var i = 0; i < results.Length; i++)
            {
                var gold = sentence.Tags[i];
                if (gold is PosTag.Punctuation or PosTag.Other)
                {
                    excluded++;
                    continue;
                }

                if (results[i].Details is not { } details)
                {
                    unanswered++;
                    continue;
                }

                gold = gold == PosTag.InfinitiveTo ? PosTag.Preposition : gold;
                var right = PosTag.Of(details.PartOfSpeech) == gold;
                scored++;
                correct += right ? 1 : 0;
                var partsOfSpeech = WordLexicon.Lookup(BasicClassifier.Normalize(sentence.Words[i])).Select(a => a.PartOfSpeech).Distinct().Count();
                if (partsOfSpeech >= 2)
                {
                    ambiguous++;
                    ambiguousCorrect += right ? 1 : 0;
                }
            }
        }

        return new TaggerScore(name, sentences.Count, scored, correct, ambiguous, ambiguousCorrect, unanswered, excluded);
    }

    /// <summary>
    ///     Reads a gold file: one token per line with the sentence id, the form and the tag letter (see <see cref="PosTag.Letters"/>),
    ///     tab-separated; empty lines and lines that start with <c>#</c> are skipped.
    /// </summary>
    /// <param name="path">The file.</param>
    /// <returns>The sentences in file order.</returns>
    /// <exception cref="InvalidDataException">Thrown when a line is malformed.</exception>
    public static List<TaggedSentence> ReadGold(string path)
    {
        var sentences = new List<TaggedSentence>();
        var words = new List<string>();
        var tags = new List<int>();
        string? current = null;
        var number = 0;
        foreach (var raw in File.ReadLines(path, new UTF8Encoding(false)))
        {
            number++;
            var line = raw.TrimEnd('\r');
            if (line.Length == 0 || line[0] == '#')
            {
                continue;
            }

            var columns = line.Split('\t');
            var tag = columns.Length == 3 && columns[2].Length == 1 ? PosTag.Letters.IndexOf(columns[2][0], StringComparison.Ordinal) : -1;
            if (tag is < 0 or >= PosTag.Count)
            {
                throw new InvalidDataException($"{Path.GetFileName(path)}:{number.ToString(CultureInfo.InvariantCulture)}: expected 'sentence-id<TAB>form<TAB>tag letter'.");
            }

            if (current is not null && !string.Equals(current, columns[0], StringComparison.Ordinal))
            {
                sentences.Add(new TaggedSentence([.. words], [.. tags]));
                words.Clear();
                tags.Clear();
            }

            current = columns[0];
            words.Add(columns[1]);
            tags.Add(tag);
        }

        if (words.Count > 0)
        {
            sentences.Add(new TaggedSentence([.. words], [.. tags]));
        }

        return sentences;
    }

    /// <summary>
    ///     Renders the scores as a Markdown table.
    /// </summary>
    /// <param name="scores">The scores, one per set.</param>
    /// <returns>The table with a trailing line feed.</returns>
    public static string Render(IEnumerable<TaggerScore> scores)
    {
        ArgumentNullException.ThrowIfNull(scores);
        var text = new StringBuilder();
        text.Append("| Set | Sentences | Scored tokens | Accuracy | Ambiguous tokens | Ambiguous accuracy | Not answered | Excluded |\n");
        text.Append("| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |\n");
        foreach (var s in scores)
        {
            text.Append("| ").Append(s.Name)
                .Append(" | ").Append(s.Sentences.ToString("N0", CultureInfo.InvariantCulture))
                .Append(" | ").Append(s.Scored.ToString("N0", CultureInfo.InvariantCulture))
                .Append(" | ").Append(Percent(s.Correct, s.Scored))
                .Append(" | ").Append(s.Ambiguous.ToString("N0", CultureInfo.InvariantCulture))
                .Append(" | ").Append(Percent(s.AmbiguousCorrect, s.Ambiguous))
                .Append(" | ").Append(s.Unanswered.ToString("N0", CultureInfo.InvariantCulture))
                .Append(" | ").Append(s.Excluded.ToString("N0", CultureInfo.InvariantCulture)).Append(" |\n");
        }

        return text.ToString();
    }

    private static string Percent(int part, int whole) =>
        whole == 0 ? "n/a" : (100.0 * part / whole).ToString("0.00", CultureInfo.InvariantCulture) + "%";
}

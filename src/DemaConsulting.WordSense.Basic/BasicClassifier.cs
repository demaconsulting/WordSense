namespace DemaConsulting.WordSense.Basic;

/// <summary>
///     The classifier of the basic model: a lexicon lookup followed by the hand-written <see cref="Rules"/>.
/// </summary>
/// <remarks>
///     A word with no lexicon entry is <c>(word, null, 0)</c>; a word with one entry is <c>(word, entry, 1)</c>. For a
///     word with several entries the neighboring words are looked up and the first rule that fires picks a part of
///     speech; the result is the single entry with that part of speech and the fixed confidence of the rule. When no
///     rule fires, or several entries share the selected part of speech, the result is <c>(word, null, 0)</c>.
/// </remarks>
internal sealed class BasicClassifier : IWordSenseClassifier
{
    /// <inheritdoc />
    public Task<WordSense[]> ClassifyAsync(IEnumerable<string> words, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(words);
        var list = words.ToList();
        if (list.Any(w => w is null))
        {
            throw new ArgumentException("A word must not be null.", nameof(words));
        }

        cancellationToken.ThrowIfCancellationRequested();
        var keys = list.Select(Normalize).ToList();
        var results = new WordSense[list.Count];
        for (var i = 0; i < list.Count; i++)
        {
            results[i] = Classify(list[i], keys, i);
        }

        return Task.FromResult(results);
    }

    /// <summary>
    ///     Classifies the word at one position.
    /// </summary>
    /// <param name="word">The word as written.</param>
    /// <param name="keys">The normalized words of the whole text.</param>
    /// <param name="index">The position of the word.</param>
    /// <returns>The result.</returns>
    internal static WordSense Classify(string word, IReadOnlyList<string> keys, int index)
    {
        if (keys[index].Length == 0 || !WordLexicon.Words.TryGetValue(keys[index], out var senses))
        {
            return new WordSense(word, null, 0);
        }

        if (senses.Length == 1)
        {
            return new WordSense(word, senses[0], 1);
        }

        var candidates = senses.Select(s => s.PartOfSpeech).ToHashSet();
        var previous = index > 0 && keys[index - 1].Length > 0 ? keys[index - 1] : null;
        var next = index + 1 < keys.Count && keys[index + 1].Length > 0 ? keys[index + 1] : null;
        if (Rules.Evaluate(previous, next, candidates) is not { } match)
        {
            return new WordSense(word, null, 0);
        }

        var chosen = senses.Where(s => s.PartOfSpeech == match.PartOfSpeech).ToList();
        return chosen.Count == 1 ? new WordSense(word, chosen[0], match.Confidence) : new WordSense(word, null, 0);
    }

    /// <summary>
    ///     Lower-cases a word and straightens the typographic apostrophe; a token without a letter becomes empty.
    /// </summary>
    /// <param name="word">The word as written.</param>
    /// <returns>The lexicon key, or an empty string for punctuation.</returns>
    internal static string Normalize(string word)
    {
        var key = word.ToLowerInvariant().Replace('\u2019', '\'');
        return key.Any(char.IsLetter) ? key : string.Empty;
    }
}

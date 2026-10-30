using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;

namespace DemaConsulting.WordSense;

/// <summary>
///     The classification of one word: the result that <see cref="IWordSenseModel.ClassifyAsync"/> returns for each word
///     of its input.
/// </summary>
/// <remarks>
///     <para>
///     The result has two parts. <see cref="Analyses"/> is the full answer: the probability of every analysis (lemma,
///     part of speech and features) that the word can have in its context and, inside each analysis, the probability of
///     each of its fine senses when the model scored them. Everything else is a summary of it: <see cref="Details"/> is the
///     selected analysis (the best variant of the most probable slot, see below), and <see cref="Level"/>, <see cref="Confidence"/>,
///     <see cref="GroupId"/> and <see cref="SenseId"/> say how precisely the model is willing to name the meaning. A
///     consumer that wants its own decision reads <see cref="Analyses"/>; a consumer that wants the model's decision
///     reads the summary.
///     </para>
///     <para>
///     Level. <see cref="SenseLevel.None"/> is only for a word that is not in the lexicon (<see cref="IsKnown"/> is
///     false, <see cref="Analyses"/> is empty). A word that is in the lexicon is always at least at
///     <see cref="SenseLevel.PartOfSpeech"/>, however uncertain the model is: the uncertainty is in
///     <see cref="Confidence"/>. At <see cref="SenseLevel.Sense"/> the result names a fine sense of the selected analysis
///     (<see cref="SenseId"/>, and <see cref="GroupId"/> when the sense belongs to a group); at
///     <see cref="SenseLevel.Group"/> it names a group of closely related senses of the selected analysis that the model
///     could not tell apart (<see cref="GroupId"/> only). Groups are optional and exist only where they were curated, so
///     a sense answer may have no group; a model that is unsure between two senses answers with their shared group when
///     they have one and otherwise with the part of speech. Test the level, not whether an identifier is null.
///     </para>
///     <para>
///     Confidence is a number from 0 to 1 at the level of the answer. It is built on the probability of the
///     <i>slot</i> of the selected analysis: the sum of the probabilities of the analyses that have the lemma and the part
///     of speech of the selected analysis (analyses that differ only in features, such as <c>cut</c> as an infinitive and
///     as a past tense, or <c>deer</c> as a singular and a plural, share one slot, because the senses belong to a lemma
///     and a part of speech and not to a feature variant). At the part-of-speech level it is the probability of that slot.
///     At the group level it is the probability of the slot times the sum of the probabilities of the member senses of
///     the group within it. At the sense level it is the probability of the slot times the probability of the answered
///     sense within it. The split of a slot among its feature variants never lowers the confidence. The probability of a
///     slot is model-specific: the Basic model estimates it from the tags and the cues of the sentence; the BertBaseCased
///     model gives a word of two or more parts of speech the probability of its part of speech from a part-of-speech
///     classifier that reads the sentence, times the share of the slot among the slots of that part of speech (combined
///     with the Basic model); the MiniLM model takes the mass of the senses of the slot, combined with the Basic model.
///     It is 0 for an unknown
///     word. It is 1 when nothing is left to decide at the level of the answer: for a word with a single lemma and part of
///     speech (a single slot) and a single sense, at the <see cref="SenseLevel.Sense"/> level (the sense is certain), and
///     also, at the <see cref="SenseLevel.PartOfSpeech"/> level, for a word with a single lemma and part of speech that has
///     several senses which the model does not decide (the part of speech is certain, the sense is not: the Basic model
///     answers such a word this way, and so does an encoder model when it answers it at that level). So a confidence of 1
///     does not mean that a sense is known; test the <see cref="Level"/>. The probabilities are comparable
///     only within one result: do not compare them across models or across words as if they were calibrated, and tune
///     any threshold per model and level on your own text.
///     </para>
///     <para>
///     Ordering and determinism. The analyses are sorted by the probability of their slot, the largest first, and inside
///     a slot by their own probability, the largest first; ties keep the order of the analyses of
///     <see cref="WordLexicon.Lookup"/>. The first analysis is therefore the best variant of the most probable slot, and it
///     is the selected one. The senses of an analysis are sorted by probability and, on a tie, in the inventory order of
///     the lemma (<see cref="WordLexicon.SensesOf"/>). Nothing is truncated: every analysis of the word is listed, and when a model
///     ranked the senses of an analysis, every sense of it. A model that has no evidence for the senses of a lemma and part of
///     speech (the Basic model, and so the encoder models for every lemma they have no weights for) ranks them by the usage
///     prior, the WordNet 3.0 tag counts of the senses with add-k smoothing: the ranking is a statement about the lemma and
///     not about the sentence, and it changes neither the level, nor the confidence, nor <see cref="SenseId"/> and <see cref="GroupId"/>
///     of the result. All arrays are immutable and may be shared between results
///     (for an unknown word, a word with one analysis and one sense), so never rely on reference identity. A
///     <see langword="default"/> result is valid: its arrays are empty and its word is the empty string.
///     </para>
/// </remarks>
/// <example>
///     <code>
///     static async Task ShowAsync(IWordSenseModel model)
///     {
///         ClassifyResult[] results = await model.ClassifyAsync(["They", "will", "record", "the", "record", "."]);
///         foreach (ClassifyResult result in results)
///         {
///             if (!result.IsKnown)
///             {
///                 Console.WriteLine($"{result.Word}: unknown");
///                 continue;
///             }
///
///             Console.WriteLine($"{result.Word}: {result.Details!.Lemma} {result.Details.PartOfSpeech} ({result.Level}, {result.Confidence:P0})");
///
///             // The full answer: every analysis with its probability, and the senses inside an analysis
///             foreach (AnalysisCandidate analysis in result.Analyses)
///             {
///                 Console.WriteLine($"  {analysis.Details.PartOfSpeech} {analysis.Probability:P0}: {analysis.Senses.Length} scored senses");
///             }
///         }
///     }
///     </code>
/// </example>
public readonly record struct ClassifyResult
{
    private static readonly ImmutableArray<AnalysisCandidate> NoAnalyses = ImmutableArray<AnalysisCandidate>.Empty;

    private readonly string? _word;
    private readonly ImmutableArray<AnalysisCandidate> _analyses;

    /// <summary>
    ///     Initializes a new instance of the <see cref="ClassifyResult"/> struct.
    /// </summary>
    /// <param name="word">The word as given by the caller.</param>
    /// <param name="isKnown">Whether the word is in the lexicon.</param>
    /// <param name="analyses">The analyses with their probabilities, the most probable first; a default array counts as empty.</param>
    /// <param name="details">The selected analysis: the best analysis of the most probable slot, or <see langword="null"/> for an unknown word.</param>
    /// <param name="level">The level at which the word was answered.</param>
    /// <param name="confidence">The confidence at that level, from 0 to 1.</param>
    /// <param name="groupId">The answered group, or the group of the answered sense; otherwise <see langword="null"/>.</param>
    /// <param name="senseId">The answered sense at the sense level; otherwise <see langword="null"/>.</param>
    [SuppressMessage("Major Code Smell", "S107:Methods should not have too many parameters", Justification = "Public constructor of an immutable result record whose components are all supplied by the classifier; the many components are the public contract.")]
    public ClassifyResult(
        string word,
        bool isKnown,
        ImmutableArray<AnalysisCandidate> analyses,
        WordDetails? details,
        SenseLevel level,
        double confidence,
        string? groupId,
        string? senseId)
    {
        _word = word;
        IsKnown = isKnown;
        _analyses = analyses;
        Details = details;
        Level = level;
        Confidence = confidence;
        GroupId = groupId;
        SenseId = senseId;
    }

    /// <summary>
    ///     Gets the word as given by the caller (not lower-cased).
    /// </summary>
    public string Word => _word ?? string.Empty;

    /// <summary>
    ///     Gets a value indicating whether the word is in the lexicon: false only for a word without any analysis.
    /// </summary>
    public bool IsKnown { get; }

    /// <summary>
    ///     Gets the analyses of the word with their probabilities, the most probable slot (lemma and part of speech)
    ///     first and in a slot the most probable analysis first, summing to one; empty for an unknown word.
    /// </summary>
    public ImmutableArray<AnalysisCandidate> Analyses => _analyses.IsDefault ? NoAnalyses : _analyses;

    /// <summary>
    ///     Gets the selected analysis: the best analysis of the most probable slot (lemma and part of speech), stored with
    ///     the result; <see langword="null"/> for an unknown word.
    /// </summary>
    public WordDetails? Details { get; }

    /// <summary>
    ///     Gets the level at which the word was answered.
    /// </summary>
    public SenseLevel Level { get; }

    /// <summary>
    ///     Gets the confidence in the answer at its <see cref="Level"/>, from 0 to 1 (see the remarks of the type).
    /// </summary>
    public double Confidence { get; }

    /// <summary>
    ///     Gets the identifier of the answered group (at the group level) or of the group of the answered sense (at the
    ///     sense level, when the sense is in a group); otherwise <see langword="null"/>.
    /// </summary>
    public string? GroupId { get; }

    /// <summary>
    ///     Gets the identifier of the answered sense at the sense level; otherwise <see langword="null"/>.
    /// </summary>
    public string? SenseId { get; }

    /// <summary>
    ///     Gets the stable key of the answer: one string that names the analysis of the word and, where known, its
    ///     sense; <see langword="null"/> for an unknown word and for a result without a selected analysis.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     The format is <c>{spelling}|{lemma}/{Part}[/{Features}][#{label}]</c> (see <see cref="WordKey"/>): the
    ///     lower-cased spelling with the apostrophe straightened, the lemma of <see cref="Details"/> with its case, the
    ///     name of its part of speech, its features (left out when empty) and the label of the sense. The label is the part
    ///     after <c>#</c> of the answered <see cref="SenseId"/> when the result is at the sense level; otherwise it is
    ///     that of the most probable ranked sense of the selected analysis, when the model ranked senses for it (this
    ///     covers the group level and the part-of-speech level of a trained lemma, and the part-of-speech level of every
    ///     polysemous lemma through the usage prior; on equal probabilities the first listed sense, which is the first in the
    ///     inventory order); otherwise it is left out. A group id
    ///     is never used, because group ids are not frozen. Examples: <c>bass|bass/Noun/Number=Sing#sea-bass</c>,
    ///     <c>polish|Polish/Noun/Number=Sing</c>, <c>read|read/Verb/Tense=Past+VerbForm=Part</c>.
    ///     </para>
    ///     <para>
    ///     The key is computed from the Word, IsKnown, Details, Level, SenseId and Analyses of the result alone, so it
    ///     is the same for every model, the models of third parties included. Every key is a member of
    ///     <see cref="WordLexicon.Keys"/>.
    ///     </para>
    /// </remarks>
    public string? Key => WordKey.KeyOf(this);

    /// <summary>
    ///     Creates the result of a word that is not in the lexicon.
    /// </summary>
    /// <param name="word">The word as given by the caller.</param>
    /// <returns>A result with no analysis, the level <see cref="SenseLevel.None"/> and confidence 0.</returns>
    public static ClassifyResult Unknown(string word) => new(word, false, NoAnalyses, null, SenseLevel.None, 0, null, null);

    /// <summary>
    ///     Determines whether another result has the same values, the arrays compared item by item.
    /// </summary>
    /// <param name="other">The other result.</param>
    /// <returns><see langword="true"/> when every value is equal.</returns>
    public bool Equals(ClassifyResult other) =>
        string.Equals(Word, other.Word, StringComparison.Ordinal)
        && IsKnown == other.IsKnown
        && Equals(Details, other.Details)
        && Level == other.Level
        && Confidence.CompareTo(other.Confidence) == 0
        && string.Equals(GroupId, other.GroupId, StringComparison.Ordinal)
        && string.Equals(SenseId, other.SenseId, StringComparison.Ordinal)
        && Analyses.AsSpan().SequenceEqual(other.Analyses.AsSpan());

    /// <summary>
    ///     Gets a hash code that agrees with <see cref="Equals(ClassifyResult)"/>.
    /// </summary>
    /// <returns>The hash code.</returns>
    public override int GetHashCode()
    {
        var hash = default(HashCode);
        hash.Add(Word, StringComparer.Ordinal);
        hash.Add(IsKnown);
        hash.Add(Details);
        hash.Add(Level);
        hash.Add(Confidence);
        hash.Add(GroupId, StringComparer.Ordinal);
        hash.Add(SenseId, StringComparer.Ordinal);
        foreach (var analysis in Analyses)
        {
            hash.Add(analysis);
        }

        return hash.ToHashCode();
    }
}

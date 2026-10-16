namespace DemaConsulting.WordSense;

/// <summary>
///     The result of classifying one word: the answer, if any, and the confidence figures behind it.
/// </summary>
/// <remarks>
///     <para>
///     Every classification returns a full analysis, so there is no separate "detail" mode. Every word is looked up
///     as an entry with one or more senses (part of speech, with a pronunciation for a heteronym) and optionally
///     trained weights. <see cref="Basis"/> says which of these cases applies:
///     </para>
///     <list type="bullet">
///     <item><description>
///     <see cref="AnswerBasis.None"/>, unknown word (not in the table, rejected by the case policy, or no tokens to
///     read): <see cref="Details"/> is null, <see cref="Abstained"/> is false, <see cref="RunnerUp"/> is null, both
///     margins are 0 and <see cref="Candidates"/> is empty.
///     </description></item>
///     <item><description>
///     <see cref="AnswerBasis.Dictionary"/>, one sense (a dictionary answer): <see cref="Details"/> is that sense (a
///     dictionary answer's <see cref="WordDetails.Pronunciation"/> is always null), <see cref="Abstained"/> is false,
///     <see cref="RunnerUp"/> is null, both margins are 0 and <see cref="Candidates"/> holds the one sense.
///     </description></item>
///     <item><description>
///     <see cref="AnswerBasis.Dictionary"/>, several senses and no trained classifier: <see cref="Details"/> is null,
///     <see cref="Abstained"/> is true, the margins are 0, <see cref="RunnerUp"/> is null and
///     <see cref="Candidates"/> lists the possible answers.
///     </description></item>
///     <item><description>
///     <see cref="AnswerBasis.Context"/>, answered (a context answer): <see cref="Details"/> is the best answer and
///     <see cref="Abstained"/> is false.
///     </description></item>
///     <item><description>
///     <see cref="AnswerBasis.Context"/>, abstained: the evidence is weak, so <see cref="Details"/> is null and
///     <see cref="Abstained"/> is true; <see cref="Margin"/> and <see cref="RunnerUp"/> are still populated, so a
///     caller may apply its own threshold.
///     </description></item>
///     </list>
///     <para>
///     <see cref="Abstained"/> therefore means "known word, no single answer". For a context answer or abstention
///     <see cref="Candidates"/> lists every sense of the lemma (the same list as
///     <see cref="IWordSenseClassifier.GetCandidates"/>) and <see cref="Details"/> and <see cref="RunnerUp"/> are
///     members of it.
///     </para>
///     <para>
///     <see cref="Candidates"/> is never null for a value the classifier returns, but <c>default(WordAnalysis)</c>
///     has a null <see cref="Candidates"/> and a null <see cref="Word"/>.
///     </para>
///     <para>
///     The margin is a score difference, not a probability, and each lemma has its own scale. An answer is given
///     when the margin is at least the lemma's abstain margin multiplied by
///     <see cref="WordSenseClassifierOptions.AbstainMarginScale"/>. The scale is not calibrated: margins of answered
///     words typically fall between about 1 and 10 and larger means more confident, but there is no fixed value that
///     means "certain". Rely on <see cref="Abstained"/>, and tune
///     <see cref="WordSenseClassifierOptions.AbstainMarginScale"/> for a stricter or looser rule.
///     </para>
/// </remarks>
/// <example>
///     <code>
///     using var classifier = await WordSenseClassifier.CreateAsync();
///     WordAnalysis[] words = await classifier.ClassifyAsync(["They", "will", "record", "it"]);
///
///     WordAnalysis record = words[2];
///     if (record.Details is { } details)
///     {
///         Console.WriteLine($"{record.Word} is the {details.PartOfSpeech} {details.Lemma} ({record.Basis}, margin {record.Margin:F2})");
///     }
///     else if (record.Abstained)
///     {
///         var possible = string.Join(", ", record.Candidates.Select(c => c.PartOfSpeech));
///         Console.WriteLine($"Not sure; it could be: {possible}.");
///     }
///     </code>
/// </example>
/// <param name="Word">The input word exactly as given.</param>
/// <param name="Details">The best answer, or null when the word is unknown or has no single answer.</param>
/// <param name="Margin">
///     For <see cref="AnswerBasis.Context"/>: the score of the best candidate minus the score of the runner-up, also
///     when the classifier abstained. 0 for a dictionary answer and for an unknown word.
/// </param>
/// <param name="Abstained">
///     True when the word is known but has no single answer: its context margin is below the lemma's (scaled) abstain
///     threshold, or it is a dictionary word with several senses. False for an answered or unknown word.
/// </param>
/// <param name="RunnerUp">
///     The second-best candidate of a context classification; null for a dictionary answer and for an unknown word.
/// </param>
/// <param name="RunnerUpMargin">
///     The score of the runner-up minus the score of the third-best candidate; 0 when there are only two candidates,
///     for a dictionary answer and for an unknown word.
/// </param>
/// <param name="Basis">Where the answer comes from: context, the dictionary, or none (unknown word).</param>
/// <param name="Candidates">
///     Every possible answer for the word's lemma, never null: the senses of the entry, empty for an unknown word. For
///     a context classification they include <paramref name="Details"/> and <paramref name="RunnerUp"/>. A
///     <c>default</c> value of this struct has a null list.
/// </param>
public readonly record struct WordAnalysis(
    string Word,
    WordDetails? Details,
    double Margin,
    bool Abstained,
    WordDetails? RunnerUp,
    double RunnerUpMargin,
    AnswerBasis Basis,
    IReadOnlyList<WordDetails> Candidates);

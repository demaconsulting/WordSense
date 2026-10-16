namespace DemaConsulting.WordSense;

/// <summary>
///     Options for <see cref="WordSenseClassifier"/>.
/// </summary>
/// <remarks>
///     Every property is optional. <see cref="AbstainMarginScale"/> trades coverage against precision: a larger scale
///     makes the classifier abstain more often (<c>Details</c> is null and <c>Abstained</c> is true).
///     Settings that belong to one encoder model, such as its model directory, download settings and sequence
///     length, are on that model package's own options class.
/// </remarks>
/// <example>
///     <code>
///     var options = new WordSenseClassifierOptions
///     {
///         AbstainMarginScale = 0.5, // answer more often; 0 never abstains, above 1 abstains more
///     };
///
///     using var classifier = WordSenseClassifier.Create(options);
///     </code>
/// </example>
public sealed class WordSenseClassifierOptions
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="WordSenseClassifierOptions"/> class with every option at its default.
    /// </summary>
    public WordSenseClassifierOptions()
    {
    }

    /// <summary>
    ///     Gets the factor applied to every lemma's abstain margin; the default is 1.0. Values above 1 make the
    ///     classifier abstain more often; 0 never abstains.
    /// </summary>
    /// <remarks>
    ///     Must not be negative or NaN; the classifier constructor rejects such values. The
    ///     effective threshold of a lemma is <c>SenseWeights.AbstainMargin * AbstainMarginScale</c>; a decision whose
    ///     margin (see <see cref="WordAnalysis.Margin"/>) is below it is withheld. For example a scale of 0.5 halves
    ///     every lemma's threshold, so more answers are given and more of them may be wrong. It has no effect without a model.
    /// </remarks>
    public double AbstainMarginScale { get; init; } = 1.0;

    /// <summary>
    ///     Gets a value indicating whether words that only the dictionary knows are answered; the default is true.
    /// </summary>
    /// <remarks>
    ///     When true, a word without a trained classifier is answered from the dictionary (<see cref="AnswerBasis.Dictionary"/>):
    ///     with its one part of speech, or abstained with its candidates when it has several. When false only lemmas with a
    ///     trained classifier are answered and every other word is unknown with <see cref="AnswerBasis.None"/>; this
    ///     is the behavior before the dictionary existed. It also makes <see cref="IWordSenseClassifier.GetCandidates"/>
    ///     return nothing for a dictionary-only lemma. The setting never changes a context answer.
    /// </remarks>
    public bool UseLexicon { get; init; } = true;
}

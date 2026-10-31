namespace DemaConsulting.WordSense;

/// <summary>
///     Marks a model whose slot probabilities for a word of two or more parts of speech are an estimate of the part of
///     speech (a part-of-speech classifier that reads the sentence), not only the mass of senses.
/// </summary>
/// <remarks>
///     <para>
///     The composition <see cref="FallbackWordSenseModel"/> reads the marker of its primary model. A marked primary
///     model that provides the evidence has a probability for every slot of a word of several parts of speech, closed
///     classes included, so the composition reweighs those probabilities against the fallback model at every level of
///     the answer and does not split the closed-class slots from the fallback result. A primary model without the
///     marker, or one that does not provide the evidence, is composed by the rules for a model whose slot probabilities are
///     the mass of its senses.
///     </para>
///     <para>
///     A marked model can also provide tense evidence: for a same-spelling verb (a verb with a past and a non-past
///     analysis, see <see cref="TenseFusion"/>) the probability that the verb is past. The composition then sets the
///     tense of such a word from that probability combined with the past and non-past split of the fallback model.
///     </para>
///     <para>
///     The interface is internal: the encoder models of this repository implement it, and a third-party model cannot
///     claim the behavior.
///     </para>
/// </remarks>
internal interface IPartOfSpeechEvidenceModel
{
    /// <summary>
    ///     Gets a value that tells whether the model estimates the part of speech of every word of several parts of
    ///     speech from the sentence.
    /// </summary>
    bool ProvidesPartOfSpeechEvidence { get; }

    /// <summary>
    ///     Gets a value that tells whether the model estimates the tense of every same-spelling verb from the sentence;
    ///     only a model that provides part-of-speech evidence can provide this.
    /// </summary>
    bool ProvidesTenseEvidence { get; }

    /// <summary>
    ///     Classifies every word like <see cref="IWordSenseModel.ClassifyAsync"/> and also gives, for a same-spelling
    ///     verb, the probability that it is past.
    /// </summary>
    /// <param name="words">The words of one tokenized text.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>The results and, parallel to them, the probability of past of each word that is a same-spelling verb the model could judge, otherwise null.</returns>
    Task<(ClassifyResult[] Results, double?[] PastProbability)> ClassifyWithTenseAsync(IEnumerable<string> words, CancellationToken cancellationToken);
}

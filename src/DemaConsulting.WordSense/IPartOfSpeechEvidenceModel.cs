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
///     marker, or one that does not provide the evidence, is composed by the rules for a model whose slot probability is
///     the mass of its senses.
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
}

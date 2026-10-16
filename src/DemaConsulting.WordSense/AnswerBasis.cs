namespace DemaConsulting.WordSense;

/// <summary>
///     Where the answer of a <see cref="WordAnalysis"/> comes from.
/// </summary>
/// <remarks>
///     A <em>context answer</em> is chosen by the classifier from the words around the target, so it carries
///     confidence figures. A <em>dictionary answer</em> comes from the table alone: the word has one part of speech in
///     the dictionary, or several and nothing to choose between them. The basis tells a caller which of the two it is
///     looking at, without inspecting the margins.
/// </remarks>
/// <example>
///     <code>
///     using var classifier = WordSenseClassifier.Create();
///     WordAnalysis[] analyses = await classifier.ClassifyAsync(["They", "will", "record", "it"]);
///
///     foreach (var analysis in analyses)
///     {
///         Console.WriteLine($"{analysis.Word}: {analysis.Basis}, {analysis.Candidates.Count} candidate(s)");
///     }
///     </code>
/// </example>
public enum AnswerBasis
{
    /// <summary>
    ///     The word is not known: there is no answer and there are no candidates.
    /// </summary>
    None = 0,

    /// <summary>
    ///     The word has a trained classifier that scored it in context; it was answered or abstained by margin.
    /// </summary>
    Context = 1,

    /// <summary>
    ///     The word was found in the dictionary: it is answered when it has exactly one part of speech, otherwise the
    ///     word abstains and its candidates list the possible parts of speech.
    /// </summary>
    Dictionary = 2,
}

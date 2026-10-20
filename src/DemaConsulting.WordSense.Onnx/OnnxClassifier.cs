using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;

namespace DemaConsulting.WordSense.Onnx;

/// <summary>
///     The classifier of the encoder models: the unit that decides the answer for every word of a tokenized text.
/// </summary>
/// <remarks>
///     <para>
///     In this version the classifier answers from the lexicon alone, so no ONNX session, model file or trained weight
///     is needed to use it. An unknown word gets <see cref="ClassifyResult.Unknown"/>. A word with one analysis gets that
///     analysis with probability 1: at the <see cref="SenseLevel.Sense"/> level when its lemma and part of speech have
///     exactly one sense (that sense is listed with probability 1) and otherwise at the
///     <see cref="SenseLevel.PartOfSpeech"/> level. A word with several analyses gets every analysis with the uniform
///     probability 1/n (the lexicon order is kept, so the first analysis is the selected one) at the
///     <see cref="SenseLevel.PartOfSpeech"/> level, and no analysis lists scored senses, because nothing was scored. A word that
///     is answered without scored senses is left to the fallback model of a composition (see
///     <see cref="FallbackWordSenseModel"/>). The scoring of fine senses with the encoder is added here later, without
///     touching the lifecycle code of the model.
///     </para>
///     <para>
///     The classifier holds no mutable state and is thread-safe.
///     </para>
/// </remarks>
internal sealed class OnnxClassifier
{
    /// <summary>
    ///     Classifies every word of a tokenized sentence or text.
    /// </summary>
    /// <param name="words">The words in order, including punctuation tokens.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>A task with an array parallel to <paramref name="words"/>.</returns>
    /// <remarks>
    ///     The words are validated here (a null list or element is rejected with the exception that the model
    ///     reports too) so that the unit keeps its contract when it is used on its own. The token is checked before the
    ///     work starts, so an already canceled token cancels the call even for an empty list.
    /// </remarks>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="words"/> is null.</exception>
    /// <exception cref="ArgumentException">Thrown when an element is null.</exception>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken"/> is cancelled.</exception>
    [SuppressMessage("Major Code Smell", "S2325:Methods and properties that don't access instance data should be static", Justification = "The classifier is an instance unit: the encoder scoring that extends it later reads the state of the model, and the model shares one instance between its classifications.")]
    public Task<ClassifyResult[]> ClassifyAsync(IEnumerable<string> words, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(words);
        var list = words.ToList();
        if (list.Any(w => w is null))
        {
            throw new ArgumentException("A word must not be null.", nameof(words));
        }

        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(list.Select(Classify).ToArray());
    }

    /// <summary>
    ///     Answers one word from the lexicon.
    /// </summary>
    /// <param name="word">The word.</param>
    /// <returns>The result.</returns>
    private static ClassifyResult Classify(string word)
    {
        if (!WordLexicon.TryLookup(word, out var details) || details.IsEmpty)
        {
            return ClassifyResult.Unknown(word);
        }

        // A word with one analysis is certain about it; its sense is named only when the lemma and part of speech have
        // exactly one sense (nothing was scored, nothing is guessed)
        if (details.Length == 1)
        {
            var only = WordLexicon.SensesOf(details[0].Lemma, details[0].PartOfSpeech);
            if (only.Length == 1)
            {
                var sense = only[0];
                return new ClassifyResult(word, true, [new AnalysisCandidate(details[0], 1.0, [new SenseCandidate(sense.SenseId, 1.0)])], details[0], SenseLevel.Sense, 1.0, sense.GroupId, sense.SenseId);
            }

            return new ClassifyResult(word, true, [new AnalysisCandidate(details[0], 1.0, [])], details[0], SenseLevel.PartOfSpeech, 1.0, null, null);
        }

        // Several analyses: every analysis is equally likely and no sense is scored, so the result is left to a fallback model
        var probability = 1.0 / details.Length;
        var analyses = ImmutableArray.CreateBuilder<AnalysisCandidate>(details.Length);
        foreach (var analysis in details)
        {
            analyses.Add(new AnalysisCandidate(analysis, probability, []));
        }

        return new ClassifyResult(word, true, analyses.MoveToImmutable(), details[0], SenseLevel.PartOfSpeech, probability, null, null);
    }
}

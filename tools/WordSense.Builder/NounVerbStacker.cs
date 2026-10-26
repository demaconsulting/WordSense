using System.Collections.Immutable;
using DemaConsulting.WordSense;

namespace WordSense.Builder;

/// <summary>
///     One training word of the noun/verb model: the features of its context, the log odds of verb against noun that the
///     rest of the model gave it when its sentence was held out, and its class.
/// </summary>
/// <param name="Hashes">The 16-bit hashes of the context features of the word (see <see cref="CueModel.ExtractNounVerb"/>).</param>
/// <param name="Offset">The log odds of the verb slot against the noun slot of the model without the noun/verb table.</param>
/// <param name="IsVerb">True when the gold tag of the word is verb, false when it is noun.</param>
internal sealed record NounVerbSample(ushort[] Hashes, double Offset, bool IsVerb);

/// <summary>
///     Fits the shared noun/verb table as a correction of the rest of the model: a logistic regression of verb against
///     noun over the context features of every training word that has both analyses, with the log odds that the model
///     without the table gave the word (when the sentence was held out of the tagger counts) as a fixed offset.
/// </summary>
/// <remarks>
///     <para>
///     The tagger and the cue tables already read the neighbors of a word. A table that was trained on the neighbors alone
///     would count the same evidence a second time, and a weight chosen on the development sets would only shrink that
///     double count. Here the weights answer a different question: given what the rest of the model says about a word
///     that was not in its counts, which contexts show that it is wrong? The offset enters with the weight 1, so the
///     weights add to the log odds of the model as they are, and the runtime weight of the table is near 1.
///     </para>
///     <para>
///     The training split of the open corpus is cut into folds by a stable hash of the sentence, as the running-text
///     reliability of the cue tables does; for each fold the tagger statistics are counted on the other folds and the cue
///     tables get the running-text reliabilities measured on the other folds alone (the final reliabilities were measured
///     from the gold tags of every fold, so they would carry the answers of the fold into its own offsets), and the fold
///     is classified with that model. A sample therefore depends on no gold tag of its own fold. The features are hashed
///     to 16 bits as the runtime reads them, so words whose features collide share a weight in the training as they do at
///     run time, and a weight counts once for every feature of the word that has its hash, as at run time. Nothing of the
///     development or test splits is read.
///     </para>
/// </remarks>
internal static class NounVerbStacker
{
    private const int Epochs = 200;
    private const double LearningRate = 0.5;
    private const double MaximumOffset = 30.0;

    /// <summary>
    ///     Fits the noun/verb table of a model and sets it.
    /// </summary>
    /// <param name="data">The trained model with the final cue reliabilities (only their anchor part is used to cross-fit the table).</param>
    /// <param name="training">The training split of the representative open corpus with gold tags.</param>
    /// <param name="lexicon">The lexicon of the core package.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <param name="runtime">How to make tables and classifiers (the shipped word image when null).</param>
    /// <returns>The model with the noun/verb table; <paramref name="data"/> itself when the folds are off.</returns>
    public static async Task<BasicModelData> ApplyAsync(
        BasicModelData data,
        IReadOnlyList<TaggedSentence> training,
        Func<string, ImmutableArray<WordDetails>> lexicon,
        CancellationToken cancellationToken,
        CueRunningRuntime? runtime = null)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (data.Hyperparameters.RunningFolds < 2)
        {
            return data;
        }

        var samples = await CollectAsync(data, training, lexicon, cancellationToken, runtime).ConfigureAwait(false);
        return data with { NounVerb = Fit(samples, data.Hyperparameters) };
    }

    /// <summary>
    ///     Collects the training words by cross-fitting: each fold is classified by a model that has seen nothing of the
    ///     fold, neither its words in the tagger counts nor its gold tags in the running-text reliability of the cue tables.
    /// </summary>
    /// <param name="data">The trained model; its hyper-parameters give the number of folds and its cue tables the anchor reliabilities, from which the running-text reliabilities are measured again for every fold on the other folds.</param>
    /// <param name="training">The training split with gold tags.</param>
    /// <param name="lexicon">The lexicon of the core package.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <param name="runtime">How to make tables and classifiers (the shipped word image when null).</param>
    /// <returns>One sample for every gold noun or gold verb that has a noun and a verb analysis and that the model answers, fold by fold.</returns>
    public static async Task<IReadOnlyList<NounVerbSample>> CollectAsync(
        BasicModelData data,
        IReadOnlyList<TaggedSentence> training,
        Func<string, ImmutableArray<WordDetails>> lexicon,
        CancellationToken cancellationToken,
        CueRunningRuntime? runtime = null)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(training);
        ArgumentNullException.ThrowIfNull(lexicon);
        runtime ??= CueRunningRuntime.Shipped(lexicon);
        var folds = data.Hyperparameters.RunningFolds;
        var samples = new List<NounVerbSample>();
        var foldOf = new int[training.Count];
        for (var i = 0; i < training.Count; i++)
        {
            foldOf[i] = BasicTuner.StableHash(string.Join(' ', training[i].Words)) % folds;
        }

        // The reliabilities that the running text gave the cue tables are measured from the gold tags of every fold, so a
        // fold is scored with the reliabilities of the anchors and the running text of the other folds alone
        var anchorOnly = data with { Cues = [.. data.Cues.Select(c => c with { Reliability = c.AnchorReliability, RunningTokens = 0, RunningHelped = 0, RunningHurt = 0 })] };
        var features = new FeatureRef[CueModel.MaxNounVerbFeatures];
        for (var fold = 0; fold < folds; fold++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // A fold with no sentence has nothing to score, and a fold that holds every sentence leaves no counts to score it with
            if (!foldOf.Contains(fold) || foldOf.All(f => f == fold))
            {
                continue;
            }

            var others = training.Where((_, i) => foldOf[i] != fold).ToList();
            var heldOut = await CueRunningReliability.ApplyAsync(anchorOnly, others, lexicon, cancellationToken, runtime).ConfigureAwait(false);
            var counts = TaggerCounts.Count(others, lexicon);
            var tagger = BasicTrainer.WithCounts(heldOut, counts, lexicon);
            var classifier = runtime.Classifier(runtime.Load(tagger));
            for (var s = 0; s < training.Count; s++)
            {
                if (foldOf[s] != fold)
                {
                    continue;
                }

                var sentence = training[s];
                var results = await classifier.ClassifyAsync(sentence.Words, cancellationToken).ConfigureAwait(false);
                for (var i = 0; i < sentence.Words.Length; i++)
                {
                    if (SampleOf(sentence, i, results[i], lexicon, features) is { } sample)
                    {
                        samples.Add(sample);
                    }
                }
            }
        }

        return samples;
    }

    private static NounVerbSample? SampleOf(TaggedSentence sentence, int index, ClassifyResult result, Func<string, ImmutableArray<WordDetails>> lexicon, FeatureRef[] features)
    {
        if (sentence.Tags[index] is not (PosTag.Noun or PosTag.Verb) || result.Details is null)
        {
            return null;
        }

        var noun = 0.0;
        var verb = 0.0;
        foreach (var analysis in result.Analyses)
        {
            noun += analysis.Details.PartOfSpeech == PartOfSpeech.Noun ? analysis.Probability : 0.0;
            verb += analysis.Details.PartOfSpeech == PartOfSpeech.Verb ? analysis.Probability : 0.0;
        }

        if (noun <= 0 || verb <= 0)
        {
            return null;
        }

        // The context is the sentence segment around the word, as the classifier reads it
        var (start, length) = BasicClassifier.SegmentOf(sentence.Words, index);
        var lower = new string[length];
        for (var k = 0; k < length; k++)
        {
            lower[k] = BasicClassifier.Lower(sentence.Words[start + k]);
        }

        var count = CueModel.ExtractNounVerb(lower, index - start, lexicon, features);
        var hashes = new ushort[count];
        for (var f = 0; f < count; f++)
        {
            hashes[f] = features[f].Hash;
        }

        return new NounVerbSample(hashes, Math.Clamp(Math.Log(verb / noun), -MaximumOffset, MaximumOffset), sentence.Tags[index] == PosTag.Verb);
    }

    /// <summary>
    ///     Fits the weights of the context features: the logistic regression of the class on the sum of the weights of the
    ///     features and the fixed offset, with an L2 penalty, by gradient descent with a per-feature step (Adagrad).
    /// </summary>
    /// <param name="samples">The training words.</param>
    /// <param name="hp">The hyper-parameters: the fewest words a feature needs and the L2 penalty.</param>
    /// <returns>The table, or null when the samples have no word of one of the classes or no feature occurs often enough.</returns>
    public static BasicNounVerbRow? Fit(IReadOnlyList<NounVerbSample> samples, BasicHyperparameters hp)
    {
        ArgumentNullException.ThrowIfNull(samples);
        ArgumentNullException.ThrowIfNull(hp);
        var verbs = samples.Count(s => s.IsVerb);
        var nouns = samples.Count - verbs;
        if (verbs == 0 || nouns == 0)
        {
            return null;
        }

        var occurrences = new int[ushort.MaxValue + 1];
        foreach (var sample in samples)
        {
            foreach (var hash in sample.Hashes.Distinct())
            {
                occurrences[hash]++;
            }
        }

        var kept = Enumerable.Range(0, occurrences.Length).Where(h => occurrences[h] >= hp.NounVerbMinFeatureCount).ToArray();
        if (kept.Length == 0)
        {
            return null;
        }

        var slot = new int[occurrences.Length];
        Array.Fill(slot, -1);
        for (var k = 0; k < kept.Length; k++)
        {
            slot[kept[k]] = k;
        }

        // A feature that two words of a sample hash to the same value counts once for each, as the runtime adds the weight of every extracted feature
        var rows = samples.Select(s => (Slots: s.Hashes.Select(h => slot[h]).Where(k => k >= 0).ToArray(), s.Offset, Target: s.IsVerb ? 1.0 : 0.0)).ToArray();
        var weights = new double[kept.Length];
        var accumulated = new double[kept.Length];
        var gradient = new double[kept.Length];
        for (var epoch = 0; epoch < Epochs; epoch++)
        {
            Array.Clear(gradient);
            foreach (var (slots, offset, target) in rows)
            {
                var z = offset;
                foreach (var k in slots)
                {
                    z += weights[k];
                }

                var error = (1.0 / (1.0 + Math.Exp(-z))) - target;
                foreach (var k in slots)
                {
                    gradient[k] += error;
                }
            }

            for (var k = 0; k < weights.Length; k++)
            {
                var g = gradient[k] + (hp.NounVerbL2 * weights[k]);
                accumulated[k] += g * g;
                weights[k] -= LearningRate * g / (Math.Sqrt(accumulated[k]) + 1e-8);
            }
        }

        var quantized = new sbyte[kept.Length];
        for (var k = 0; k < quantized.Length; k++)
        {
            quantized[k] = (sbyte)Math.Clamp(Math.Round(weights[k] / CueModel.Quantum), sbyte.MinValue, sbyte.MaxValue);
        }

        return new BasicNounVerbRow([.. kept.Select(h => (ushort)h)], quantized, nouns, verbs);
    }
}

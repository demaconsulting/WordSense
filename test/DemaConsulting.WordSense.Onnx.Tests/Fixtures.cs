using System.Collections.Immutable;
using System.Reflection;
using DemaConsulting.WordSense.MiniLM;

namespace DemaConsulting.WordSense.Onnx.Tests;

/// <summary>
///     Builds weights and feature vectors over the real senses of the shipped word image, so that a chosen sense
///     wins by a chosen margin.
/// </summary>
/// <remarks>
///     A fixture lemma has one weight row per sense, and row <c>i</c> is <see cref="Scale"/> times the unit vector of
///     component <c>i</c>. A feature vector is built so that, after the classifier scales it to unit length, the score
///     of every named sense is exactly the requested value: the last component absorbs the remaining length. The rows
///     of a form chooser use the components from <see cref="ChooserOffset"/> on, so that the lemma and the chooser of
///     a word are steered independently.
/// </remarks>
internal static class Fixtures
{
    /// <summary>
    ///     The dense dimension of the fixtures; a lemma has at most <see cref="ChooserOffset"/> senses.
    /// </summary>
    public const int Dimension = 40;

    /// <summary>
    ///     The length of the unit vector that a weight row multiplies.
    /// </summary>
    public const float Scale = 10f;

    /// <summary>
    ///     The first component that a form chooser scores.
    /// </summary>
    public const int ChooserOffset = 32;

    /// <summary>
    ///     Gets the inventory hash of the shipped word image.
    /// </summary>
    /// <returns>The hash.</returns>
    public static byte[] InventoryHash() => WordLexicon.Current.InventoryHash.ToArray();

    /// <summary>
    ///     Lists the senses of a lemma across all parts of speech, in the column order of its weights.
    /// </summary>
    /// <param name="lemma">The lemma.</param>
    /// <returns>The senses.</returns>
    public static ImmutableArray<SenseInfo> Senses(string lemma) =>
        [.. Enum.GetValues<PartOfSpeech>().SelectMany(pos => WordLexicon.SensesOf(lemma, pos))];

    /// <summary>
    ///     Lists the sense ids of a lemma across all parts of speech, in the column order of its weights.
    /// </summary>
    /// <param name="lemma">The lemma.</param>
    /// <returns>The sense ids.</returns>
    public static string[] SenseIds(string lemma) => [.. Senses(lemma).Select(s => s.SenseId)];

    /// <summary>
    ///     Creates weights whose row <c>r</c> is <paramref name="scale"/> times the unit vector of component
    ///     <paramref name="offset"/> + <c>r</c>, with zero biases.
    /// </summary>
    /// <param name="rows">The number of rows.</param>
    /// <param name="dimension">The dense dimension.</param>
    /// <param name="scale">The value of the one non-zero weight of a row.</param>
    /// <param name="fine">The fine threshold.</param>
    /// <param name="group">The group threshold.</param>
    /// <param name="offset">The component of the first row.</param>
    /// <returns>The weights.</returns>
    public static LemmaWeights Identity(int rows, int dimension, float scale, float fine, float group, int offset = 0)
    {
        var weights = new float[rows * dimension];
        for (var row = 0; row < rows; row++)
        {
            weights[(row * dimension) + offset + row] = scale;
        }

        return new LemmaWeights(weights, new float[rows], dimension, fine, group);
    }

    /// <summary>
    ///     Creates the trained lemma over all senses of a real lemma.
    /// </summary>
    /// <param name="lemma">The lemma.</param>
    /// <param name="fine">The fine threshold.</param>
    /// <param name="group">The group threshold.</param>
    /// <param name="dimension">The dense dimension.</param>
    /// <param name="choosers">The form choosers stored with the lemma.</param>
    /// <returns>The weighted lemma.</returns>
    public static WeightedLemma Lemma(string lemma, float fine = LemmaWeights.Never, float group = LemmaWeights.Never, int dimension = Dimension, FormChooser[]? choosers = null)
    {
        var ids = SenseIds(lemma);
        return new WeightedLemma(lemma, ids, Identity(ids.Length, dimension, Scale, fine, group), choosers ?? []);
    }

    /// <summary>
    ///     Creates a form chooser over labels.
    /// </summary>
    /// <param name="form">The form.</param>
    /// <param name="labels">The labels in score order.</param>
    /// <param name="threshold">The threshold of the chooser.</param>
    /// <param name="dimension">The dense dimension.</param>
    /// <returns>The chooser.</returns>
    public static FormChooser Chooser(string form, string[] labels, float threshold, int dimension = Dimension) =>
        new(form, labels, Identity(labels.Length, dimension, Scale, threshold, LemmaWeights.Never, ChooserOffset));

    /// <summary>
    ///     Creates weights data for the word image with a zero mean.
    /// </summary>
    /// <param name="dimension">The dense dimension.</param>
    /// <param name="lemmas">The lemmas.</param>
    /// <returns>The data.</returns>
    public static WeightsData Data(int dimension, params WeightedLemma[] lemmas) =>
        new("fixture-model", InventoryHash(), dimension, new float[dimension], lemmas);

    /// <summary>
    ///     Creates a resolved table of fixture lemmas.
    /// </summary>
    /// <param name="lemmas">The lemmas.</param>
    /// <returns>The table.</returns>
    public static WeightedLemmaTable Table(params WeightedLemma[] lemmas) => new(Data(Dimension, lemmas));

    /// <summary>
    ///     Gets the component of a sense in the feature vector: its column among the senses of its lemma.
    /// </summary>
    /// <param name="lemma">The lemma.</param>
    /// <param name="senseId">The sense id.</param>
    /// <returns>The component.</returns>
    public static int Column(string lemma, string senseId) => Array.IndexOf(SenseIds(lemma), senseId);

    /// <summary>
    ///     Gets the component of a form chooser label in the feature vector.
    /// </summary>
    /// <param name="label">The index of the label.</param>
    /// <returns>The component.</returns>
    public static int Label(int label) => ChooserOffset + label;

    /// <summary>
    ///     Creates a feature vector whose scores (for the fixtures, with <see cref="Scale"/>) are the given values and
    ///     zero everywhere else.
    /// </summary>
    /// <param name="scores">The component and score pairs.</param>
    /// <returns>The vector, to be returned by the encoder.</returns>
    public static double[] Feature(params (int Component, double Score)[] scores)
    {
        var vector = new double[Dimension];
        foreach (var (component, score) in scores)
        {
            vector[component] = score;
        }

        var rest = ((double)Scale * Scale) - vector.Sum(v => v * v);
        Assert.True(rest >= 0, "The requested scores are too large for the scale of the fixtures.");
        vector[^1] = Math.Sqrt(rest);
        return vector;
    }

    /// <summary>
    ///     Creates a feature vector for the senses of a lemma.
    /// </summary>
    /// <param name="lemma">The lemma.</param>
    /// <param name="scores">The sense id and score pairs.</param>
    /// <returns>The vector.</returns>
    public static double[] Feature(string lemma, params (string SenseId, double Score)[] scores) =>
        Feature([.. scores.Select(s => (Column(lemma, s.SenseId), s.Score))]);

    /// <summary>
    ///     Computes the softmax of scores with plain arithmetic.
    /// </summary>
    /// <param name="scores">The scores.</param>
    /// <returns>The probabilities.</returns>
    public static double[] Softmax(IReadOnlyList<double> scores)
    {
        var exp = scores.Select(Math.Exp).ToArray();
        var sum = exp.Sum();
        return [.. exp.Select(e => e / sum)];
    }
}

/// <summary>
///     A fake of the encoder delegate that returns designed vectors and records what it was asked.
/// </summary>
/// <param name="vectorOf">Gives the vector of a target word, or null for a word without word pieces.</param>
internal sealed class FakeEncoder(Func<string, double[]?> vectorOf)
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="FakeEncoder"/> class that returns one vector for every word.
    /// </summary>
    /// <param name="vector">The vector.</param>
    public FakeEncoder(double[]? vector)
        : this(_ => vector)
    {
    }

    /// <summary>
    ///     Gets the target indexes of every call.
    /// </summary>
    public List<int[]> Targets { get; } = [];

    /// <summary>
    ///     Gets the words of every call.
    /// </summary>
    public List<string[]> Texts { get; } = [];

    /// <summary>
    ///     Gets the number of calls.
    /// </summary>
    public int Calls => Targets.Count;

    /// <summary>
    ///     Encodes the targets: one designed vector each.
    /// </summary>
    /// <param name="words">The words of the text.</param>
    /// <param name="targets">The target indexes.</param>
    /// <param name="_">The token, which the fake ignores; it is part of the signature of the encoder delegate.</param>
    /// <returns>The vectors parallel to the targets.</returns>
    public ValueTask<double[]?[]> EncodeAsync(IReadOnlyList<string> words, IReadOnlyList<int> targets, CancellationToken _)
    {
        Targets.Add([.. targets]);
        Texts.Add([.. words]);
        return ValueTask.FromResult(targets.Select(t => vectorOf(words[t])).ToArray());
    }
}

/// <summary>
///     An assembly that serves embedded resources from memory, standing in for the assembly of a model package.
/// </summary>
/// <param name="resources">The bytes of each resource by its manifest resource name.</param>
internal sealed class FixtureAssembly(IReadOnlyDictionary<string, byte[]> resources) : Assembly
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="FixtureAssembly"/> class that serves one resource.
    /// </summary>
    /// <param name="resourceName">The manifest resource name.</param>
    /// <param name="data">The bytes of the resource.</param>
    public FixtureAssembly(string resourceName, byte[] data)
        : this(new Dictionary<string, byte[]> { [resourceName] = data })
    {
    }

    /// <inheritdoc />
    public override Stream? GetManifestResourceStream(string name) =>
        resources.TryGetValue(name, out var data) ? new MemoryStream(data) : null;
}

/// <summary>
///     An encoder model with the real MiniLM model files but with weights of its own (over the real senses of the word
///     image), so that the lifecycle of the model can be tested without the embedded weights of a model package.
/// </summary>
internal sealed class FixtureEncoderModel : OnnxWordSenseModel
{
    /// <summary>
    ///     The manifest resource name of the fixture weights.
    /// </summary>
    public const string ResourceName = "fixture.weights.bin";

    /// <summary>
    ///     The manifest resource name of the fixture part-of-speech head.
    /// </summary>
    public const string HeadResourceName = "fixture.head.bin";

    /// <summary>
    ///     The hidden size of the MiniLM encoder.
    /// </summary>
    public const int HiddenDimension = 384;

    /// <summary>
    ///     Initializes a new instance of the <see cref="FixtureEncoderModel"/> class.
    /// </summary>
    /// <param name="modelDirectory">The directory that holds the model files, or null for the per-user store.</param>
    /// <param name="lemmas">The trained lemmas; by default the lemma <c>just</c> without thresholds.</param>
    public FixtureEncoderModel(string? modelDirectory, params WeightedLemma[] lemmas)
        : base(CreateSpec(lemmas.Length == 0 ? [Fixtures.Lemma("just", dimension: HiddenDimension)] : lemmas), modelDirectory, 128)
    {
        IntraOpThreads = 2;
    }

    /// <summary>
    ///     Initializes a new instance of the <see cref="FixtureEncoderModel"/> class with the default fixture lemma
    ///     and a changed model description (for example other pinned digests).
    /// </summary>
    /// <param name="modelDirectory">The directory that holds the model files.</param>
    /// <param name="adjust">Changes the model description.</param>
    public FixtureEncoderModel(string? modelDirectory, Func<ModelSpec, ModelSpec> adjust)
        : base(adjust(CreateSpec([Fixtures.Lemma("just", dimension: HiddenDimension)])), modelDirectory, 128)
    {
        IntraOpThreads = 2;
    }

    private static ModelSpec CreateSpec(WeightedLemma[] lemmas)
    {
        using var minilm = new MiniLMEncoderModel(null, 128);
        var data = new WeightsData(minilm.Spec.ModelId, Fixtures.InventoryHash(), minilm.HiddenSize, new float[minilm.HiddenSize], lemmas);

        // A head bound to the real MiniLM encoder file, so that the model loads and checks it like an embedded one
        var onnx = minilm.Spec.Files.First(f => f.Name == minilm.Spec.OnnxFileName);
        var head = HeadSpec.Create(minilm.HiddenSize) with { ModelId = minilm.Spec.ModelId, EncoderSha256 = Convert.FromHexString(onnx.Sha256) };
        var resources = new Dictionary<string, byte[]> { [ResourceName] = BlobWriter.Write(data), [HeadResourceName] = HeadBlobWriter.Write(head) };
        return minilm.Spec with { WeightsAssembly = new FixtureAssembly(resources), WeightsResourceName = ResourceName, HeadResourceName = HeadResourceName };
    }
}

namespace DemaConsulting.WordSense.Basic;

/// <summary>
///     The basic model for DemaConsulting.WordSense: chooses between the senses of an ambiguous English word from the
///     words around it, with no neural network, no model files and no ONNX Runtime.
/// </summary>
/// <remarks>
///     <para>
///     Package: the NuGet package <c>DemaConsulting.WordSense.Basic</c>, which targets <c>net8.0</c>, <c>net9.0</c> and
///     <c>net10.0</c>; it depends only on <c>DemaConsulting.WordSense</c>. Install it with
///     <c>dotnet add package DemaConsulting.WordSense.Basic</c>. It uses hand-written rules; no weights are embedded and
///     nothing is downloaded. It is small and fast and less accurate than the encoder models; the comparison is
///     in the build notes (https://github.com/demaconsulting/WordSense/tree/main/docs/build_notes).
///     </para>
///     <para>
///     Start with <see cref="BasicWordSenseModel"/>: create it, call <c>EnsureModelAsync</c> (it completes at once), then
///     <c>Create</c> to get an <see cref="IWordSenseClassifier"/>.
///     </para>
/// </remarks>
/// <example>
///     Quickstart: classify a sentence and read the part of speech of the ambiguous word.
///     <code>
///     var model = new BasicWordSenseModel();
///     await model.EnsureModelAsync();
///     IWordSenseClassifier classifier = model.Create();
///
///     string[] words = ["They", "will", "record", "the", "record", "."];
///     WordSense[] senses = await classifier.ClassifyAsync(words, CancellationToken.None);
///
///     for (var i = 0; i &lt; words.Length; i++)
///     {
///         Console.WriteLine($"{words[i]}: {senses[i].Details?.PartOfSpeech.ToString() ?? "-"} ({senses[i].Confidence:0.00})");
///     }
///     </code>
/// </example>
internal static class NamespaceDoc;

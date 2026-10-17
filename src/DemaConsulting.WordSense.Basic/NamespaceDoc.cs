namespace DemaConsulting.WordSense.Basic;

/// <summary>
///     The basic model for DemaConsulting.WordSense: chooses between the senses of an ambiguous English word from the
///     words around it, with no neural network, no model files and no ONNX Runtime.
/// </summary>
/// <remarks>
///     <para>
///     Package: the NuGet package <c>DemaConsulting.WordSense.Basic</c>, which targets <c>net8.0</c>, <c>net9.0</c> and
///     <c>net10.0</c>; it depends only on <c>DemaConsulting.WordSense</c>. Install it with
///     <c>dotnet add package DemaConsulting.WordSense.Basic</c>. It uses a statistical part-of-speech tagger and a context cue
///     model whose small tables are embedded; nothing is downloaded. It is small and fast and less accurate than the encoder models; the comparison is
///     in the build notes (https://github.com/demaconsulting/WordSense/tree/main/docs/build_notes).
///     </para>
///     <para>
///     Start with <see cref="BasicWordSenseModel"/>: create it, call <c>EnsureModelAsync</c> (it completes at once), then
///     <c>Create</c> to get an <see cref="IWordSenseClassifier"/>. The model needs no disposal.
///     </para>
///     <para>
///     Confidence: 0 means the word is unknown to the lexicon (<c>Details</c> is null), 1 means the word has a single
///     sense in the lexicon, and a word with several senses gets the probability of the chosen sense, between one over
///     the number of senses and 1. The value is a relative score, not a calibrated probability, but higher values are
///     more often right, so a threshold such as 0.8 trades coverage for precision; the build notes list the measured
///     figures. An ambiguous word is never left undecided.
///     </para>
///     <para>
///     Which model to choose: Basic is the smallest and fastest and needs nothing else, but it is less accurate than
///     the encoder models. If accuracy matters more, use
///     <c>DemaConsulting.WordSense.MiniLM</c> (small and fast) or <c>DemaConsulting.WordSense.BertBaseCased</c> (larger,
///     case-aware and more accurate); see their namespace documentation for the figures.
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
///     WordSense[] senses = await classifier.ClassifyAsync(words);
///
///     for (var i = 0; i &lt; words.Length; i++)
///     {
///         Console.WriteLine($"{words[i]}: {senses[i].Details?.PartOfSpeech.ToString() ?? "-"} ({senses[i].Confidence:0.00})");
///     }
///     </code>
/// </example>
internal static class NamespaceDoc;

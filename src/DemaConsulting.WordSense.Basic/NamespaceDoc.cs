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
///     <c>Create</c> to get an <see cref="IWordSenseClassifier"/>. The model needs no disposal.
///     </para>
///     <para>
///     Confidence: 0 means the word is unknown or no rule applied (undecided; <c>Details</c> is null), 1 means the word
///     has a single sense in the lexicon, and a word decided by a rule gets that rule's fixed value, the precision the
///     rule achieved on the training sentences. Thresholds are therefore meaningful: use 0.9 to keep only the most
///     reliable rules.
///     </para>
///     <list type="table">
///     <listheader><term>Rule</term><description>Selects (confidence)</description></listheader>
///     <item><term>after-modal (previous word is a modal or a form of "do")</term><description>verb (0.99)</description></item>
///     <item><term>after-to (previous word is "to")</term><description>verb (0.98)</description></item>
///     <item><term>after-subject-pronoun</term><description>verb (0.99)</description></item>
///     <item><term>after-determiner</term><description>noun, else adjective (0.95)</description></item>
///     <item><term>after-degree-adverb ("very", "too", ...)</term><description>adjective (0.89)</description></item>
///     <item><term>after-preposition</term><description>noun (0.82)</description></item>
///     <item><term>before-auxiliary (next word is "is", "will", ...)</term><description>noun (0.97)</description></item>
///     <item><term>before-noun (next word is only a noun)</term><description>adjective (0.77)</description></item>
///     </list>
///     <para>
///     Which model to choose: Basic is the smallest and fastest and needs nothing else, but it decides only the part of
///     speech from the neighbors and leaves some words undecided. If accuracy matters more, use
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

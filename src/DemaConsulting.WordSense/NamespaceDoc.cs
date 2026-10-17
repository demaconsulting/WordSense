namespace DemaConsulting.WordSense;

/// <summary>
///     The core of DemaConsulting.WordSense: the English word lexicon and the contracts that every word-sense model
///     package implements.
/// </summary>
/// <remarks>
///     <para>
///     Package: the NuGet package <c>DemaConsulting.WordSense</c>, which targets <c>net8.0</c>, <c>net9.0</c> and
///     <c>net10.0</c> and has no dependencies. It holds <see cref="WordLexicon"/> (every known word with its lemma, part
///     of speech, meaning and respelling, loaded from an embedded table on first use), the types
///     <see cref="WordDetails"/> and <see cref="PartOfSpeech"/>, and the contracts <see cref="IWordSenseModel"/>,
///     <see cref="MirrorOptions"/>, <see cref="IWordSenseClassifier"/> and <see cref="WordSense"/>. It cannot
///     disambiguate a word in context by itself: add one of the model packages for that.
///     </para>
///     <para>
///     Model packages: <c>DemaConsulting.WordSense.Basic</c> (hand-written rules, no model files, no ONNX; see
///     https://github.com/demaconsulting/WordSense/tree/main/src/DemaConsulting.WordSense.Basic),
///     <c>DemaConsulting.WordSense.MiniLM</c> (a small encoder; see
///     https://github.com/demaconsulting/WordSense/tree/main/src/DemaConsulting.WordSense.MiniLM) and
///     <c>DemaConsulting.WordSense.BertBaseCased</c> (a larger, case-aware encoder; see
///     https://github.com/demaconsulting/WordSense/tree/main/src/DemaConsulting.WordSense.BertBaseCased). The two
///     encoder packages share the ONNX plumbing of <c>DemaConsulting.WordSense.Onnx</c>. The comparison of the models is
///     in the build notes: https://github.com/demaconsulting/WordSense/tree/main/docs/build_notes.
///     </para>
/// </remarks>
/// <example>
///     Look a word up without any model, then classify a sentence with a model package.
///     <code>
///     foreach (WordDetails sense in WordLexicon.Words["record"])
///     {
///         Console.WriteLine($"{sense.Lemma}: {sense.PartOfSpeech}");
///     }
///
///     IWordSenseModel model = new DemaConsulting.WordSense.Basic.BasicWordSenseModel();
///     await model.EnsureModelAsync();
///     IWordSenseClassifier classifier = model.Create();
///     WordSense[] senses = await classifier.ClassifyAsync(["They", "will", "record", "the", "record", "."]);
///     </code>
/// </example>
internal static class NamespaceDoc;

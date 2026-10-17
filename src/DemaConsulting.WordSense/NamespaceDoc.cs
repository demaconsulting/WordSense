namespace DemaConsulting.WordSense;

/// <summary>
///     The core of DemaConsulting.WordSense: the English word table and the abstractions for models that choose the sense
///     of an ambiguous word from its sentence.
/// </summary>
/// <remarks>
///     <para>
///     Package: the NuGet package <c>DemaConsulting.WordSense</c>, which targets <c>net8.0</c>, <c>net9.0</c> and
///     <c>net10.0</c> and has no dependencies. It holds <see cref="Words"/> (every known word with its lemma, part of
///     speech, meaning and respelling, loaded from an embedded table on first use), the types
///     <see cref="WordDetails"/> and <see cref="PartOfSpeech"/>, and the interfaces <see cref="IWordSenseModel"/> and
///     <see cref="IWordSenseClassifier"/>. It cannot disambiguate a word in context by itself: add one of the model
///     packages for that.
///     </para>
///     <para>
///     Model packages: <c>DemaConsulting.WordSense.Basic</c> (feature-based, no model files, no ONNX; see
///     https://github.com/demaconsulting/WordSense/tree/main/src/DemaConsulting.WordSense.Basic),
///     <c>DemaConsulting.WordSense.MiniLM</c> (a small encoder; see
///     https://github.com/demaconsulting/WordSense/tree/main/src/DemaConsulting.WordSense.MiniLM) and
///     <c>DemaConsulting.WordSense.BertBaseCased</c> (a larger, case-aware encoder; see
///     https://github.com/demaconsulting/WordSense/tree/main/src/DemaConsulting.WordSense.BertBaseCased). The
///     comparison of the models is in the build notes:
///     https://github.com/demaconsulting/WordSense/tree/main/docs/build_notes.
///     </para>
///     <para>
///     How to use it: create a model, call <see cref="IWordSenseModel.EnsureModelAsync"/> once, call
///     <see cref="IWordSenseModel.Create"/> and classify sentences with <see cref="IWordSenseClassifier.ClassifyAsync"/>.
///     The result of a word is a <see cref="WordSense"/>: the chosen <see cref="WordDetails"/> (or null when the word is
///     unknown or cannot be decided) and a confidence. Use <see cref="MirrorOptions"/> to download model files from a
///     mirror inside a network that blocks public hosts.
///     </para>
/// </remarks>
/// <example>
///     Look a word up without any model, then classify a sentence with a model package.
///     <code>
///     foreach (WordDetails sense in Words.Find("record"))
///     {
///         Console.WriteLine($"{sense.Lemma}: {sense.PartOfSpeech}");
///     }
///
///     using var model = new DemaConsulting.WordSense.Basic.BasicWordSenseModel();
///     await model.EnsureModelAsync(null, CancellationToken.None);
///     IWordSenseClassifier classifier = model.Create();
///     WordSense[] senses = await classifier.ClassifyAsync(["They", "will", "record", "the", "record", "."], CancellationToken.None);
///     </code>
/// </example>
internal static class NamespaceDoc;

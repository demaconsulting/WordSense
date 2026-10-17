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
///     Model packages: <c>DemaConsulting.WordSense.Basic</c> (statistical tagger, no model files, no ONNX; see
///     https://github.com/demaconsulting/WordSense/tree/main/src/DemaConsulting.WordSense.Basic),
///     <c>DemaConsulting.WordSense.MiniLM</c> (a small encoder; see
///     https://github.com/demaconsulting/WordSense/tree/main/src/DemaConsulting.WordSense.MiniLM) and
///     <c>DemaConsulting.WordSense.BertBaseCased</c> (a larger, case-aware encoder; see
///     https://github.com/demaconsulting/WordSense/tree/main/src/DemaConsulting.WordSense.BertBaseCased). The two
///     encoder packages share the ONNX plumbing of <c>DemaConsulting.WordSense.Onnx</c>. The comparison of the models is
///     in the build notes: https://github.com/demaconsulting/WordSense/tree/main/docs/build_notes.
///     </para>
///     <para>
///     Which model to choose. The figures are from the build notes (<c>docs/build_notes/model-table.md</c>, which the
///     README repeats exactly); they are rounded here, and latency, first-use time and memory are measured on one
///     machine, so treat them as approximate. "Real text" means sentences from openly licensed or public-domain
///     sources and is the realistic expectation; the holdout was written together with the training sentences and
///     is optimistic.
///     </para>
///     <list type="table">
///     <listheader>
///     <term>Model</term>
///     <description>Best sense, holdout / real text; download; latency per sentence; memory added</description>
///     </listheader>
///     <item>
///     <term>Basic</term>
///     <description>about 89% / 85%; none (83 KiB table inside the package); under 0.1 ms; about 21 MiB; no network, no ONNX Runtime</description>
///     </item>
///     <item>
///     <term>MiniLM</term>
///     <description>about 97% / 93%; 23 MiB, once; about 2 ms; about 64 MiB</description>
///     </item>
///     <item>
///     <term>BertBaseCased</term>
///     <description>about 99% / 97%; 104 MiB, once; about 9 ms; about 150 MiB; keeps the case of the text</description>
///     </item>
///     </list>
///     <para>
///     Default recommendation: start with <c>DemaConsulting.WordSense.MiniLM</c>, which is small, fast and accurate
///     enough for most uses. Choose <c>DemaConsulting.WordSense.BertBaseCased</c> when accuracy matters most and the
///     larger download and memory are acceptable. Basic is enough when nothing may be downloaded, startup and memory
///     must stay small, you cannot ship ONNX Runtime, or you can live with roughly one wrong answer in six or seven
///     ambiguous words on real text (about 85% best-sense accuracy), possibly filtered by a confidence threshold.
///     </para>
///     <para>
///     First-use costs (approximate, machine dependent): the lexicon loads on first use of <see cref="WordLexicon"/>
///     (about 120 ms, about 43 MiB); Basic then loads its table in about 22 ms; an encoder model opens its ONNX
///     session on the first <c>Create</c> (about 0.4 s for MiniLM, about 0.5 s for BertBaseCased) after
///     <c>EnsureModelAsync</c> has verified its files. The Basic model has no files, so its <c>Create</c> needs no
///     preceding <c>EnsureModelAsync</c>.
///     </para>
///     <para>
///     Naming: <c>WordSense</c> is both this namespace and the result type <see cref="WordSense"/> (a record struct of
///     the word, its details and a confidence). Classifiers return <c>WordSense[]</c>.
///     </para>
/// </remarks>
/// <example>
///     Look a word up without any model, then classify a sentence with a model package.
///     <code>
///     using DemaConsulting.WordSense;
///     using DemaConsulting.WordSense.Basic;
///
///     foreach (WordDetails sense in WordLexicon.Words["record"])
///     {
///         Console.WriteLine($"{sense.Lemma}: {sense.PartOfSpeech}");
///     }
///
///     IWordSenseModel model = new BasicWordSenseModel();
///     await model.EnsureModelAsync();
///     IWordSenseClassifier classifier = model.Create();
///     WordSense[] senses = await classifier.ClassifyAsync(["They", "will", "record", "the", "record", "."]);
///     </code>
/// </example>
internal static class NamespaceDoc;

namespace DemaConsulting.WordSense.Basic;

/// <summary>
///     The basic model for DemaConsulting.WordSense: chooses the analysis (lemma, part of speech and features) of an
///     ambiguous English word from the words around it, with no neural network, no model files and no ONNX Runtime.
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
///     Start with <see cref="BasicWordSenseModel"/>: create it and call <c>ClassifyAsync</c>.
///     <c>EnsureModelAsync</c> is optional for this model (it completes at once and
///     checks nothing, because there are no files), and <c>ClassifyAsync</c> works with or without it; call it only to keep
///     the code identical to the encoder models. Dispose the model with <c>using</c> like every <see cref="IWordSenseModel"/>
///     (it holds nothing to release, but afterwards <c>EnsureModelAsync</c> and <c>ClassifyAsync</c>
///     throw <see cref="ObjectDisposedException"/>, from the call itself, not through a returned task, and before any
///     other check, so even with an already cancelled token). Disposing while a classification is running is safe,
///     as for every model: new calls throw at once, and a call that is already running completes normally (this model
///     has nothing to release). The Basic model throws every
///     exception of <c>ClassifyAsync</c> (null list, null element, <see cref="OperationCanceledException"/> for a token that is cancelled at the start or between sentence segments) from the call itself. With a token that is already
///     cancelled, <c>EnsureModelAsync</c> always returns a cancelled task (awaiting it throws
///     <see cref="OperationCanceledException"/>); otherwise the token is ignored.
///     </para>
///     <para>
///     First use (approximate, machine dependent): the word lexicon of the core package loads on first use (about 55 ms,
///     about 15 MiB), at the latest in the first <c>ClassifyAsync</c>, and the statistical table (83 KiB, embedded) loads in about 22 ms
///     on the first classification of a non-empty list (the table is loaded whether or not the list contains an ambiguous word; an empty list does not load it). After that a sentence takes about 0.03 ms (30 µs),
///     about 2 µs per word (measured), and the model adds about 2 MiB of memory,
///     not counting the 15 MiB of the lexicon. That figure is the larger of the growth of the private memory of the process
///     (<c>Process.PrivateMemorySize64</c>) and of the managed heap (<c>GC.GetTotalMemory</c>), both after a full garbage
///     collection, from before the model was created to
///     after it classified warm-up sentences; it holds the decoded tables and working buffers of the model, so it is
///     not a count of the model's own data (the embedded
///     table is only 83 KiB). The model is safe for concurrent use.
///     </para>
///     <para>
///     Accuracy (build notes, https://github.com/demaconsulting/WordSense/blob/main/docs/build_notes/basic-evaluation.md and
///     https://github.com/demaconsulting/WordSense/blob/main/docs/build_notes/model-table.md): about 92% best-analysis
///     accuracy on the holdout and about 89% on real text, and about 92% and 90% for the part of speech alone. Basic is
///     enough when nothing may be downloaded, startup time and memory must stay small, or ONNX Runtime cannot be
///     shipped.
///     </para>
///     <para>
///     Tokens: split punctuation into elements of their own, one sentence per call is recommended. The model splits a
///     list into sentence segments at the tokens <c>"."</c>, <c>"!"</c> and <c>"?"</c>, tags each segment and uses the
///     five words on each side of the target as context; there is no length limit. Keep a contraction such as
///     <c>don't</c> or a hyphenated word as one token; it has no lexicon entry and is returned as unknown
///     (<see cref="ClassifyResult.IsKnown"/> is false, level <see cref="SenseLevel.None"/>, no analysis, <c>Details</c>
///     null, confidence 0), and a tokenizer that splits it into <c>do</c> and <c>n't</c> changes what the tagger sees.
///     </para>
///     <para>
///     Result: <see cref="ClassifyResult.Analyses"/> lists every analysis of the word (lemma, part of speech and
///     features) with its probability, the largest first, summing to one; <c>Details</c> is the first of them, the
///     selected analysis. The model scores analyses, not fine senses: the <see cref="AnalysisCandidate.Senses"/> of an
///     analysis list its one sense with probability 1 when its lemma and part of speech have exactly one sense, and are
///     empty otherwise. A consumer that wants the model's decision reads the summary (<c>Level</c>, <c>Confidence</c>,
///     <c>SenseId</c>); one that wants its own decision reads the analyses.
///     </para>
///     <para>
///     Answer levels: the Basic model answers at two of the four <see cref="SenseLevel"/> values for a known word. When
///     the selected analysis has a lemma and part of speech with exactly one sense, the word is answered at the
///     <see cref="SenseLevel.Sense"/> level with that sense (<c>SenseId</c>, and <c>GroupId</c> when the sense is in a
///     group); every other known word is answered at the <see cref="SenseLevel.PartOfSpeech"/> level with its analysis in
///     <c>Details</c> and no sense or group id, because the statistics cannot separate fine senses or groups of senses,
///     however low the confidence is; an unknown word is answered at the <see cref="SenseLevel.None"/> level. The model
///     never answers at the <see cref="SenseLevel.Group"/> level.
///     </para>
///     <para>
///     Confidence: 0 means the word is unknown to the lexicon (<c>Details</c> is null), 1 means the word has a single
///     lemma and part of speech in the lexicon, and a word with several of them gets the probability of the lemma
///     and part of speech of the selected analysis (the analyses that differ only in features, such as the singular and
///     the plural of <c>deer</c>, share it and never lower it), between
///     one over the number of lemmas and parts of speech and 1 (at the sense level it is that probability times the
///     probability 1 of the
///     one sense); <c>Details</c> is never null for a word that is in the lexicon (a number word such as
///     <c>one</c> included), so an ambiguous
///     word is never left undecided. This probability scale is specific to the Basic model: do not reuse a threshold
///     from, or for, MiniLM or BertBaseCased, whose confidence is a margin score. The value is a relative score, not a
///     calibrated probability, but higher values are more often right: on real text a threshold of 0.6 keeps about
///     90% of the target words at about 89% precision, 0.8 about 78% at about 93%, and 0.9 about 70% at about 96%
///     (holdout: 95% at 90%, 86% at 94%, 78% at 96%; the table is in
///     https://github.com/demaconsulting/WordSense/blob/main/docs/build_notes/basic-evaluation.md). The lowest value
///     a word with s lemmas and parts of speech can get is 1/s (0.5 for two), so a threshold at or below that keeps every answer
///     for such words; the recommended starting threshold for this model is 0.8.
///     </para>
///     <para>
///     Offline and mirror: Basic needs no files and no network, so the mirror, the model-directory and the
///     <c>WORDSENSE_MIRROR_*</c> environment settings of the encoder packages do not apply to it, and a
///     <c>MirrorOptions</c> passed to <c>EnsureModelAsync</c> is ignored. It does everything synchronously on the
///     calling thread (the returned task is already complete) and checks a cancellation token when the call starts
///     and between sentence segments.
///     </para>
///     <para>
///     Case: the lookup and the cue words are lower-cased, so <c>Record</c> and <c>record</c> are the same word. The
///     only use of case is that a word that is not in the lexicon, starts with an upper-case letter and is not the
///     first word of its sentence segment is tagged a noun (a likely proper noun). A period inside the list ends the
///     segment, so merge an abbreviation into one token yourself (<c>Mr.</c>; the reference tokenizer of the core
///     namespace documentation does not do that) and write a decimal as one token (<c>3.5</c>); see the
///     table of edge cases in the documentation of the <c>DemaConsulting.WordSense</c> namespace.
///     </para>
///     <para>
///     Which model to choose: Basic is the smallest and fastest and needs nothing else. The encoder packages
///     <c>DemaConsulting.WordSense.MiniLM</c> (22 MiB) and <c>DemaConsulting.WordSense.BertBaseCased</c> (104 MiB,
///     case-aware) score the fine senses and groups of senses of the 20 lemmas of the first reviewed batch and answer
///     every other ambiguous word through the Basic model. See the namespace documentation of
///     <c>DemaConsulting.WordSense</c> and the model table.
///     </para>
/// </remarks>
/// <example>
///     Quickstart: classify a sentence and read the part of speech of the ambiguous word.
///     <code>
///     using DemaConsulting.WordSense;
///     using DemaConsulting.WordSense.Basic;
///
///     using var model = new BasicWordSenseModel();
///     await model.EnsureModelAsync();
///
///     string[] words = ["They", "will", "record", "the", "record", "."];
///     ClassifyResult[] results = await model.ClassifyAsync(words);
///
///     for (var i = 0; i &lt; words.Length; i++)
///     {
///         Console.WriteLine($"{words[i]}: {results[i].Details?.PartOfSpeech.ToString() ?? "-"} ({results[i].Level}, {results[i].Confidence:0.00})");
///     }
///     </code>
/// </example>
internal static class NamespaceDoc;

namespace DemaConsulting.WordSense.BertBaseCased;

/// <summary>
///     The BERT base cased encoder for DemaConsulting.WordSense: chooses between the senses of an ambiguous English word
///     from its sentence with an int8 ONNX bert-base-cased model that keeps the case of the text.
/// </summary>
/// <remarks>
///     <para>
///     Package: the NuGet package <c>DemaConsulting.WordSense.BertBaseCased</c>, which targets <c>net8.0</c>,
///     <c>net9.0</c> and <c>net10.0</c>; it is built on <c>DemaConsulting.WordSense.Onnx</c> (the shared tokenizer, ONNX
///     encoder, verified downloader and abstract <c>OnnxWordSenseModel</c>, which <c>BertBaseCasedWordSenseModel</c>
///     extends) and depends on <c>DemaConsulting.WordSense</c> and ONNX Runtime. Install it with
///     <c>dotnet add package DemaConsulting.WordSense.BertBaseCased</c>. Two kinds of data are involved: the per-lemma
///     classifier weights trained for this model are embedded in the assembly (nothing to download), while the encoder
///     files (<c>model_int8.onnx</c> of 103.5 MiB, plus <c>vocab.txt</c>, <c>config.json</c> and
///     <c>tokenizer.json</c>; 104 MiB in total, exact sizes below) are downloaded
///     once on first use, verified against pinned SHA-256 digests and cached per user, or read from a directory you
///     supply, in which case no network access happens at all.
///     </para>
///     <para>
///     Start with <see cref="BertBaseCasedWordSenseModel"/>: create it, call <c>EnsureModelAsync</c>, then
///     <c>ClassifyAsync</c> (which also ensures the model lazily when a word needs it). The files are described in the repository at
///     https://github.com/demaconsulting/WordSense/tree/main/model/bert-base-cased-int8. The model identifier
///     (<c>ModelId</c>) is <c>bert-base-cased-int8</c>; it names the store folder and the folder on a mirror. The encoder is
///     the base model without its masked-language head, cut and quantized with
///     https://github.com/demaconsulting/WordSense/blob/main/tools/model-prep/make-bert-encoder.py.
///     </para>
///     <para>
///     Choose BertBaseCased for the most accurate model: 104 MiB, about 9 ms per sentence, about 99% best-sense
///     accuracy on the holdout and about 97% on real text (precision of the words it answers with a confidence of at
///     least 0.6: about 99%), and it keeps the case of the text
///     (https://github.com/demaconsulting/WordSense/blob/main/docs/build_notes/model-table.md; approximate and machine
///     dependent; the memory of about 150 MiB it adds does not include the 15 MiB of the word lexicon). Choose
///     <c>DemaConsulting.WordSense.MiniLM</c> (the default recommendation) for a small, fast
///     model: 22 MiB, about 2 ms per sentence, about 97% and 93%. The statistical Basic model
///     (<c>DemaConsulting.WordSense.Basic</c>, no download, about 85% on real text) is enough when nothing may be
///     downloaded.
///     </para>
///     <para>
///     First use (approximate, machine dependent): <c>EnsureModelAsync</c> hashes the four files once per model instance (the ONNX file is the
///     bulk; about 110 ms in total with a warm file cache); the first classification of an ambiguous word opens the ONNX session and loads the weights in about 0.5 s, and the word lexicon
///     of the core package loads in about 55 ms (about 15 MiB). Recommendation: dispose the model (with <c>using</c>)
///     when you no longer need it. Guarantee: disposing while calls are running is safe: the model is marked disposed
///     at once, a classification that already runs completes normally, and the ONNX session is released when the last
///     of them finishes; afterwards <c>EnsureModelAsync</c> (thrown from the call) and <c>ClassifyAsync</c>
///     (reported through the returned task; also when the token is already cancelled)
///     throw <see cref="ObjectDisposedException"/>. Disposing during a download cancels it (the pending
///     <c>EnsureModelAsync</c> throws <see cref="OperationCanceledException"/>), but the hashing of files that are
///     already in place is not interrupted: when no download is needed the pending ensure completes normally (the
///     model is disposed nevertheless). A <c>ClassifyAsync</c> that is still waiting for its lazy ensure ends with
///     <see cref="OperationCanceledException"/> or, when the ensure finished first and the session was not yet opened,
///     with <see cref="ObjectDisposedException"/>. Downloaded files are kept. The
///     model is safe for concurrent use: concurrent classifications share one ONNX session and run in parallel. It
///     runs on the CPU only. <c>EnsureModelAsync</c> is optional: <c>ClassifyAsync</c> ensures lazily (so it may throw
///     the ensure exceptions too), but you need it to fail early or to pass a <see cref="MirrorOptions"/>, which is
///     ignored while an ensure is already in flight. Cancelling the token of <c>EnsureModelAsync</c> cancels the wait
///     of that call only (the returned task is cancelled, also for an already cancelled token unless the ensure has
///     already completed, in which case the completed task is returned; nothing is thrown from
///     the call); the shared ensure keeps running even when no caller waits any more, and the next call joins it. The opening of the ONNX
///     session and the inference run synchronously on the thread that continues the call (not offloaded), and a
///     token is checked between the words, never inside one ONNX run.
///     </para>
///     <para>
///     Confidence: the confidence of a decided word is a monotonic mapping of the margin between the best and the
///     runner-up sense onto 0 to 1, relative to the abstain margin trained for the word (0.5 at that margin, clamped;
///     the evaluation counts a word as answered from 0.5 up, so 0.5 is the lowest meaningful value); it
///     is a ranking score, not a calibrated probability, so tune your threshold, starting at the recommended 0.8 for
///     this model, and never reuse a threshold from
///     another model (the Basic model reports a probability and MiniLM has its own margin scale). 1 means a
///     single-sense word and 0 an unknown word (<c>Details</c> is null); <c>Details</c> is also null for a word with
///     several senses whose lemma has no intact weights, which does not happen for the weights embedded in this
///     package. A low confidence still carries the best sense. Measured on the real-text sentences (approximate,
///     rounded): a threshold of at least 0.6 keeps about 85% of the target words at about 99% precision, at least 0.8
///     about 70% at about 99% (about one error in a hundred), and at least 0.9 about 64% at about 99% (holdout: 94%, 87% and 81% of the words at about
///     99.7%). Without a threshold the accuracy is the best-sense figure above; the full table is in
///     https://github.com/demaconsulting/WordSense/blob/main/docs/build_notes/model-comparison.md.
///     </para>
///     <para>
///     Mirrors: a <see cref="MirrorOptions"/> passed to <c>EnsureModelAsync</c> takes precedence over the
///     <c>WORDSENSE_MIRROR_*</c> variables; the mirror is tried before the public source for each file that is missing
///     or invalid and is not contacted when every file is already present. The mirror holds the files at
///     <c>{mirror}/{model-id}/{file}</c>, where the model id is the <c>ModelId</c> of the model, for example
///     <c>https://models.example.com/wordsense/bert-base-cased-int8/model_int8.onnx</c>. Downloaded files are kept in
///     <c>{StoreRoot}/bert-base-cased-int8/</c>; the default store root is
///     <c>%LOCALAPPDATA%\DemaConsulting\WordSense\models</c> on Windows and
///     <c>~/.local/share/DemaConsulting/WordSense/models</c> on Linux and macOS (beneath <c>$XDG_DATA_HOME</c> when that
///     variable is set); only <see cref="MirrorOptions.StoreRoot"/> relocates the store (no
///     environment variable does, and a <see cref="MirrorOptions"/> needs a mirror base URI; a relative store root is
///     resolved against the current directory of the process). Offline route through the store: copy all four files
///     into <c>{StoreRoot}/bert-base-cased-int8/</c> (or the default store); files that match their pinned SHA-256 are
///     kept and never requested, so nothing is downloaded when all are valid. Network notes: downloads use the default
///     .NET proxy behavior (system proxy; the <c>HTTP_PROXY</c>, <c>HTTPS_PROXY</c> and <c>NO_PROXY</c> variables on
///     Linux and macOS), with no proxy credentials of its own; allow-list your mirror host and
///     <c>media.githubusercontent.com</c> (the public source, which may redirect to further hosts).
///     </para>
///     <para>
///     Pinned files: all four files are verified by <c>EnsureModelAsync</c>, whether they come from the store, a mirror,
///     GitHub or a supplied directory. SHA-256 digests: <c>model_int8.onnx</c>:
///     <c>dcc10a647f1520c00e07fd2ee2b3ad383db94657bc97cba3e1915d27e98a962d</c> (108,477,896 bytes), <c>vocab.txt</c>:
///     <c>eeaa9875b23b04b4c54ef759d03db9d1ba1554838f8fb26c5d96fa551df93d02</c> (213,450 bytes), <c>config.json</c>:
///     <c>19aa23b85dfc4ce3e199a4ab06d51acd4b7bfc9ae9bae62b78823dd5dfb0c286</c> (639 bytes) and <c>tokenizer.json</c>:
///     <c>343989712a36cd8b253efeaf8baf6a08b9d2583f78e395e83832e8ee9f8d8ee1</c> (668,923 bytes). The runtime itself
///     reads only the ONNX file and <c>vocab.txt</c>; the other two are provenance files that must still match.
///     </para>
///     <para>
///     Offline and air-gapped use: fetch the four files from
///     <c>https://media.githubusercontent.com/media/demaconsulting/WordSense/main/model/bert-base-cased-int8/{file}</c>,
///     put them directly in one directory (no <c>{model-id}</c> subfolder) and pass it to the constructor. Calling
///     <c>EnsureModelAsync</c> is optional but recommended: it verifies the digests above up front without network
///     access (otherwise <c>ClassifyAsync</c> verifies lazily and may throw the same exceptions); a missing file
///     throws <see cref="FileNotFoundException"/> and a wrong digest <see cref="InvalidDataException"/>. For a corporate
///     mirror pass a
///     <see cref="MirrorOptions"/> to <c>EnsureModelAsync</c> or set <c>WORDSENSE_MIRROR_URL</c> (with the optional
///     <c>WORDSENSE_MIRROR_USER</c>, <c>WORDSENSE_MIRROR_PASSWORD</c> or <c>WORDSENSE_MIRROR_BEARER_TOKEN</c>); files are
///     requested at <c>{mirror}/bert-base-cased-int8/{file}</c>. A file that fails verification is never used. Variable
///     details: a blank variable counts as unset; a user without a password (or the reverse), or a user, password or
///     token without <c>WORDSENSE_MIRROR_URL</c>, throws <see cref="ArgumentException"/>; none of this applies, and
///     the variables are never read, when a model directory was supplied or a <see cref="MirrorOptions"/> is passed;
///     a <see cref="MirrorOptions.Timeout"/> that is zero, negative or longer than about 49 days throws
///     <see cref="ArgumentOutOfRangeException"/> (<c>Timeout.InfiniteTimeSpan</c> means no limit); a mirror URL that is
///     relative, not http or https, or has a query, a fragment or user-info throws <see cref="ArgumentException"/>.
///     </para>
///     <para>
///     Input: one sentence per call; split punctuation into tokens of their own; keep a contraction such as
///     <c>don't</c> or a hyphenated word as one token; the model is case-aware, so keep the case of the text. A
///     reference tokenizer and a table of edge cases are in the documentation of the core namespace
///     <c>DemaConsulting.WordSense</c>
///     (https://github.com/demaconsulting/WordSense/blob/main/src/DemaConsulting.WordSense/NamespaceDoc.cs). The
///     default maximum sequence length is 128 word pieces including the special tokens (8 to 512 can be set in the
///     constructor); a longer text is encoded per word from a window around it, so nothing fails but distant context
///     is lost.
///     </para>
/// </remarks>
/// <example>
///     Quickstart: classify a sentence and read the part of speech of the ambiguous word.
///     <code>
///     using DemaConsulting.WordSense;
///     using DemaConsulting.WordSense.BertBaseCased;
///
///     using var model = new BertBaseCasedWordSenseModel();
///     await model.EnsureModelAsync();
///
///     string[] words = ["They", "will", "record", "the", "record", "."];
///     WordSense[] senses = await model.ClassifyAsync(words);
///
///     for (var i = 0; i &lt; words.Length; i++)
///     {
///         Console.WriteLine($"{words[i]}: {senses[i].Details?.PartOfSpeech.ToString() ?? "-"} ({senses[i].Confidence:0.00})");
///     }
///     </code>
///     Download through a mirror (see the mirror notes above): pass the options to <c>EnsureModelAsync</c>.
///     <code>
///     using DemaConsulting.WordSense;
///     using DemaConsulting.WordSense.BertBaseCased;
///
///     var mirror = new MirrorOptions(new Uri("https://models.example.com/wordsense"));
///     using var mirrored = new BertBaseCasedWordSenseModel();
///     await mirrored.EnsureModelAsync(mirror);
///     var mirroredSenses = await mirrored.ClassifyAsync(["They", "will", "record", "the", "record", "."]);
///     </code>
///     Offline: the four files are in one flat directory, for example <c>/opt/models/bert</c>.
///     <code>
///     using DemaConsulting.WordSense.BertBaseCased;
///
///     using var offline = new BertBaseCasedWordSenseModel("/opt/models/bert");
///     await offline.EnsureModelAsync();   // optional: verifies all four SHA-256 digests now, no network
///     var offlineSenses = await offline.ClassifyAsync(["They", "will", "record", "the", "record", "."]);
///     </code>
/// </example>
internal static class NamespaceDoc;

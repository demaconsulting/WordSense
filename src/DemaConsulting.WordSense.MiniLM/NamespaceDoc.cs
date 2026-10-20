namespace DemaConsulting.WordSense.MiniLM;

/// <summary>
///     The MiniLM encoder for DemaConsulting.WordSense: an int8 ONNX all-MiniLM-L6-v2 model with its pinned files, a
///     verified downloader and a model that classifies the words of a sentence.
/// </summary>
/// <remarks>
///     <para>
///     Package: the NuGet package <c>DemaConsulting.WordSense.MiniLM</c>, which targets <c>net8.0</c>, <c>net9.0</c> and
///     <c>net10.0</c>; it is built on <c>DemaConsulting.WordSense.Onnx</c> (the shared tokenizer, ONNX encoder, verified
///     downloader and abstract <c>OnnxWordSenseModel</c>, which the encoder model inside <c>MiniLMWordSenseModel</c>
///     extends) and depends on
///     <c>DemaConsulting.WordSense</c>, <c>DemaConsulting.WordSense.Basic</c> and ONNX Runtime. Install it with
///     <c>dotnet add package DemaConsulting.WordSense.MiniLM</c>; no native or other setup is needed. Two kinds of
///     data are involved: the per-lemma classifier weights trained for the model are embedded in the assembly (nothing
///     to download), while the encoder files (<c>model_int8.onnx</c>, 21.9 MiB or 22,972,370
///     bytes, plus <c>vocab.txt</c>, 0.2 MiB or 231,508 bytes; 22 MiB in total) are downloaded once by
///     <c>EnsureModelAsync</c>,
///     verified against pinned SHA-256 digests and cached per user, or read from a directory you supply, in which
///     case no network access happens at all.
///     </para>
///     <para>
///     Start with <see cref="MiniLMWordSenseModel"/>: create it, call <c>EnsureModelAsync</c>, then <c>ClassifyAsync</c>.
///     <c>MiniLMWordSenseModel</c> composes its encoder model with the Basic model as the fallback (see
///     <c>FallbackWordSenseModel</c>). The encoder model scores the fine senses of the 20 lemmas that this version has
///     weights for and answers them at the sense, group or part-of-speech level, whichever its per-lemma thresholds
///     support; every other ambiguous word is answered by the Basic model at the part-of-speech level. A text that has no
///     trained lemma with several senses is classified without reading the model files, and a text that has one ensures
///     them on first use. The files are described in the repository at
///     https://github.com/demaconsulting/WordSense/tree/main/model/all-MiniLM-L6-v2-int8. The model identifier
///     (<c>ModelId</c>) is <c>all-MiniLM-L6-v2-int8</c>; it names the store folder and the folder on a mirror. The
///     model is uncased: its tokenizer lower-cases the text, so the case of your words never changes its answer.
///     </para>
///     <para>
///     Choose MiniLM for a small model, the default recommendation: 22 MiB download
///     (https://github.com/demaconsulting/WordSense/blob/main/docs/build_notes/model-table.md; the memory it adds
///     does not include the 15 MiB of the word lexicon).
///     Choose <c>DemaConsulting.WordSense.BertBaseCased</c> when case-awareness matters more than size:
///     104 MiB. The statistical Basic model
///     (<c>DemaConsulting.WordSense.Basic</c>, no download) is enough when nothing may be
///     downloaded. Latency, first-use time and memory are approximate and machine dependent.
///     </para>
///     <para>
///     First use (approximate, machine dependent): <c>EnsureModelAsync</c> hashes the two files once per model instance
///     (about 30 ms with a warm file cache), and the word lexicon of the core package loads in
///     about 55 ms (about 15 MiB). Recommendation: dispose the model (with <c>using</c>) when you no longer need it.
///     Guarantee: disposing while calls are running is safe: the model is marked disposed at once, a classification
///     that already runs completes normally, and the model's resources are released when the last of them finishes; afterwards
///     <c>EnsureModelAsync</c> (thrown from the call) and <c>ClassifyAsync</c> (reported through the returned task;
///     also when the token is already cancelled) throw
///     <see cref="ObjectDisposedException"/>. Disposing during a download cancels it (the pending
///     <c>EnsureModelAsync</c> throws <see cref="OperationCanceledException"/>), but the hashing of files that are
///     already in place is not interrupted: when no download is needed the pending ensure completes normally (the
///     model is disposed nevertheless). Downloaded files are kept. The model
///     is safe for concurrent use: concurrent classifications run in parallel. It runs on
///     the CPU only. <c>EnsureModelAsync</c> is optional for classification; you need it to fetch or verify the
///     model files, or to pass a <see cref="MirrorOptions"/>, which is
///     ignored while an ensure is already in flight. Cancelling the token of <c>EnsureModelAsync</c> cancels the wait
///     of that call only (the returned task is cancelled, also for an already cancelled token unless the ensure has
///     already completed, in which case the completed task is returned; nothing is thrown from
///     the call); the shared ensure keeps running even when no caller waits any more, and the next call joins it.
///     </para>
///     <para>
///     Results: <c>ClassifyAsync</c> returns one <c>ClassifyResult</c> per word, with the probability of every analysis
///     of the word. A word with one analysis and one sense is answered by the encoder model at the
///     <c>Sense</c> level with confidence 1; a trained lemma by the encoder model at the <c>Sense</c>, <c>Group</c> or
///     <c>PartOfSpeech</c> level with the probability of the answer at that level as the confidence, and its ranked
///     senses listed in every analysis; every other known word by the Basic model at the <c>PartOfSpeech</c>
///     level with the probability of its selected analysis as the confidence (not the scale of the encoder, so do not
///     tune a threshold on it for another model), without scored senses. An unknown word has no
///     <c>Details</c>, the <c>None</c> level and confidence 0. A low confidence still carries the best part of speech.
///     </para>
///     <para>
///     Mirrors: a <see cref="MirrorOptions"/> passed to <c>EnsureModelAsync</c> takes precedence over the
///     <c>WORDSENSE_MIRROR_*</c> variables; the mirror is tried before the public source for each file that is missing
///     or invalid and is not contacted when every file is already present. The mirror holds the files at
///     <c>{mirror}/{model-id}/{file}</c>, where the model id is the <c>ModelId</c> of the model, for example
///     <c>https://models.example.com/wordsense/all-MiniLM-L6-v2-int8/model_int8.onnx</c>. Downloaded files are kept in
///     <c>{StoreRoot}/all-MiniLM-L6-v2-int8/</c>; the default store root is
///     <c>%LOCALAPPDATA%\DemaConsulting\WordSense\models</c> on Windows and
///     <c>~/.local/share/DemaConsulting/WordSense/models</c> on Linux and macOS (beneath <c>$XDG_DATA_HOME</c> when that
///     variable is set); only <see cref="MirrorOptions.StoreRoot"/> relocates the store (no
///     environment variable does, and a <see cref="MirrorOptions"/> needs a mirror base URI; a relative store root is
///     resolved against the current directory of the process). Offline route through the store: copy both files into
///     <c>{StoreRoot}/all-MiniLM-L6-v2-int8/</c> (or the default store); files that match their pinned SHA-256 are kept
///     and never requested, so nothing is downloaded when both are valid. Network notes: downloads use the default
///     .NET proxy behavior (system proxy; the <c>HTTP_PROXY</c>, <c>HTTPS_PROXY</c> and <c>NO_PROXY</c> variables on
///     Linux and macOS), with no proxy credentials of its own; allow-list your mirror host and
///     <c>media.githubusercontent.com</c> (the public source, which may redirect to further hosts).
///     </para>
///     <para>
///     Offline and air-gapped use: fetch <c>model_int8.onnx</c> (SHA-256
///     <c>afdb6f1a0e45b715d0bb9b11772f032c399babd23bfc31fed1c170afc848bdb1</c>) and <c>vocab.txt</c> (SHA-256
///     <c>07eced375cec144d27c900241f3e339478dec958f92fddbc551f295c992038a3</c>) from
///     <c>https://media.githubusercontent.com/media/demaconsulting/WordSense/main/model/all-MiniLM-L6-v2-int8/{file}</c>,
///     put both directly in one directory (no <c>{model-id}</c> subfolder) and pass it to the constructor. Calling
///     <c>EnsureModelAsync</c> is optional but recommended: it verifies both files against these digests up front (no
///     network access happens); a
///     missing file throws <see cref="FileNotFoundException"/> and a wrong digest
///     <see cref="InvalidDataException"/>. For a corporate mirror pass a
///     <see cref="MirrorOptions"/> to <c>EnsureModelAsync</c> or set <c>WORDSENSE_MIRROR_URL</c> (with the optional
///     <c>WORDSENSE_MIRROR_USER</c>, <c>WORDSENSE_MIRROR_PASSWORD</c> or <c>WORDSENSE_MIRROR_BEARER_TOKEN</c>); files are
///     requested at <c>{mirror}/all-MiniLM-L6-v2-int8/{file}</c>. A file that fails verification is never used. Variable
///     details: a blank variable counts as unset; a user without a password (or the reverse), or a user, password or
///     token without <c>WORDSENSE_MIRROR_URL</c>, throws <see cref="ArgumentException"/>; none of this applies, and
///     the variables are never read, when a model directory was supplied or a <see cref="MirrorOptions"/> is passed;
///     a <see cref="MirrorOptions.Timeout"/> that is zero, negative or longer than about 49 days throws
///     <see cref="ArgumentOutOfRangeException"/> (<c>Timeout.InfiniteTimeSpan</c> means no limit); a mirror URL that is
///     relative, not http or https, or has a query, a fragment or user-info throws <see cref="ArgumentException"/>.
///     </para>
///     <para>
///     Input: one sentence per call; split punctuation into tokens of their own; keep a contraction such as
///     <c>don't</c> or a hyphenated word as one token. A reference tokenizer and a table of edge cases are in the
///     documentation of the core namespace <c>DemaConsulting.WordSense</c>
///     (https://github.com/demaconsulting/WordSense/blob/main/src/DemaConsulting.WordSense/NamespaceDoc.cs). The default maximum sequence length is 128 word pieces
///     including the special tokens (8 to 512 can be set in the constructor); a longer text is encoded per word from a
///     window around it, so nothing fails but distant context is lost.
///     </para>
/// </remarks>
/// <example>
///     Quickstart: classify a sentence and read the part of speech and level of each word.
///     <code>
///     using DemaConsulting.WordSense;
///     using DemaConsulting.WordSense.MiniLM;
///
///     using var model = new MiniLMWordSenseModel();
///     await model.EnsureModelAsync();
///
///     string[] words = ["They", "will", "record", "the", "record", "."];
///     ClassifyResult[] results = await model.ClassifyAsync(words);
///
///     for (var i = 0; i &lt; words.Length; i++)
///     {
///         Console.WriteLine($"{words[i]}: {results[i].Details?.PartOfSpeech.ToString() ?? "-"} {results[i].Level} ({results[i].Confidence:0.00})");
///     }
///     </code>
///     Download through a mirror (see the mirror notes above): pass the options to <c>EnsureModelAsync</c>.
///     <code>
///     using DemaConsulting.WordSense;
///     using DemaConsulting.WordSense.MiniLM;
///
///     var mirror = new MirrorOptions(new Uri("https://models.example.com/wordsense"));
///     using var mirrored = new MiniLMWordSenseModel();
///     await mirrored.EnsureModelAsync(mirror);
///     var mirroredSenses = await mirrored.ClassifyAsync(["They", "will", "record", "the", "record", "."]);
///     </code>
///     Offline: the two files are in one flat directory, for example <c>/opt/models/minilm</c>.
///     <code>
///     using DemaConsulting.WordSense.MiniLM;
///
///     using var offline = new MiniLMWordSenseModel("/opt/models/minilm");
///     await offline.EnsureModelAsync();   // optional: verifies SHA-256 now, no network
///     var offlineSenses = await offline.ClassifyAsync(["They", "will", "record", "the", "record", "."]);
///     </code>
/// </example>
internal static class NamespaceDoc;

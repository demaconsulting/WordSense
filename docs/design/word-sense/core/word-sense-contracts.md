### WordSenseContracts Unit Design

![Core Structure](CoreView.svg)

#### Purpose

`WordSenseContracts` defines the public types that callers and model packages share, so that a caller can swap one
model for another without code changes, and the composite `FallbackWordSenseModel` that combines two models.

#### Data Model

- `PartOfSpeech`: Noun, Pronoun, Verb, Adjective, Adverb, Preposition, Conjunction, Interjection, Determiner (the
  articles and possessives such as the, a, an, his, my) and Auxiliary (modal and primary auxiliaries such as will,
  can, is, has; always the last analysis of a word, after its content analyses). Every dictionary word has a lexicon
  entry.
- `WordDetails(Lemma, PartOfSpeech, Features)`: a sealed record that describes one analysis of a word. It carries no
  sense id. `Features` is the inflection as `Key=Value` pairs joined by `+` (for example `Person=3+Tense=Pres`), or an
  empty string. `TryGetFeature(name, out value)` returns the value of one named feature (for example `Tense`) without
  splitting the string; it returns false, with a null value, when the feature is absent or the name is empty, and
  throws for a null name.
- `SenseLevel`: an enum with the four answer levels in order of increasing precision: `None` (0, the word is unknown),
  `PartOfSpeech` (1), `Group` (2) and `Sense` (3). The levels nest: a part of speech (the lemma and part of speech of an
  analysis) has senses, and may have groups between the two. A group exists only where it was curated, as a set of at
  least two, but not all, senses of one lemma and part of speech that are closely related in meaning and easily
  confused; a model that is unsure between senses steps up to the next level that exists, their shared group or else the
  part of speech.
- `SenseCandidate(SenseId, Probability)`: a readonly record struct that names one fine sense and its probability from
  0 to 1 given the analysis it is listed in.
- `AnalysisCandidate(Details, Probability, Senses)`: a readonly record struct for one analysis of a word.
  `Details` is the `WordDetails` of the analysis; `Probability` is its probability among the analyses of the word, where
  the analyses that share a lemma and a part of speech (a slot, the unit that owns the senses) and differ only in
  features share the probability of the slot, split among them by the model's evidence or equally, so the probability of
  a slot is the sum over its analyses;
  `Senses` is an immutable array of `SenseCandidate`, the probability of each fine sense given the analysis (given its
  slot, the same for every variant of a slot), sorted by
  probability (ties by the ordinal order of the sense id), summing to one and never truncated. `Senses` is empty when
  the model did not score the senses of the analysis. A `default` candidate is valid (a placeholder `Details` without a
  lemma, empty `Senses`). Equality compares the senses item by item.
- `ClassifyResult(Word, IsKnown, Analyses, Details, Level, Confidence, GroupId, SenseId)`: a readonly record struct,
  the result for one word (the type is not named `WordSense`, which is the name of the namespace and the package). It
  has two parts:
  - `Analyses` is the single source of truth: an immutable array of `AnalysisCandidate`, the probability of every
    analysis (lemma, part of speech and features) of the word, sorted by the probability of the slot of the analysis,
    largest first, and inside a slot by the probability of the analysis, largest first (ties keep the
    order of the analyses of `WordLexicon.Lookup`), summing to one and never truncated; it is empty only for an
    unknown word.
  - The summary: `Word` is the input word as given; `IsKnown` tells whether the word is in the lexicon; `Details` is the
    selected analysis (the first analysis: the best variant of the most probable slot, stored in the result), or null for
    an unknown word; `Level` is the level
    at which the word was answered. `Level` is `None` only for an unknown word (`IsKnown` false, `Analyses` empty,
    `Confidence` 0); a known word that the model is unsure about is at `PartOfSpeech` with a low `Confidence`. The ids
    are present exactly where the level carries them: at `Sense` `SenseId` is set (the answered sense lies in the
    selected analysis) and `GroupId` is set only when the sense is in a group, at `Group` only `GroupId` (the group lies
    in the selected analysis), at `PartOfSpeech` and `None` neither. `Confidence` is from 0 to 1 and is the probability
    of the answer at its level, built on the probability of the slot of the selected analysis (the sum of the
    probabilities of its feature variants, so that the split among them never lowers it): at `PartOfSpeech` that
    probability; at `Group` that probability
    times the sum of the probabilities, within the slot, of the member senses of the group; at `Sense` that
    probability times the probability of the answered sense within the slot. A word with one lemma and part of speech
    and one sense is answered at `Sense` with confidence 1; a word with one lemma and part of speech and several senses
    that the model does not decide is answered at `PartOfSpeech`, also with confidence 1 (the slot has no alternative,
    so the level, not the confidence, tells whether a sense is known). Probabilities are comparable only within one
    result (not
    across models,
    words or levels).
  - A group answer always exposes its member senses: `GroupId` with `WordLexicon.TryGetGroup` gives the
    `SenseGroup` whose `SenseIds` are the members in inventory order, and the `Senses` of the selected analysis (when
    they were scored) let a consumer degrade the answer to the members. Group ids are versioned per data release and are
    not frozen (sense ids are frozen): a consumer treats a group id as opaque and saves it with `WordLexicon.Version`.
  - The type is immutable and built of plain properties, so it serializes without custom code. `Unknown(word)` builds
    the result of an unknown word with a shared empty array. The arrays may be shared between results (for an unknown
    word and for a word with a single analysis or a single sense), so a consumer must not rely on reference identity.
    `default(ClassifyResult)` is valid: the word is the empty string, the arrays are empty, `IsKnown` is false, the
    level is `None` and the confidence is 0. The equality is by value and compares the word, `IsKnown`, `Details`,
    `Level`, `Confidence`, the ids and the contents of `Analyses` (item by item, senses included); the hash code agrees.
  - `Key` is a computed `string?`, derived from `Word`, `IsKnown`, `Details`, `Level`, `SenseId` and `Analyses` only, so
    it works for every model (the models of third parties included) and changes nothing for the existing members. It is
    null for an unknown word, a word without selected analysis and a word without a letter; otherwise it is
    `WordKey.Format(spelling, lemma, part, features, label)`: the spelling is `BasicClassifier.Normalize(Word)` (the
    normalization of the lookup: lower case, the typographic apostrophe straightened), the lemma, part of speech and
    features are those of `Details`, and the label is, in this order, (a) the part after `#` of `SenseId` at the `Sense`
    level; (b) otherwise that of the most probable sense (ties by the ordinal order of the id) of the analysis of
    `Analyses` that equals `Details`, or of the first analysis with its lemma and part of speech, when it lists senses (the
    group level and the part-of-speech level of a trained lemma); (c) otherwise none. A group id is never used because
    group ids are not frozen. It does not appear in `Equals` or `GetHashCode`, which compare the stored values, and the
    `ToString` of the `ClassifyResult` record struct lists it like any property.
- `WordKey(Spelling, Lemma, PartOfSpeech, Features, Label)`: a sealed record that holds the parts of a key. `ToString()`
  writes `{spelling}|{lemma}/{Part}[/{Features}][#{label}]`: the features and their `/` are left out when empty, the label
  and its `#` when it is null. A scan of the committed data found lemmas and spellings that contain `/` (21 lemmas, 31
  spellings, for example `A/C`, `I/O device`, `read/write head`) and none that contains `|` or `#`, and the labels
  contain only lower-case letters, digits and `-`; so the separators are escaped by a rule that makes the text
  unambiguous for any input: a backslash is written before each `\`, `|`, `/` and `#` of a part and no other character is
  escaped. `TryParse(string?, out WordKey?)` reads the text back with the same rule and accepts only canonical text: all
  of spelling, lemma and part (a defined `PartOfSpeech` name, exactly as `ToString` writes it) present, features and label
  present only with a non-empty value, every separator escaped, no other escape; so `key.ToString()` equals the parsed
  text. `Format` and the label rule (`LabelOf`) are internal and shared with `WordLexicon.Keys`.
- `SenseInfo(SenseId, GroupId, Lemma, PartOfSpeech, SenseKey)`: a sealed record that describes one sense of
  the catalog. `SenseId` is `lemma#label`. `GroupId` is null when the sense is in no group (a sense is in at most one
  group). `SenseKey` is the OEWN sense key, or null for a project sense that OEWN does not provide.
- `SenseGroup(GroupId, Lemma, PartOfSpeech, SenseIds)`: a sealed record that describes a group of senses of
  one lemma and part of speech; `GroupId` is `lemma#group:label`, in a namespace of its own that can never equal a
  sense id; `SenseIds` is an immutable array in inventory order with at least two senses and never all the senses of
  its lemma and part of speech. Its equality is by value and compares `SenseIds` as a
  sequence (content and order), with a hash code that is consistent with it.
- `IWordSenseModel : IDisposable`: the classification contract only.
  `ClassifyAsync(IEnumerable<string> words, CancellationToken)` returns `Task<ClassifyResult[]>`
  with one result per input word in input order; empty input gives an empty array. The input is one running text whose
  words are joined by single spaces, with punctuation as separate elements. A model that needs files ensures them
  lazily on first need inside `ClassifyAsync` (the default and `WORDSENSE_MIRROR_*` environment settings; lexicon-only
  words never need the model; concurrent callers share one ensure; a failed ensure can be retried), and it is safe for
  concurrent use. The model owns the lifetime; `Dispose` is idempotent and thread-safe, cancels a pending ensure
  (`OperationCanceledException`), and `ClassifyAsync` throws `ObjectDisposedException` afterwards.
- `IDownloadableWordSenseModel : IWordSenseModel`: the capability of a model that needs files. It adds
  `Task EnsureModelAsync(DownloadOptions? options = null, CancellationToken cancellationToken = default)`. The explicit
  ensure is optional because `ClassifyAsync` ensures lazily; it controls when the download happens and lets a caller
  handle failures up front. It is idempotent, thread-safe (concurrent calls share one ensure) and retryable after a
  failure. An already cancelled token returns a cancelled task (the call itself does not throw); a disposed model
  throws `ObjectDisposedException` from the call. A caller that holds only an `IWordSenseModel` writes
  `if (model is IDownloadableWordSenseModel downloadable) { await downloadable.EnsureModelAsync(); }`; a caller that
  holds a concrete type (for example `MiniLMWordSenseModel`) calls `await model.EnsureModelAsync();` directly.
- `FallbackWordSenseModel(IWordSenseModel primary, IWordSenseModel fallback)`: a sealed public class that implements
  `IDownloadableWordSenseModel` by composing two models. It owns both: it stores them and disposes both with its own
  `Dispose`.
- `DownloadOptions(BaseUri, BearerToken, Credentials, StoreRoot, Timeout)`: a sealed record in its own file (formerly
  `MirrorOptions`; the `WORDSENSE_MIRROR_*` environment variable names are unchanged). The base URI is the
  mirror's root; the store root defaults to the per-user `DemaConsulting/WordSense/models` folder; the timeout is per
  file download and defaults to 5 minutes. The record validates nothing; the printed form masks the token and the
  credentials.
- `WordSenseException : Exception`: the abstract base of the typed failures of a model or of the lexicon, with
  protected constructors `()`, `(string)` and `(string, Exception)`. A caller catches it to handle every typed failure.
  The original exception is always the inner exception. Its three sealed subtypes have public constructors `()`,
  `(string)` and `(string, Exception)`:
  - `ModelUnavailableException`: the files of a model are missing and cannot be obtained.
  - `ModelIntegrityException`: a model file, an embedded table or the embedded word image is damaged or does not fit
    the rest of the package.
  - `DownloadConfigurationException`: the download options or the `WORDSENSE_MIRROR_*` environment settings are
    invalid. It is not an `ArgumentException` (the inheritance is single): a caller that formerly caught
    `ArgumentException` around `EnsureModelAsync` for a bad mirror catches this type instead, and the original
    `ArgumentException` or `ArgumentOutOfRangeException` is its inner exception.

  The wrapping happens at the public boundary (see the table in _Error Handling_), never inside the internal download
  units, which keep the platform exceptions as their internal vocabulary. The four types belong to this unit.

#### Key Methods

The contract of `ClassifyAsync` is the specification that every model implements: a null list throws
`ArgumentNullException`, a null element throws `ArgumentException`, a canceled token throws
`OperationCanceledException`; the returned array is parallel to the input; a word is looked up in lower case with
`WordLexicon.Lookup` semantics, so every model sees the same analyses; an unknown word is `ClassifyResult.Unknown`; a
word with one analysis and one sense is at `Sense` with confidence 1. A caller tests the `Level` of a result, not
whether an id is null: a result at the `PartOfSpeech` level is a valid answer that has no sense id by design.

`FallbackWordSenseModel` implements `IDownloadableWordSenseModel` as follows:

- `EnsureModelAsync(options, token)`: throws `ObjectDisposedException` from the call when disposed; otherwise starts
  the ensure of every member that is an `IDownloadableWordSenseModel`, with the same options and token (all are started
  before any failure is reported; a member that throws before it returns a task is treated like one that returns a
  faulted task, so the other member is always started too) and completes when all have completed (`Task.WhenAll`),
  so they are ensured concurrently; the first failure is the failure of the call and a member's typed exception
  reaches the caller unchanged. A member without the capability (for example the Basic model) is skipped. When no
  member is capable the call returns a completed task (`Task.FromCanceled` for an already cancelled token).
- `ClassifyAsync(words, token)`: an async method, so every exception below is reported through the returned task
  (`EnsureModelAsync` throws `ObjectDisposedException` from the call). It throws `ObjectDisposedException` when
  disposed, `ArgumentNullException` for a null list
  and `ArgumentException` for a null element. It copies the words to a list once and starts both models on the whole
  list (so both see the same sentence and context; the lazy ensure stays inside the encoder member), awaits both, and
  throws `InvalidDataException` when either result array is not parallel to the input (a contract violation of a
  member, not a model integrity failure). Then, for each word index, it takes the result of the primary model when the
  primary model scored senses for the word, else the result of the fallback model. The primary model scored senses when
  its level is `Group` or `Sense`, or when the analysis of its `Analyses` that equals the selected `Details` has a
  non-empty `Senses`; any other result (the word is unknown to the primary model, or it only chose between analyses)
  is replaced by the fallback result. The result of a word is the result of one model, never a mixture of the two
  (its analyses, selected analysis, level, confidence and ids belong together), with one refinement (`Refine`): for a
  slot of the primary result (lemma and part of speech) with two or more analyses whose probabilities are all equal
  within 1e-9 (the encoder has no evidence for the split of a slot, which shares its senses, so it gives the feature
  variants the same probability), the composite looks up the same analyses (equal `WordDetails`) in the fallback result
  and, when the fallback lists all of them and does not give them the same probability, gives them the relative
  proportions of the fallback with the total of the slot as the primary gave it (`p' = mass * f / sum f`). It sorts the
  slot again (largest first, a tie keeping the primary order) and takes the first analysis of the first slot as the
  selected analysis when that analysis changed. The level, confidence, group id and sense id, the senses of every analysis
  and the probability of every slot are untouched by construction; a slot that the primary result split unequally (a
  trained form chooser) is never touched, nor is a slot the fallback does not list or does not separate.
- `Dispose()`: idempotent and thread-safe (an atomic flag); disposes the primary model and then, even when that throws,
  the fallback model, each once.

A typical composition is an encoder model as the primary model and the Basic model as the fallback: the encoder scores
the fine senses of the lemmas it was trained for, and Basic chooses the analysis of every other ambiguous word.

#### Error Handling

The record types and the interfaces themselves declare no failure of their own: constructing a record, comparing it and
reading its properties never throw, and the interfaces only state the contract that the implementations follow. The one
method on a record that validates is `WordDetails.TryGetFeature`, which rejects a null name with
`ArgumentNullException` (a `Try` method that throws for a null argument, never for a missing feature, which returns
false). `FallbackWordSenseModel`
propagates every exception of either model (an `AggregateException` of the two tasks is unwrapped by `await`, so the
first exception is thrown, and a typed exception of a member reaches the caller unchanged) and reports
`ObjectDisposedException` and `InvalidDataException` as above. `DownloadOptions` does not validate its values; the
model package that consumes it rejects invalid combinations (see the DownloadMirror unit) and reports them as a
`DownloadConfigurationException`.

The typed exceptions are thrown at the public boundary of the units that own the failing resource; the original
exception is always the inner exception:

- `ModelUnavailableException` (the inner exception is a `FileNotFoundException`, a `DirectoryNotFoundException` or
  an `HttpRequestException`): thrown by `OnnxWordSenseModel.EnsureModelAsync` and by the lazy ensure inside
  `ClassifyAsync` (and so by the MiniLM, BertBaseCased and composite models) when the model directory is missing or
  lacks a file, or when every download source failed, including a source that served bytes with a wrong SHA-256
  (the next source is tried); and by `OnnxWordSenseModel.ClassifyAsync` when a verified model file is gone when it is
  opened.
- `ModelIntegrityException` (the inner exception is an `InvalidDataException`, an `OnnxRuntimeException` or, for an
  unusable vocabulary or model metadata, an `ArgumentException`, `InvalidOperationException`,
  `IndexOutOfRangeException` or `FormatException`): thrown by
  `OnnxWordSenseModel.EnsureModelAsync` when a file of a supplied model directory fails its pinned SHA-256; by
  `OnnxWordSenseModel.ClassifyAsync` when the embedded weights are missing, corrupt or not for this word image or
  dimension, when an ONNX file that passed its SHA-256 cannot be opened, or when the classifier finds the weights
  and the word image inconsistent; by `WordLexicon` when the embedded word image is missing or corrupt or the sense
  catalog is damaged; and by `BasicTables` (so by `BasicWordSenseModel.ClassifyAsync`, from the call, on first
  use)
  when the embedded statistics table is missing, corrupt or foreign.
- `DownloadConfigurationException` (the inner exception is an `ArgumentException` or an
  `ArgumentOutOfRangeException`): thrown by `OnnxWordSenseModel.EnsureModelAsync` and by the lazy ensure for a
  relative, non-http(s), query, fragment or user-info base URI, a token together with credentials, credentials over
  plain http to a non-loopback host, a timeout that is zero, negative or above about 49 days, an invalid store root
  path, and invalid `WORDSENSE_MIRROR_*` environment settings. It is not thrown when a model directory is supplied:
  the options and the environment are then ignored.

`IOException` and `UnauthorizedAccessException` of an unwritable store are not wrapped (the platform exception is kept).
`OperationCanceledException` (a canceled call, or disposal during a download), `ObjectDisposedException`,
`ArgumentNullException`, the `ArgumentException` of a null word element and the `ArgumentOutOfRangeException` of a
constructor range are unchanged.

#### Dependencies

The records, the options and the exceptions depend on nothing. `FallbackWordSenseModel` depends on the
`IWordSenseModel` and `IDownloadableWordSenseModel` contracts only (the models it is given), not on any model package.

#### Callers

Application code, all model packages (implementing the interfaces and producing `ClassifyResult` values and typed
exceptions), the MiniLM and BertBaseCased packages (which construct a `FallbackWordSenseModel` of their encoder model
and a Basic model), the `WordLexicon` and `BasicTables` units (which throw `ModelIntegrityException`) and the builder
(which checks for `IDownloadableWordSenseModel` and catches `WordSenseException`).

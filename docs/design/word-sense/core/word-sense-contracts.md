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
- `AnalysisCandidate(Details, Probability, Senses)` and `AnalysisCandidate(Details, Probability, Senses,
  SensesRankedByUsage)`: a readonly record struct for one analysis of a word.
  `Details` is the `WordDetails` of the analysis; `Probability` is its probability among the analyses of the word, where
  the analyses that share a lemma and a part of speech (a slot, the unit that owns the senses) and differ only in
  features share the probability of the slot, split among them by the model's evidence or equally, so the probability of
  a slot is the sum over its analyses;
  `Senses` is an immutable array of `SenseCandidate`, the probability of each fine sense given the analysis (given its
  slot, the same for every variant of a slot), sorted by
  probability (ties in the inventory order of the lemma, see `WordLexicon.SensesOf`), summing to one and never
  truncated. `Senses` is empty when the model did not rank the senses of the analysis: an encoder scores them for its
  trained lemmas, and the Basic model (and so every composed model, for a lemma the encoder has no weights for) ranks
  the senses of every lemma and part of speech that has two or more by the usage prior, which is a statement about the
  lemma and never an answer. `SensesRankedByUsage` (a read-only `bool`) tells the two rankings apart: it is true only
  when the model ranked the senses by the usage prior and did not score them from the context of the word (the Basic
  model for a lemma and part of speech with two or more senses); it is false when `Senses` is empty, when it lists the
  only sense of a lemma, when an encoder scored it, for a `default` candidate and for every candidate built with the
  three-argument constructor, so a model that lists senses and says nothing else counts as scoring them in context. It
  is a part of the equality. A `default` candidate is valid (a placeholder `Details` without a
  lemma, empty `Senses`). Equality compares the senses item by item and the flag.
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
    level; (b) otherwise that of the most probable sense (on equal probabilities the first listed one: the senses are listed
    in the order of the ranking, ties in the inventory order of the lemma) of the analysis of
    `Analyses` that equals `Details`, or of the first analysis with its lemma and part of speech, when it lists senses (the
    group level and the part-of-speech level of a trained lemma, and the part-of-speech level of every polysemous lemma
    through the usage prior of the Basic model); (c) otherwise none. A group id is never used because
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
  lazily on first need inside `ClassifyAsync` (the default and `WORDSENSE_MIRROR_*` environment settings; a text whose
  words need no context never needs the model, and an encoder model that uses a part-of-speech head needs it for the
  first text that has a word of several parts of speech; concurrent callers share one ensure; a failed ensure can be
  retried), and it is safe for
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
- `FallbackWordSenseModel(IWordSenseModel primary, IWordSenseModel fallback)` and
  `FallbackWordSenseModel(IWordSenseModel primary, IWordSenseModel fallback, double primaryWeight)`: a sealed public
  class that implements `IDownloadableWordSenseModel` by composing two models. It owns both: it stores them and
  disposes both with its own `Dispose`. The primary weight `w` (the read-only property `PrimaryWeight`) is a number
  from 0 to 1, 1 when it is not given; a value below 0, above 1 or not a number is rejected with
  `ArgumentOutOfRangeException`.
- `IPartOfSpeechEvidenceModel` (internal): the marker of a model whose slot probabilities for a word of two or more parts
  of speech are an estimate of the part of speech from the sentence, not only the mass of senses. Its one property,
  `ProvidesPartOfSpeechEvidence`, is true when the model has such evidence (for the encoder models, when a head is named
  and in use). `FallbackWordSenseModel` reads it from its primary model. The interface is internal, so a third-party
  model cannot claim the behavior, and a marked model that does not provide the evidence is composed as an unmarked one.
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
  non-empty `Senses` that are not only ranked by usage (`SensesRankedByUsage` is false); any other result (the word is
  unknown to the primary model, or it only chose between analyses, or the senses of its selected analysis are only the
  usage prior, as those of the Basic model) is replaced by the fallback result, so a composite whose primary model is the
  Basic model never refines or reconciles a ranking by usage as if it were a score. The fallback result that replaces
  the primary result passes through `GuardSingleSense`: when it is at the `Sense` level, its confidence is below the
  target 0.97 (the precision target that the encoder models are calibrated to on real text), it lists more than one slot
  (lemma and part of speech) and its selected analysis lists exactly one sense (the single-sense rule of the Basic model,
  which answers at the `Sense` level at any confidence), it is answered at the `PartOfSpeech` level instead, with the
  same analyses, selected analysis and confidence and no group or sense id; a word with a single slot (the probability
  of the slot is 1) and every other fallback result are used as they are, and the Basic model alone is not affected.
  The result of a word is the result
  of one model, never a mixture of the two
  (its analyses, selected analysis, level, confidence and ids belong together), with three refinements
  (`SplitClosedClass`, `Refine` and `Reconcile`, in this order) for a primary model without part-of-speech evidence and
  the reweighing described after them for a primary model with it. `SplitClosedClass`: a slot of the primary result
  whose part of speech is
  closed class (not a noun, verb, adjective or adverb; `SenseFusion.IsClosedClass`) has no probability of its own, since
  a sense classifier has no evidence for it. When the primary result lists closed-class slots without probability (the
  encoder does not score them) and open-class slots, and
  the fallback result lists any of them, the probability of every closed-class slot is the probability of the slot in the
  fallback result, normalized over the slots of the primary result (`f_k`), and every open-class slot gets
  `(1 − sum of f over the closed-class slots) × p_k / sum of p over the open-class slots`, so the primary result decides
  only inside the open-class part. The analyses of a closed-class slot are those of the fallback result with their
  senses; the analyses of an open-class slot keep their shares and senses. When an open-class slot is still the most
  probable, the result keeps its level, group id, sense id and selected analysis, and its confidence is scaled by the new
  probability of the slot. When a closed-class slot is the most probable, the result is at the `PartOfSpeech` level with
  the new probabilities, the first analysis of the most probable slot selected and no group or sense id (the composite
  decided a part of speech, even when the fallback names the only sense of the slot). A primary result
  without a closed-class slot or without an open-class slot, one that gives a closed-class slot a probability of its
  own (the Basic model as the primary model), a fallback result without analyses or without any of the
  slots, are left as they are. `Refine`: for a
  slot of the primary result (lemma and part of speech) with two or more analyses whose probabilities are all equal
  within 1e-9 (the encoder has no evidence for the split of a slot, which shares its senses, so it gives the feature
  variants the same probability), the composite looks up the same analyses (equal `WordDetails`) in the fallback result
  and, when the fallback lists all of them and does not give them the same probability, gives them the relative
  proportions of the fallback with the total of the slot as the primary gave it (`p' = mass * f / sum f`). It sorts the
  slot again (largest first, a tie keeping the primary order) and takes the first analysis of the first slot as the
  selected analysis when that analysis changed. The level, confidence, group id and sense id, the senses of every analysis
  and the probability of every slot are untouched by construction; a slot that the primary result split unequally (a
  trained form chooser) is never touched, nor is a slot the fallback does not list or does not separate.
  `Reconcile` (only when `w` < 1, the primary result is at the `PartOfSpeech` level, it has two or more slots over both
  results and the fallback result has analyses; every other result is returned as it is, so an answer at the sense or
  group level never changes): the encoder's slot probability is the mass of its senses, not a part-of-speech estimate,
  so a result whose senses it could not tell apart can have the wrong top slot. Each slot of the union of the slots
  of both results (the slots of the primary first, then the slots only the fallback lists, with the analyses of the
  fallback result as they are: their senses, which for the Basic model are the senses ranked by usage of a polysemous
  slot with `SensesRankedByUsage` true, or none for a slot without senses) gets the log score
  `w × ln(max(p_primary, 1e-6)) + (1 − w) × ln(max(p_fallback, 1e-6))`,
  where a slot's probability is the sum of its analyses and a slot a result lacks counts with the floor 1e-6; the scores
  are normalized with a softmax (the maximum subtracted) into the new slot probabilities. When SplitClosedClass was
  applied to the result (a primary result whose open-class slots have no probability at all, because a chooser label that
  stands for closed-class slots only took it, has no evidence for them: every slot takes the probability of the
  fallback result, the analyses keeping their senses and sharing the probability of their slot equally), the
  closed-class slots of the primary result carry the probability of the fallback result by
  construction (0 when the fallback gives the slot none or omits it) and keep it, a zero one too:
  the softmax runs over the other slots only, which share the probability that the closed-class slots leave
  ((1 − sum of the closed-class probabilities) × exp(score) / sum of exp(score)), so the closed-class slots end with
  exactly the probability of the fallback result (combining it again would count the fallback twice) and the open-class
  slots keep the encoder evidence combined with the fallback evidence. Inside a slot every
  analysis keeps its share of its own result (`p / slot mass`, equal shares when the mass is 0), its senses and the kind
  of its ranking (`SensesRankedByUsage`). The
  slots are listed by the new probability, the largest first (a tie keeping the order of the slots), and the analyses
  of a slot by their probability (a stable sort); the selected analysis is the first analysis, the level stays
  `PartOfSpeech`, the group and sense ids stay null, and the confidence is the probability of the first slot, as the
  contract of the confidence at that level says. The floor 1e-6 only keeps the logarithm defined; it is not tuned.
  The weight is a hyperparameter of each model package, tuned on development text only (see the MiniLM and
  BertBaseCased design).
- Reweighing for a primary model with part-of-speech evidence (`Reweigh`): when the primary model implements
  `IPartOfSpeechEvidenceModel` with `ProvidesPartOfSpeechEvidence` true, a word whose primary analyses have two or more
  distinct parts of speech and for which the fallback result has analyses is not split or reconciled; its slot
  probabilities are reweighed at every level of the answer. The marked primary has a probability for every slot of such
  a word, closed classes included, so `SplitClosedClass` and `Reconcile` are skipped. The base result is the primary
  result (with its tied variants refined by `Refine`) when the primary model scored senses, and otherwise the fallback
  result (the usage-ranked senses and the tense variants of Basic). The slots of the union of both results get
  the log score `w × ln(max(p_primary, 1e-6)) + (1 − w) × ln(max(p_fallback, 1e-6))` with the primary weight `w`, a
  softmax gives the probability of every slot, and the slots and the analyses inside them are listed as in `Reconcile`
  (every analysis keeps its share of the slot in its own result; a slot that the base result does not list takes the
  analyses of the other result). When the most probable slot is the slot that the base result answered at the `Sense` or
  `Group` level, the level, group id, sense id and selected analysis stay and the confidence is the new probability of the
  slot times the probability of the sense (or group) within it, that is, the base confidence divided by the base
  probability of the slot; when another slot is the most probable, or the base result was at the `PartOfSpeech` level,
  the result is at the `PartOfSpeech` level on the first analysis of the most probable slot with its probability as the
  confidence and no group or sense id. A base result taken from the fallback passes through `GuardSingleSense` on the
  reweighed result (0.97). A word of one part of speech is not reweighed: a scored primary result is only refined, and a
  primary result without scored senses is replaced by the guarded fallback result. A primary model without the marker, or
  with the marker and no evidence (MiniLM), keeps the rules above exactly. The tense of a verb stays the one of the
  fallback result (`Refine` and the variants of a fallback base result).
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
`IWordSenseModel` and `IDownloadableWordSenseModel` contracts and on the internal marker `IPartOfSpeechEvidenceModel`
only (the models it is given), not on any model package.

#### Callers

Application code, all model packages (implementing the interfaces and producing `ClassifyResult` values and typed
exceptions), the MiniLM and BertBaseCased packages (which construct a `FallbackWordSenseModel` of their encoder model
and a Basic model), the `WordLexicon` and `BasicTables` units (which throw `ModelIntegrityException`) and the builder
(which checks for `IDownloadableWordSenseModel` and catches `WordSenseException`).

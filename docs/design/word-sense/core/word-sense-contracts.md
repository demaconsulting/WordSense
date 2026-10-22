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
- `SenseInfo(SenseId, GroupId, Lemma, PartOfSpeech, SenseKey, Respelling)`: a sealed record that describes one sense of
  the catalog. `SenseId` is `lemma#label`. `GroupId` is null when the sense is in no group (a sense is in at most one
  group). `SenseKey` is the OEWN sense key, or null for a project sense that OEWN does not provide; `Respelling` is
  optional pronunciation data.
- `SenseGroup(GroupId, Lemma, PartOfSpeech, SenseIds, Respelling)`: a sealed record that describes a group of senses of
  one lemma and part of speech; `GroupId` is `lemma#group:label`, in a namespace of its own that can never equal a
  sense id; `SenseIds` is an immutable array in inventory order with at least two senses and never all the senses of
  its lemma and part of speech, and `Respelling` is optional. Its equality is by value and compares `SenseIds` as a
  sequence (content and order), with a hash code that is consistent with it.
- `IWordSenseModel : IDisposable`: `EnsureModelAsync(MirrorOptions? mirror, CancellationToken)` prepares the model
  (files, download). `ClassifyAsync(IEnumerable<string> words, CancellationToken)` returns `Task<ClassifyResult[]>`
  with one result per input word in input order; empty input gives an empty array. The input is one running text whose
  words are joined by single spaces, with punctuation as separate elements. `ClassifyAsync` ensures the model lazily on
  first need (the default and environment mirror settings; lexicon-only words never need the model; concurrent callers
  share one ensure; a failed ensure can be retried), so `EnsureModelAsync` is optional, and it is safe for concurrent
  use. The model owns the lifetime; `Dispose` is idempotent and thread-safe, cancels a pending ensure
  (`OperationCanceledException`), and `EnsureModelAsync` and `ClassifyAsync` throw `ObjectDisposedException`
  afterwards.
- `FallbackWordSenseModel(IWordSenseModel primary, IWordSenseModel fallback)`: a sealed public class that implements
  `IWordSenseModel` by composing two models. It owns both: it stores them and disposes both with its own `Dispose`.
- `MirrorOptions(BaseUri, BearerToken, Credentials, StoreRoot, Timeout)`: a sealed record. The base URI is the
  mirror's root; the store root defaults to the per-user `DemaConsulting/WordSense/models` folder; the timeout is per
  file download and defaults to 5 minutes. The printed form masks the token and the credentials.

#### Key Methods

The contract of `ClassifyAsync` is the specification that every model implements: a null list throws
`ArgumentNullException`, a null element throws `ArgumentException`, a canceled token throws
`OperationCanceledException`; the returned array is parallel to the input; a word is looked up in lower case with
`WordLexicon.Lookup` semantics, so every model sees the same analyses; an unknown word is `ClassifyResult.Unknown`; a
word with one analysis and one sense is at `Sense` with confidence 1. A caller tests the `Level` of a result, not
whether an id is null: a result at the `PartOfSpeech` level is a valid answer that has no sense id by design.

`FallbackWordSenseModel` implements `IWordSenseModel` as follows:

- `EnsureModelAsync(mirror, token)`: throws `ObjectDisposedException` from the call when disposed; otherwise starts
  the ensure of
  both models with the same mirror and token (a model that throws before it returns a task is treated like one that
  returns a faulted task, so the other model is always started too) and completes when both have completed
  (`Task.WhenAll`), so both are ensured concurrently; a failure of either is a failure of the call, reported after both
  ran.
- `ClassifyAsync(words, token)`: an async method, so every exception below is reported through the returned task
  (`EnsureModelAsync` throws `ObjectDisposedException` from the call). It throws `ObjectDisposedException` when
  disposed, `ArgumentNullException` for a null list
  and `ArgumentException` for a null element. It copies the words to a list once and starts both models on the whole
  list (so both see the same sentence and context), awaits both, and throws `InvalidDataException` when either result
  array is not parallel to the input. Then, for each word index, it takes the result of the primary model when the
  primary model scored senses for the word, else the result of the fallback model. The primary model scored senses when
  its level is `Group` or `Sense`, or when the analysis of its `Analyses` that equals the selected `Details` has a
  non-empty `Senses`; any other result (the word is unknown to the primary model, or it only chose between analyses)
  is replaced by the fallback result. The result of a word is always the complete result of one model, never a
  mixture (its analyses, selected analysis, level, confidence and ids belong together).
- `Dispose()`: idempotent and thread-safe (an atomic flag); disposes the primary model and then, even when that throws,
  the fallback model, each once.

A typical composition is an encoder model as the primary model and the Basic model as the fallback: the encoder scores
the fine senses of the lemmas it was trained for, and Basic chooses the analysis of every other ambiguous word.

#### Error Handling

The records and the interface throw nothing. `WordDetails.TryGetFeature` rejects a null name. `FallbackWordSenseModel`
propagates every exception of either model (an `AggregateException` of the two tasks is unwrapped by `await`, so the
first exception is thrown) and reports `ObjectDisposedException` and `InvalidDataException` as above. `MirrorOptions`
does not validate its values; the model package that consumes it rejects invalid combinations (see the DownloadMirror
unit).

#### Dependencies

The records depend on nothing. `FallbackWordSenseModel` depends on the `IWordSenseModel` contract only (the models it is
given), not on any model package.

#### Callers

Application code, all model packages (implementing the interfaces and producing `ClassifyResult` values), the MiniLM and
BertBaseCased packages (which construct a `FallbackWordSenseModel` of their encoder model and a Basic model) and the
builder.

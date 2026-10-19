### WordSenseContracts Unit Design

![Core Structure](CoreView.svg)

#### Purpose

`WordSenseContracts` defines the public types that callers and model packages share, so that a caller can swap one
model for another without code changes.

#### Data Model

- `PartOfSpeech`: Noun, Pronoun, Verb, Adjective, Adverb, Preposition, Conjunction, Interjection, Determiner (the
  articles and possessives such as the, a, an, his, my) and Auxiliary (modal and primary auxiliaries such as will,
  can, is, has; always the last analysis of a word, after its content analyses). Every dictionary word has a lexicon
  entry.
- `WordDetails(Lemma, PartOfSpeech, Features)`: a sealed record that describes one analysis of a word. It carries no
  sense id. `Features` is the inflection as `Key=Value` pairs joined by `+` (for example `Person=3+Tense=Pres`), or an
  empty string.
- `SenseLevel`: an enum with the four answer levels in order of increasing precision: `None` (0, the word is unknown),
  `PartOfSpeech` (1), `Group` (2) and `Sense` (3).
- `WordSense(Word, Details, Level, GroupId, SenseId, Confidence)`: a readonly record struct. `Word` is the input word
  as given; `Details` is the chosen analysis, or null when the level is `None`; `Level` is the level at which the word
  was answered. The ids are present exactly where the level carries them: at `Sense` both `SenseId` and `GroupId` are
  set, at `Group` only `GroupId`, at `PartOfSpeech` and `None` neither. `Confidence` is from 0 to 1 and refers to the
  answered level: 1 for a word with a single sense, 0 for an unknown word, and 0.5 exactly at the model's calibrated
  threshold for the level.
- `SenseInfo(SenseId, GroupId, Lemma, PartOfSpeech, SenseKey, Respelling)`: a sealed record that describes one sense of
  the catalog. `SenseKey` is the OEWN sense key, or null for a project sense that OEWN does not provide; `Respelling`
  is optional pronunciation data.
- `SenseGroup(GroupId, Lemma, PartOfSpeech, SenseIds, Respelling)`: a sealed record that describes a group of senses of
  one lemma and part of speech; `SenseIds` is an immutable array in inventory order and `Respelling` is optional.
- `IWordSenseModel : IDisposable`: `EnsureModelAsync(MirrorOptions? mirror, CancellationToken)` prepares the model
  (files, download). `ClassifyAsync(IEnumerable<string> words, CancellationToken)` returns `Task<WordSense[]>`
  parallel to the input; empty input gives an empty array. The input is one running text whose words are joined by
  single spaces, with punctuation as separate elements. `ClassifyAsync` ensures the model lazily on first need (the
  default and environment mirror settings; lexicon-only words never need the model; concurrent callers share one
  ensure; a failed ensure can be retried), so `EnsureModelAsync` is optional, and it is safe for concurrent use. The
  model owns the lifetime; `Dispose` is idempotent and thread-safe, cancels a pending ensure
  (`OperationCanceledException`), and `EnsureModelAsync` and `ClassifyAsync` throw `ObjectDisposedException`
  afterwards.
- `MirrorOptions(BaseUri, BearerToken, Credentials, StoreRoot, Timeout)`: a sealed record. The base URI is the
  mirror's root; the store root defaults to the per-user `DemaConsulting/WordSense/models` folder; the timeout is per
  file download and defaults to 5 minutes. The printed form masks the token and the credentials.

#### Key Methods

The unit has no algorithms. The contract of `ClassifyAsync` is the specification that every model implements: a null
list throws `ArgumentNullException`, a null element throws `ArgumentException`, a canceled token throws
`OperationCanceledException`. A caller tests the `Level` of a result, not whether an id is null: a result at the
`PartOfSpeech` level is a valid answer that has no sense id by design.

#### Error Handling

The records and interfaces throw nothing. `MirrorOptions` does not validate its values; the model package that
consumes it rejects invalid combinations (see the DownloadMirror unit).

#### Dependencies

None.

#### Callers

Application code, all model packages (implementing the interfaces and producing `WordSense` values) and the builder.

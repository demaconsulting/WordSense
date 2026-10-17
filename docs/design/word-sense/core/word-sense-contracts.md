### WordSenseContracts Unit Design

![Core Structure](CoreView.svg)

#### Purpose

`WordSenseContracts` defines the public types that callers and model packages share, so that a caller can swap one
model for another without code changes.

#### Data Model

- `PartOfSpeech`: Noun, Pronoun, Verb, Adjective, Adverb, Preposition, Conjunction, Interjection. Determiners have no
  value and no lexicon entry.
- `WordDetails(Lemma, PartOfSpeech, Meaning, Respelling)`: a sealed record. `Meaning` (lower-case ASCII letters,
  digits and hyphens) and `Respelling` (ASCII letters, hyphens and apostrophes) are non-null only for heteronyms.
- `WordSense(Word, Details, Confidence)`: a readonly record struct. `Word` is the input word as given; `Details` is
  the chosen sense or null when no sense could be chosen; `Confidence` is from 0 to 1 (1 when the word has a single
  sense, 0 when unknown or undecided).
- `IWordSenseClassifier.ClassifyAsync(IEnumerable<string> words, CancellationToken)`: returns `Task<WordSense[]>`
  parallel to the input; empty input gives an empty array. The input is one running text whose words are joined by
  single spaces, with punctuation as separate elements. A classifier is safe for concurrent use, is not disposable and
  only borrows the model: after the model is disposed every call throws `ObjectDisposedException`.
- `IWordSenseModel : IDisposable`: `EnsureModelAsync(MirrorOptions? mirror, CancellationToken)` prepares the model
  (files, download); `Create()` returns a classifier. `EnsureModelAsync` shall complete before `Create`. The model owns
  the lifetime; `Dispose` is idempotent and thread-safe, and `EnsureModelAsync` and `Create` throw
  `ObjectDisposedException` afterwards.
- `MirrorOptions(BaseUri, BearerToken, Credentials, StoreRoot, Timeout)`: a sealed record. The base URI is the
  mirror's root; the store root defaults to the per-user `DemaConsulting/WordSense/models` folder; the timeout is per
  file download and defaults to 5 minutes. The printed form masks the token and the credentials.

#### Key Methods

The unit has no algorithms. The contract of `ClassifyAsync` is the specification that every model implements: a null
list throws `ArgumentNullException`, a null element throws `ArgumentException`, a canceled token throws
`OperationCanceledException`.

#### Error Handling

The records and interfaces throw nothing. `MirrorOptions` does not validate its values; the model package that
consumes it rejects invalid combinations (see the DownloadMirror unit).

#### Dependencies

None.

#### Callers

Application code, all model packages (implementing the interfaces and producing `WordSense` values) and the builder.

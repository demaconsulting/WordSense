# System Design

This document provides the system-level design for WordSense, the library that identifies the sense of English words
in text: the lemma and part of speech of every word and, where a model can tell, the Open English WordNet (OEWN) fine
sense or the coarse group of senses that the word has in its sentence, with the level at which it was answered.

![WordSense Structure](WordSenseView.svg)

## Architecture

WordSense answers a word from its lexicon when the word has one sense, and answers a word with several senses (for
example "bank" as a financial institution or a river side) from its context with an interchangeable model. Senses come
from one catalog that every model shares: each fine sense has a readable id (`lemma#label`) and exactly one OEWN sense
key (or none, for a project sense that OEWN does not provide), and belongs to exactly one coarse group of the same
lemma and part of speech (`lemma#group-label`). An answer carries the level (`SenseLevel`) at which the model was
confident enough to answer: the fine sense, the group, or the part of speech alone; its confidence is relative to
that level. The system is delivered as five layered NuGet packages, each modeled as a subsystem, plus a non-shipped
build-time tool:

- **Core** (`DemaConsulting.WordSense`): the word lexicon (the analyses of every spelling), the sense catalog (senses,
  groups, lookups by id and by OEWN key) and the contracts. It has no vectors, weights, ONNX code or third-party
  dependency. See _Core Design_.
- **Basic** (`DemaConsulting.WordSense.Basic`): a model that chooses the analysis of an ambiguous word with a
  statistical part-of-speech tagger and context cues, answers at the part of speech level (at the sense level only
  where the lemma and part of speech have one sense), and needs no files, weights or network. See _Basic Design_.
- **Onnx** (`DemaConsulting.WordSense.Onnx`): the machinery shared by the encoder models: model file download and
  verification, the ONNX encoder and tokenizer, per-lemma weights and thresholds, and the classifier that scores the
  fine senses and chooses the answer level. Words that the weights do not cover are answered by the Basic model at
  the part of speech level. See _Onnx Design_.
- **MiniLM** (`DemaConsulting.WordSense.MiniLM`): the all-MiniLM-L6-v2 int8 encoder (uncased) with its embedded
  weights and thresholds. See _MiniLM Design_.
- **BertBaseCased** (`DemaConsulting.WordSense.BertBaseCased`): the bert-base-cased int8 encoder (cased) with its
  embedded weights and thresholds. See _BertBaseCased Design_.
- **WordSenseBuilder** (unit, `tools/WordSense.Builder`): builds the lexicon blob, trains the weights of each encoder
  model, chooses the thresholds and measures the models on independent text. It is never shipped. See _WordSenseBuilder
  Unit Design_.
- **WordData** (`data/` and `tools/WordSense.Builder/Data`): the TSV word data imported from Open English WordNet
  (CC BY 4.0) with the WordNet 3.0 sense counts, its schema, and the builder code that imports, labels and validates
  it. It is the source of the lexicon and the sense catalog that the builder compiles into the core package. See
  _WordData Subsystem Design_.

The layering is strict. Core references nothing. Basic references only core. Onnx references core and Basic (and ONNX
Runtime). MiniLM and BertBaseCased reference only Onnx and, through it, Basic and core. Model packages never reference
each other, so a consumer installs only the model it wants. The contracts in core are the seam between callers and
models: a caller programs against `IWordSenseModel` and can swap models without code changes.

## External Interfaces

- `IWordSenseModel.EnsureModelAsync` (in): optional `MirrorOptions` and a cancellation token. Idempotent; a no-op for
  the Basic model.
- `IWordSenseModel.ClassifyAsync` (in/out): tokenized words in, a parallel `WordSense[]` out (the word, the analysis
  `WordDetails` of lemma, part of speech and features, the `SenseLevel`, the group id, the sense id and the confidence
  of that level). The words are one running text with punctuation as its own elements. An encoder model ensures its
  files lazily (default and environment mirror settings) when some word needs the encoder.
- `WordLexicon.Lookup`, `TryLookup`, `Contains`, `FormsOf` (out): the analyses (`WordDetails`) of a word from the
  embedded word image.
- `WordLexicon.SensesOf`, `TryGetSense`, `TryGetSenseByKey`, `TryGetGroup` (out): the sense catalog from the embedded
  word image: the senses of a lemma and part of speech (most frequent first), a sense by its readable id or its OEWN
  sense key, and a group with its senses.
- Environment variables (in): `WORDSENSE_MIRROR_URL`, `WORDSENSE_MIRROR_USER`, `WORDSENSE_MIRROR_PASSWORD` and
  `WORDSENSE_MIRROR_BEARER_TOKEN`; used only when no mirror option is passed.
- Model store (in/out): `<store>/<model-id>/<file>` in a per-user directory unless `StoreRoot` is given.
- Mirror and GitHub LFS source (in): HTTP(S) `GET <base>/<model-id>/<file>`; SHA-256 verified, credentials sent to
  the mirror's own origin only.
- Builder command line (in/out): `compile-image`, `import-moby`, `weights`, `evaluate`, `evaluate-senses`,
  `evaluate-running`, `ud-counts`, `ud-irregular`, `coverage`, `train-basic`, `tune-basic`, `label-senses`,
  `import-wn30-counts`, `tiers` and `appendix`; build time only, never shipped. The two evaluation commands download
  their independent texts (SemCor and the Universal Dependencies English-EWT test file) to a cache after verifying
  their pinned checksums; the texts are never committed.

## Dependencies

The packages have these runtime dependencies:

- Core: none besides the .NET base class library.
- Basic: the core package only.
- Onnx: the core package, the Basic package (the fallback for words without weights) and the OTS item **ONNX Runtime**
  (see _OnnxRuntime Integration Design_ in the OTS design).
- MiniLM and BertBaseCased: the Onnx package.

The following OTS items are used to build and verify the system and are not consumed at runtime: **ApiMark**,
**BuildMark**, **FileAssert**, **Pandoc**, **ReqStream**, **ReviewMark**, **SarifMark**, **SonarMark**,
**SysML2Tools**, **VersionMark**, **WeasyPrint** and **xUnit**. Each has its own design document under the OTS
design.

## Risk Control Measures

N/A - WordSense is a text-analysis library with no safety-critical functionality requiring risk control measures
(IEC 62304 §5.3.3). Integrity of downloaded model files is protected by pinned SHA-256 digests, and the embedded
weights are bound to their lexicon by the hash of the sense, group and analysis inventory and by a per-lemma match of
the sense ids, so a stale weights blob is refused when it is loaded instead of producing a wrong answer. A word that
the weights do not cover is answered by the Basic model at the part of speech level, never with a guessed sense.

## Data Flow

1. The caller creates a model (`BasicWordSenseModel`, `MiniLMWordSenseModel` or `BertBaseCasedWordSenseModel`) and
   calls `EnsureModelAsync` (optional for words the lexicon alone resolves: `ClassifyAsync` ensures lazily with the
   default and environment mirror settings, and a model that is never needed is never downloaded). The Basic model
   returns immediately. An encoder model uses the files of its model
   directory when one was given, otherwise reuses valid files in the per-user store, otherwise downloads each missing
   file from the mirror (explicit option or environment variables) and then from the GitHub LFS copy, verifying every
   file against its pinned SHA-256.
2. The caller passes the tokenized words to `ClassifyAsync`. An encoder model loads its embedded weights, matches
   them to the lexicon and opens the ONNX session on first need; the session and weights are shared by all callers
   of the model.
3. The words are one running text joined by single spaces.
4. For every word the model lower-cases it and looks it up in `WordLexicon`, which returns the analyses of the word
   (lemma, part of speech and features), and then lists the fine senses of each analysis from the sense catalog:
   - no entry: the result is the word, no details, level `None`, confidence 0;
   - one candidate sense (one analysis whose lemma and part of speech have exactly one sense): the result is that
     analysis at level `Sense`, with the sense id, its group id and confidence 1, in every model;
   - several candidates: the model decides. Basic estimates the part of speech of every word of the sentence with a
     hidden Markov model, scores the analyses of the word from the neighboring words and picks the most probable
     analysis, so it always answers, at level `PartOfSpeech`, with the probability of the analysis. An encoder model
     runs the encoder once over the text (only when some word needs it), pools the hidden states of the word,
     centers and scales the vector, and scores the fine senses of the word with the lemma's weights. It answers at
     the finest level whose per-lemma threshold is met: the fine sense (level `Sense`), else the best group (level
     `Group`, confidence of at least one half), else the best group with a confidence below one half. A word whose
     lemma has no weights is answered by the Basic model at level `PartOfSpeech`.
5. The model returns a `WordSense[]` parallel to the input. The confidence of each result is relative to its own
   level and equals one half at the model's calibrated threshold for that level.

At build time the builder reads the data and the corpus and writes the lexicon blob embedded in core and the weights
blobs (with their thresholds) embedded in the encoder packages.

## Design Constraints

- **Platform**: the packages target net8.0, net9.0 and net10.0 and run on Windows, Linux and macOS. The encoder
  models run on the CPU through ONNX Runtime.
- **Offline use**: the lexicon and the Basic model need no network. An encoder model needs the network only to
  download its files once; supplying a model directory avoids all network access.
- **Thread safety**: the lexicon and the models are safe for concurrent use. `IWordSenseModel` is
  `IDisposable` and owns the lifetime (the ONNX session and the downloader state); dispose it when it is no longer
  needed (disposing while calls are running is safe, they complete), and disposal is idempotent and thread-safe.
  `EnsureModelAsync` and `ClassifyAsync`
  throw `ObjectDisposedException` after the model is disposed.
- **Security**: downloaded files are accepted only when they match their pinned SHA-256; mirror credentials are
  sent only to the mirror's own origin and are rejected for plain http except on a loopback host; redirects are
  followed manually so credentials never cross origins; credentials never appear in a printed option value.
- **Public surface**: the packages expose only the types needed by a caller: the contracts and `WordLexicon` in
  core, `BasicWordSenseModel`, the abstract `OnnxWordSenseModel`, `MiniLMWordSenseModel` and
  `BertBaseCasedWordSenseModel`. Everything else is internal and visible only to the builder and the test projects.
- **Language scope**: English text; words are supplied already tokenized by the caller.

### Deviations and Known Limitations

- There is no sentence-level normalization of capitalization or possessives beyond what the models do themselves: the
  Basic tagger uses a capitalized flag and a suffix table, and the encoder sees the text as
  given (the cased model keeps case, the uncased model lower-cases it).
- The weights format `WSX3` supports dense weights only, stored at reduced precision (16-bit floats); there is no
  sparse variant. Weights are keyed by sense id (`lemma#label`) and pinned to the word image by the hash of its sense,
  group and analysis inventory.
- Only the lemmas that have enough anchor sentences are trained; every other ambiguous word is answered at the part
  of speech level by the Basic model, and Basic itself never answers at the group level.
- The published accuracy figures are measured on SemCor and on running text, which the training never reads; the
  held-out sentences of the corpus come from the same generator as the anchors and are optimistic, so they are reported
  only as such.
- `MirrorOptions` is a sealed record; its validation happens when a model uses it (see _DownloadMirror Unit
  Design_), not in the record.
- `IWordSenseModel` is `IDisposable` and owns the lifetime; the Basic model's `Dispose` only marks the model as
  disposed. Disposing a model while a classification is running is safe for every model: the running call completes,
  new calls throw `ObjectDisposedException`, and an encoder model releases its ONNX session after the last running
  call.
- The `model_int8.onnx` file of `bert-base-cased-int8` is the derived encoder export, not the upstream masked
  language model export.

# System Design

This document provides the system-level design for WordSense, the library that tells the part of speech and, for
words pronounced differently by meaning, the pronunciation of English words in text.

![WordSense Structure](WordSenseView.svg)

## Architecture

WordSense answers a word from its lexicon when the word has one sense, and answers a word with several senses (for
example "record" as noun or verb) from its context with an interchangeable model. The system is delivered as five
layered NuGet packages, each modeled as a subsystem, plus a non-shipped build-time tool:

- **Core** (`DemaConsulting.WordSense`): the word lexicon and the contracts. It has no vectors, weights, ONNX code or
  third-party dependency. See _Core Design_.
- **Basic** (`DemaConsulting.WordSense.Basic`): a model that chooses the part of speech of an ambiguous word with
  hand-written rules and needs no files, weights or network. See _Basic Design_.
- **Onnx** (`DemaConsulting.WordSense.Onnx`): the machinery shared by the encoder models: model file download and
  verification, the ONNX encoder and tokenizer, per-lemma weights and the classifier that scores senses. See _Onnx
  Design_.
- **MiniLM** (`DemaConsulting.WordSense.MiniLM`): the all-MiniLM-L6-v2 int8 encoder (uncased) with its embedded
  weights. See _MiniLM Design_.
- **BertBaseCased** (`DemaConsulting.WordSense.BertBaseCased`): the bert-base-cased int8 encoder (cased) with its
  embedded weights. See _BertBaseCased Design_.
- **WordSenseBuilder** (unit, `tools/WordSense.Builder`): builds the lexicon blob, trains the weights of each encoder
  model and measures the models. It is never shipped. See _WordSenseBuilder Unit Design_.

The layering is strict. Core references nothing. Basic and Onnx reference only core. MiniLM and BertBaseCased
reference only Onnx and, through it, core. Model packages never reference each other, so a consumer installs only the
model it wants. The contracts in core are the seam between callers and models: a caller programs against
`IWordSenseModel` and `IWordSenseClassifier` and can swap models without code changes.

## External Interfaces

- `IWordSenseModel.EnsureModelAsync` (in): optional `MirrorOptions` and a cancellation token. Idempotent; a no-op for
  the Basic model.
- `IWordSenseModel.Create` (out): an `IWordSenseClassifier`. For encoder models it requires a completed
  `EnsureModelAsync`.
- `IWordSenseClassifier.ClassifyAsync` (in/out): tokenized words in, a parallel `WordSense[]` out. The words are one
  running text with punctuation as its own elements.
- `WordLexicon.Words` (out): read-only dictionary from lower-case word to `WordDetails[]`.
- Environment variables (in): `WORDSENSE_MIRROR_URL`, `WORDSENSE_MIRROR_USER`, `WORDSENSE_MIRROR_PASSWORD` and
  `WORDSENSE_MIRROR_BEARER_TOKEN`; used only when no mirror option is passed.
- Model store (in/out): `<store>/<model-id>/<file>` in a per-user directory unless `StoreRoot` is given.
- Mirror and GitHub LFS source (in): HTTP(S) `GET <base>/<model-id>/<file>`; SHA-256 verified, credentials sent to
  the mirror's own origin only.
- Builder command line (in/out): `words`, `weights`, `evaluate`, `basic-precision` and `appendix`; build time only,
  never shipped.

## Dependencies

The packages have these runtime dependencies:

- Core: none besides the .NET base class library.
- Basic: the core package only.
- Onnx: the core package and the OTS item **ONNX Runtime** (see _OnnxRuntime Integration Design_ in the OTS design).
- MiniLM and BertBaseCased: the Onnx package.

The following OTS items are used to build and verify the system and are not consumed at runtime: **ApiMark**,
**BuildMark**, **FileAssert**, **Pandoc**, **ReqStream**, **ReviewMark**, **SarifMark**, **SonarMark**,
**SysML2Tools**, **VersionMark**, **WeasyPrint** and **xUnit**. Each has its own design document under the OTS
design.

## Risk Control Measures

N/A - WordSense is a text-analysis library with no safety-critical functionality requiring risk control measures
(IEC 62304 §5.3.3). Integrity of downloaded model files is protected by pinned SHA-256 digests, and the embedded
weights are bound to their lexicon by an all-or-nothing match per lemma, so a stale weights blob never produces a
wrong answer.

## Data Flow

1. The caller creates a model (`BasicWordSenseModel`, `MiniLMWordSenseModel` or `BertBaseCasedWordSenseModel`) and
   calls `EnsureModelAsync`. The Basic model returns immediately. An encoder model uses the files of its model
   directory when one was given, otherwise reuses valid files in the per-user store, otherwise downloads each missing
   file from the mirror (explicit option or environment variables) and then from the GitHub LFS copy, verifying every
   file against its pinned SHA-256.
2. The caller calls `Create` to obtain an `IWordSenseClassifier`. An encoder model loads its embedded weights, matches
   them to the lexicon and opens the ONNX session on first use; the session and weights are shared by all classifiers
   of the model.
3. The caller passes the tokenized words to `ClassifyAsync`. The words are one running text joined by single spaces.
4. For every word the classifier lower-cases it and looks it up in `WordLexicon.Words`:
   - no entry: the result is the word, no details, confidence 0;
   - one entry: the word, that entry, confidence 1;
   - several entries: the model decides. Basic applies its rules to the neighboring words. An encoder model runs the
     encoder once over the text (only when some word needs it), pools the hidden states of the word, centers and
     scales the vector, scores the word's candidate senses with the lemma's weights and derives a confidence from the
     score margin.
5. The classifier returns a `WordSense[]` parallel to the input.

At build time the builder reads the corpus and writes the lexicon blob embedded in core and the weights blobs
embedded in the encoder packages.

## Design Constraints

- **Platform**: the packages target net8.0, net9.0 and net10.0 and run on Windows, Linux and macOS. The encoder
  models run on the CPU through ONNX Runtime.
- **Offline use**: the lexicon and the Basic model need no network. An encoder model needs the network only to
  download its files once; supplying a model directory avoids all network access.
- **Thread safety**: the lexicon, the models and the classifiers are safe for concurrent use. A model that holds a
  native session implements `IDisposable`; it is disposed once after all classification has finished.
- **Security**: downloaded files are accepted only when they match their pinned SHA-256; mirror credentials are
  sent only to the mirror's own origin and are rejected for plain http except on a loopback host; redirects are
  followed manually so credentials never cross origins; credentials never appear in a printed option value.
- **Public surface**: the packages expose only the types needed by a caller: the contracts and `WordLexicon` in
  core, `BasicWordSenseModel`, the abstract `OnnxWordSenseModel`, `MiniLMWordSenseModel` and
  `BertBaseCasedWordSenseModel`. Everything else is internal and visible only to the builder and the test projects.
- **Language scope**: English text; words are supplied already tokenized by the caller.

### Deviations and Known Limitations

- The capitalization and possessive heuristics of the earlier classifier were dropped; the encoder sees the text as
  given (the cased model keeps case, the uncased model lower-cases it).
- The weights format `WSX1` supports dense weights only; the sparse variant of the earlier design was removed.
- `MirrorOptions` is a sealed record; its validation happens when a model uses it (see _DownloadMirror Unit
  Design_), not in the record.
- `IWordSenseModel` is not `IDisposable`; the encoder models implement `IDisposable` themselves and the Basic model
  holds nothing to release.
- The `model_int8.onnx` file of `bert-base-cased-int8` is the derived encoder export, not the upstream masked
  language model export.

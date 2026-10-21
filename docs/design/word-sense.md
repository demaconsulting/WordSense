# System Design

This document provides the system-level design for WordSense, the library that identifies the sense of English words
in text: the lemma and part of speech of every word and, where a model can tell, the Open English WordNet (OEWN) fine
sense or the group of senses that the word has in its sentence, with the level at which it was answered, the ranked
analyses of the word and, where a model scored them, the ranked senses of each analysis.

![WordSense Structure](WordSenseView.svg)

## Architecture

WordSense answers a word from its lexicon when the word has one sense, and answers a word with several senses (for
example "bank" as a financial institution or a river side) from its context with an interchangeable model. Senses come
from one catalog that every model shares: each fine sense has a readable id (`lemma#label`, frozen: an id is never
reused for another meaning) and exactly one OEWN sense
key (or none, for a project sense that OEWN does not provide). A group exists only where it was curated: a set of at
least two, but not all, senses of the same lemma and part of speech that are closely related in meaning and easily
confused, so a sense is in at most one group and many lemmas have none. A group has an id in a namespace of its own,
`lemma#group:label`, that can never equal a sense id. Group ids are versioned per data release and are not frozen:
consumers treat them as opaque per-release values (a consumer that stores one saves the `WordLexicon.Version` of the
inventory with it), and the changes of the groups of a release (new, renamed, merged or dropped groups) are recorded
in the changelog and release notes. A group answer always exposes its member senses: `ClassifyResult.GroupId` with
`WordLexicon.TryGetGroup` gives the `SenseGroup` whose `SenseIds` are the members in inventory order, and the
`Senses` of the selected analysis (when scored) let a consumer degrade the answer to its members. The levels nest:
the part of speech (lemma and part of speech) above the optional group above the fine sense, and a model that is
unsure between senses steps up to the next level that exists, their shared group or else the part of speech.

An answer (`ClassifyResult`) has two parts. `Analyses` is the single source of truth: an immutable array with one
`AnalysisCandidate` for every analysis (lemma, part of speech and features) that the word can have in its context,
each with its probability (the probabilities sum to one over the word) and, where the model scored the fine senses of
the analysis, its `Senses` (a `SenseCandidate` list of sense id and probability given the analysis, summing to one).
Everything else is a summary of it: `Details` is the selected analysis (the best variant of the most probable slot, a
slot being a lemma and a part of speech: the senses belong to a slot, and the analyses of a slot that differ only in
features, such as `cut` as an infinitive and as a past tense, share its probability), `Level`
(`SenseLevel`) is the level at which the model was confident enough to answer (the fine sense, the group, or the part
of speech alone; none only for a word that is not in the lexicon), `Confidence` is the probability of the answer at
that level, and `GroupId` and `SenseId` name the answered group and sense. The system is delivered as five layered NuGet
packages, each modeled as a subsystem, plus a non-shipped build-time tool:

- **Core** (`DemaConsulting.WordSense`): the word lexicon (the analyses of every spelling), the sense catalog (senses,
  groups, lookups by id and by OEWN key, the version of the inventory), the contracts and the composite
  `FallbackWordSenseModel`. It has no vectors, weights, ONNX code or third-party dependency. See _Core Design_.
- **Basic** (`DemaConsulting.WordSense.Basic`): a model that chooses the analysis of an ambiguous word with a
  statistical part-of-speech tagger and context cues, answers at the part of speech level (at the sense level only
  where the selected analysis has one sense), and needs no files, weights or network. See _Basic Design_.
- **Onnx** (`DemaConsulting.WordSense.Onnx`): the machinery shared by the encoder models: model file download and
  verification, the ONNX encoder and tokenizer, per-lemma weights and thresholds, and the classifier that scores the
  fine senses and chooses the answer level. It references only the core package and is usable on its own; words whose
  senses it does not score are answered from the lexicon alone. See _Onnx Design_.
- **MiniLM** (`DemaConsulting.WordSense.MiniLM`): the all-MiniLM-L6-v2 int8 encoder (uncased) with its embedded
  weights and thresholds, composed with the Basic model. See _MiniLM Design_.
- **BertBaseCased** (`DemaConsulting.WordSense.BertBaseCased`): the bert-base-cased int8 encoder (cased) with its
  embedded weights and thresholds, composed with the Basic model. See _BertBaseCased Design_.
- **WordSenseBuilder** (unit, `tools/WordSense.Builder`): builds the lexicon blob, trains the weights of each encoder
  model, chooses the thresholds and measures the models on independent text. It is never shipped. See _WordSenseBuilder
  Unit Design_.
- **WordData** (`data/` and `tools/WordSense.Builder/Data`): the TSV word data imported from Open English WordNet
  (CC BY 4.0) with the WordNet 3.0 sense counts, its schema, and the builder code that imports, labels and validates
  it. It is the source of the lexicon and the sense catalog that the builder compiles into the core package. See
  _WordData Subsystem Design_.

The layering is strict. Core references nothing. Basic references only core. Onnx references only core (and ONNX
Runtime). MiniLM and BertBaseCased reference core, Onnx and Basic. The composition of an encoder model with the Basic
model is made in the model packages with the core `FallbackWordSenseModel`: the primary model (the encoder) answers a
word when it scored senses for it, and the fallback model (Basic) answers every other word with its complete result,
never a mixture of the two. Model packages never reference each other otherwise, so a consumer installs only the model
it wants. The contracts in core are the seam between callers and models: a caller programs against `IWordSenseModel`
and can swap models without code changes. The licenses follow the embedded data: the core package is licensed
`MIT AND CC-BY-4.0` and ships a `NOTICE.md` with the Open English WordNet 2025 (CC BY 4.0) attribution and the Princeton
WordNet 3.0 notice, and the Basic package is licensed `MIT` and ships a `NOTICE.md` that records that its statistics come
from our own CC0 corpora and from no treebank.

## External Interfaces

- `IWordSenseModel.EnsureModelAsync` (in): optional `MirrorOptions` and a cancellation token. Idempotent; a no-op for
  the Basic model.
- `IWordSenseModel.ClassifyAsync` (in/out): tokenized words in, a parallel `ClassifyResult[]` out, one result per word
  in input order (the word, `IsKnown`, the `Analyses` with their probabilities and the senses inside them, the
  selected analysis `Details` (`WordDetails` of lemma, part of speech and features), the `SenseLevel`, the confidence
  of that level and the group and sense ids). The words are one running text with punctuation as
  its own elements. An encoder model ensures its
  files lazily (default and environment mirror settings) when some word needs the encoder.
- `FallbackWordSenseModel` (in/out): a model that composes two `IWordSenseModel` instances (primary and fallback) and
  implements the interface itself.
- `WordLexicon.Lookup`, `TryLookup`, `Contains`, `FormsOf` (out): the analyses (`WordDetails`) of a word from the
  embedded word image.
- `WordLexicon.SensesOf`, `TryGetSense`, `TryGetSenseByKey`, `TryGetGroup` (out): the sense catalog from the embedded
  word image: the senses of a lemma and part of speech (most frequent first), a sense by its readable id or its OEWN
  sense key, and a group with its member senses.
- `WordLexicon.Version` (out): a string that identifies the sense inventory of the embedded image (the first 16
  lower-case hexadecimal digits of its inventory hash), to be compared for equality only.
- Environment variables (in): `WORDSENSE_MIRROR_URL`, `WORDSENSE_MIRROR_USER`, `WORDSENSE_MIRROR_PASSWORD` and
  `WORDSENSE_MIRROR_BEARER_TOKEN`; used only when no mirror option is passed.
- Model store (in/out): `<store>/<model-id>/<file>` in a per-user directory unless `StoreRoot` is given.
- Mirror and GitHub LFS source (in): HTTP(S) `GET <base>/<model-id>/<file>`; SHA-256 verified, credentials sent to
  the mirror's own origin only.
- Builder command line (in/out): `compile-image`, `import-moby`, `weights`, `evaluate`, `evaluate-senses`,
  `evaluate-running`, `open-counts`, `evaluate-tagger`, `coverage`, `train-basic`, `tune-basic`, `label-senses`,
  `import-wn30-counts`, `tiers` and `appendix`; build time only, never shipped. The evaluation commands that use an
  independent text (SemCor) download it to a cache after verifying its pinned checksum; the texts are never committed,
  and the Universal Dependencies English-EWT treebank is only ever an evaluation source in the same way.

## Dependencies

The packages have these runtime dependencies:

- Core: none besides the .NET base class library.
- Basic: the core package only.
- Onnx: the core package only, and the OTS item **ONNX Runtime** (see _OnnxRuntime Integration Design_ in the OTS
  design).
- MiniLM and BertBaseCased: the core, Onnx and Basic packages.

The following OTS items are used to build and verify the system and are not consumed at runtime: **ApiMark**,
**BuildMark**, **FileAssert**, **Pandoc**, **ReqStream**, **ReviewMark**, **SarifMark**, **SonarMark**,
**SysML2Tools**, **VersionMark**, **WeasyPrint** and **xUnit**. Each has its own design document under the OTS
design.

## Risk Control Measures

N/A - WordSense is a text-analysis library with no safety-critical functionality requiring risk control measures
(IEC 62304 §5.3.3). Integrity of downloaded model files is protected by pinned SHA-256 digests, and the embedded
weights are bound to their lexicon by the inventory hash of the word image and by a per-lemma match of
the sense ids, so a stale weights blob is refused when it is loaded instead of producing a wrong answer. A word whose
senses the encoder does not score is never answered with a guessed sense: the encoder model states the lexicon
analyses at the part of speech level, and the Basic model of a composition chooses among them.

## Data Flow

1. The caller creates a model (`BasicWordSenseModel`, `MiniLMWordSenseModel` or `BertBaseCasedWordSenseModel`) and
   calls `EnsureModelAsync` (optional for words the lexicon alone resolves: `ClassifyAsync` ensures lazily with the
   default and environment mirror settings, and a model that is never needed is never downloaded). The Basic model
   returns immediately. An encoder model uses the files of its model
   directory when one was given, otherwise reuses valid files in the per-user store, otherwise downloads each missing
   file from the mirror (explicit option or environment variables) and then from the GitHub LFS copy, verifying every
   file against its pinned SHA-256. A composite model ensures both of its models concurrently.
2. The caller passes the tokenized words to `ClassifyAsync`. An encoder model loads its embedded weights, matches
   them to the lexicon and opens the ONNX session on first need; the session and weights are shared by all callers
   of the model. A composite model classifies the whole text with both of its models.
3. The words are one running text joined by single spaces.
4. For every word the model lower-cases it and looks it up in `WordLexicon`, which returns the analyses of the word
   (lemma, part of speech and features), and then lists the fine senses of each analysis from the sense catalog:
   - no entry: the result is `ClassifyResult.Unknown`: the word, not known, no analyses, no details, level `None`,
     confidence 0;
   - one analysis whose lemma and part of speech have exactly one sense: the result has that analysis with
     probability 1 and that sense with probability 1 at level `Sense` with confidence 1 (no group id: a group never
     holds all the senses of its lemma and part of speech), in every model;
   - otherwise the model decides. Basic estimates the part of speech of every word of the sentence with a
     hidden Markov model, scores the analyses of the word from the neighboring words and fuses both into a probability
     for every analysis, so it always answers: `Analyses` lists every analysis with its probability (the probability of
     a slot is the sum over its feature variants, and the context cues alone split it among them), the selected
     analysis is the best variant of the most probable slot, and the level is `PartOfSpeech` with the probability of
     that slot as confidence (or `Sense`
     when the selected analysis has exactly one sense). The senses of an analysis are listed only when it has
     exactly one sense (probability 1). An encoder model
     runs the encoder once over the text (only when some word needs it), pools the hidden states of the word,
     centers and scales the vector, and scores the fine senses of the word with the lemma's weights. It lists every
     analysis with its probability and, in each, the senses with their probabilities given the analysis (softmax-
     normalized fine scores, summing to one, whatever the level), and answers at the finest level whose per-lemma
     threshold is met: the fine sense (level `Sense`, with its group id when the sense is in a group), else, for a
     lemma that has groups and a best sense in a group, the group (level `Group`, when the group threshold is met), else
     the part of speech (level `PartOfSpeech`, with the ranked senses still listed). The probabilities are worked
     out per slot: a slot has the mass of its senses, the probability of a sense given its slot is its renormalized
     mass, the feature variants of a slot split its probability (by the form chooser when it has a label for each,
     otherwise equally) and the split never reaches the level or the confidence. A word
     whose lemma has no weights, or for which the encoder gave no usable vector, is answered from the lexicon alone:
     the slots are equally likely (the variants of a slot split its share equally), the first is selected, no senses
     are scored, and the level is `PartOfSpeech`.
5. A composite model (`FallbackWordSenseModel`) answers each word with the complete result of its primary model when
   the primary model scored senses for it (the selected analysis lists scored senses, or the level is `Group` or
   `Sense`) and otherwise with the complete result of its fallback model. In the encoder packages the primary model is
   the encoder model and the fallback is the Basic model, so a word that the encoder does not score is answered by Basic
   at the part of speech level with the probabilities of its analyses. The composite adds no probability of its own: the
   slot rules above hold inside each of the two results.
6. The model returns a `ClassifyResult[]` parallel to the input. The confidence of each result is the probability of
   its answer at its own level (the probability of the slot of the selected analysis, times the probability of the
   answered sense, or the summed probability of the member senses of the answered group, within that slot); it is
   comparable only within one result.

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
- **Public surface**: the packages expose only the types needed by a caller: the contracts (including
  `ClassifyResult`, `AnalysisCandidate`, `SenseCandidate` and `FallbackWordSenseModel`) and `WordLexicon` in
  core, `BasicWordSenseModel`, the abstract `OnnxWordSenseModel`, `MiniLMWordSenseModel` and
  `BertBaseCasedWordSenseModel`. Everything else is internal and visible only to the builder and the test projects.
  The result types are immutable, expose plain properties (so they serialize without custom code) and have a valid
  default value.
- **Language scope**: English text; words are supplied already tokenized by the caller.

### Deviations and Known Limitations

- There is no sentence-level normalization of capitalization or possessives beyond what the models do themselves: the
  Basic tagger uses a capitalized flag and a suffix table, and the encoder sees the text as
  given (the cased model keeps case, the uncased model lower-cases it).
- The weights format `WSX3` supports dense weights only, stored at reduced precision (16-bit floats); there is no
  sparse variant. Weights are keyed by sense id (`lemma#label`) and pinned to the word image by its inventory hash.
- Only the lemmas that have enough anchor sentences are trained; every other ambiguous word is answered at the part
  of speech level by the Basic model of a composition, and Basic itself never answers at the group level.
- Group ids are not frozen: they are versioned per data release (sense ids are frozen). A consumer that stores a group
  id must treat it as opaque, save it with `WordLexicon.Version` and re-resolve it after a catalog change; the changes
  are recorded in the changelog and release notes.
- Probabilities are comparable only within one result: they are not calibrated across models, words or levels, so a
  consumer tunes any threshold per model and level on its own text.
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

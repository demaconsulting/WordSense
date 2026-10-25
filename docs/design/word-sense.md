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
that level, and `GroupId` and `SenseId` name the answered group and sense. The system is delivered as four layered NuGet
packages, each modeled as a subsystem, plus a non-shipped build-time tool:

- **Core** (`DemaConsulting.WordSense`): the word lexicon (the analyses of every spelling), the sense catalog (senses,
  groups, lookups by id and by OEWN key, the version of the inventory), the contracts, the composite
  `FallbackWordSenseModel` and the built-in statistical model `BasicWordSenseModel`, which chooses the analysis of an
  ambiguous word with a statistical part-of-speech tagger and context cues, answers at the part of speech level (at the
  sense level only where the selected analysis has one sense), and needs no files, weights or network. (The statistical
  model was the separate package `DemaConsulting.WordSense.Basic` before version 1.0; that package is retired.) The core
  package has no ONNX code, encoder weights or third-party dependency. See _Core Design_.
- **Onnx** (`DemaConsulting.WordSense.Onnx`): the machinery shared by the encoder models: model file download and
  verification, the ONNX encoder and tokenizer, per-lemma weights and thresholds, and the classifier that scores the
  fine senses and chooses the answer level. It references only the core package and is usable on its own; words whose
  senses it does not score are answered from the lexicon alone. See _Onnx Design_.
- **MiniLM** (`DemaConsulting.WordSense.MiniLM`): the all-MiniLM-L6-v2 int8 encoder (uncased) with its embedded
  weights and thresholds, composed with the core Basic model. See _MiniLM Design_.
- **BertBaseCased** (`DemaConsulting.WordSense.BertBaseCased`): the bert-base-cased int8 encoder (cased) with its
  embedded weights and thresholds, composed with the core Basic model. See _BertBaseCased Design_.
- **WordSenseBuilder** (unit, `tools/WordSense.Builder`): builds the lexicon blob, trains the weights of each encoder
  model, chooses the thresholds and measures the models on independent text. It is never shipped. See _WordSenseBuilder
  Unit Design_.
- **WordData** (`data/` and `tools/WordSense.Builder/Data`): the TSV word data imported from Open English WordNet
  (CC BY 4.0) with the WordNet 3.0 sense counts for the usage prior, its schema, and the builder code that imports,
  labels and validates
  it. It is the source of the lexicon and the sense catalog that the builder compiles into the core package. See
  _WordData Subsystem Design_.

The layering is strict. Core references nothing. Onnx references only core (and ONNX
Runtime). MiniLM and BertBaseCased reference core and Onnx. The composition of an encoder model with the Basic
model is made in the model packages with the core `FallbackWordSenseModel`: the primary model (the encoder) answers a
word when it scored senses for it, and the fallback model (Basic, from core) answers every other word with its complete
result, never a mixture of the two. Model packages never reference each other otherwise, so a consumer installs only
the model it wants. The contracts in core are the seam between callers and models: a caller programs against
`IWordSenseModel` and can swap models without code changes; a caller that wants to control when the files of a model
are downloaded tests for the optional `IDownloadableWordSenseModel`. The licenses follow the embedded data: the core
package is
licensed `MIT AND CC-BY-4.0` and ships a `NOTICE.md` that enumerates what it embeds: Open English WordNet 2025
(CC BY 4.0) with the Princeton WordNet notice that it requires, the Moby Part-of-Speech List (public domain), the
inflected forms authored by the project (CC0 1.0), the statistics of the built-in model (CC0 1.0, from our own corpora
and from no treebank) and the project data (MIT); it also names the tagged tokens of the open corpus (CC0 1.0) that
corroborate generated forms at build time. No CC BY-SA material is shipped anywhere. The WordNet 3.0 counts feed only
the usage prior of the encoder models: the MiniLM and BertBaseCased packages are also `MIT AND CC-BY-4.0`, because
their weights hold Open English WordNet sense ids, and their notices add the WordNet 3.0 notice of that prior.

**Keys and heteronyms.** A downstream library, for example a speech front end, attaches its own data to the meaning of
a word; WordSense names meanings and never carries phonemes. For that, every known word that a model answers has one
stable string key (`ClassifyResult.Key`, the type `WordKey`) that names the analysis and, where known, the sense;
`WordLexicon.Keys()` lists every key that any model can return, so the library can build its map in advance; and the
heteronyms (the spellings whose key, not the spelling, decides the word) are published as a table. The keys are part of
core (the contracts and the lexicon); the key file and the heteronym table are written by the builder, from the data and
the curated table `corpus/curation/heteronyms.tsv`. An unknown word has no key.

## External Interfaces

- `IDownloadableWordSenseModel.EnsureModelAsync` (in): optional `DownloadOptions` and a cancellation token. The
  explicit ensure is optional (`ClassifyAsync` ensures lazily); it controls when the download happens and lets the
  caller handle failures up front. Idempotent, thread-safe (concurrent calls share one ensure) and retryable after a
  failure. An already cancelled token returns a cancelled task; a disposed model throws `ObjectDisposedException`
  from the call. The Basic model does not implement it (it needs no files). A caller that holds only an
  `IWordSenseModel` tests the capability: `if (model is IDownloadableWordSenseModel downloadable)`.
- `IWordSenseModel.ClassifyAsync` (in/out): tokenized words in, a parallel `ClassifyResult[]` out, one result per word
  in input order (the word, `IsKnown`, the `Analyses` with their probabilities and the senses inside them, the
  selected analysis `Details` (`WordDetails` of lemma, part of speech and features), the `SenseLevel`, the confidence
  of that level and the group and sense ids). The words are one running text with punctuation as
  its own elements. An encoder model ensures its
  files lazily (default and environment download settings) when some word needs the encoder.
- `FallbackWordSenseModel` (in/out): a model that composes two `IWordSenseModel` instances (primary and fallback) and
  implements `IDownloadableWordSenseModel` itself: its ensure forwards to the members that have the capability.
- Typed exceptions (out): `ModelUnavailableException` (the model files are missing or cannot be downloaded),
  `ModelIntegrityException` (a model file, embedded table or word image is damaged or does not fit) and
  `DownloadConfigurationException` (the download options or `WORDSENSE_MIRROR_*` settings are invalid), all deriving
  from `WordSenseException`, with the original exception as the inner exception (see _WordSenseContracts Unit
  Design_).
- `WordLexicon.Lookup`, `TryLookup`, `Contains`, `FormsOf` (out): the analyses (`WordDetails`) of a word from the
  embedded word image.
- `WordLexicon.SensesOf`, `TryGetSense`, `TryGetSenseByKey`, `TryGetGroup` (out): the sense catalog from the embedded
  word image: the senses of a lemma and part of speech (in the order of Open English WordNet), a sense by its readable
  id or its OEWN
  sense key, and a group with its member senses.
- `WordLexicon.Version` (out): a string that identifies the sense inventory of the embedded image (the first 16
  lower-case hexadecimal digits of its inventory hash), to be compared for equality only.
- `ClassifyResult.Key` (out): the one stable string key of a known word that a model answered with a selected analysis
  (`{spelling}|{lemma}/{Part}[/{Features}][#{label}]`: the normalized spelling, the lemma, the part of speech, the
  features and, where the answer or the ranking names a sense, its label; never a group id and never a sound). It is
  computed from the fields of the result alone, so every model gives the same key, and it is null for an unknown word and
  for a result without a selected analysis.
- `WordKey` (in/out): the parts of a key as an immutable value; `ToString` writes the key (a backslash escapes a
  backslash, bar, slash or number sign inside a part) and `WordKey.TryParse` reads it back, accepting canonical text only.
- `WordLexicon.Keys()` (out): every key that any model can return, each once, in a fixed order, with deferred execution
  (one pass over the spellings at the first read, then streamed): the key of every analysis of every normalized
  spelling and one key for every catalog sense of its lemma and part of speech.
- Builder commands `keys --output FILE` and `heteronyms --output FILE` (out): the text file of all keys and the TSV of
  the heteronyms (the spellings whose key, not the spelling, decides the word) for a downstream library that attaches
  its own data (for example the phonemes of a speech front end; WordSense carries none) to the keys.
- Environment variables (in): `WORDSENSE_MIRROR_URL`, `WORDSENSE_MIRROR_USER`, `WORDSENSE_MIRROR_PASSWORD` and
  `WORDSENSE_MIRROR_BEARER_TOKEN`; used only when no download option is passed.
- Model store (in/out): `<store>/<model-id>/<file>` in a per-user directory unless `StoreRoot` is given.
- Mirror and GitHub LFS source (in): HTTP(S) `GET <base>/<model-id>/<file>`; SHA-256 verified, credentials sent to
  the mirror's own origin only.
- Builder command line (in/out): `compile-image`, `import-moby`, `weights`, `evaluate`, `evaluate-senses`,
  `evaluate-running`, `open-counts`, `evaluate-tagger`, `coverage`, `train-basic`, `tune-basic`, `label-senses`,
  `import-wn30-counts`, `tiers`, `appendix`, `keys` and `heteronyms`; build time only, never shipped. The evaluation
  commands that use an
  independent text (SemCor) download it to a cache after verifying its pinned checksum; the texts are never committed,
  and the Universal Dependencies English-EWT treebank is only ever an evaluation source in the same way.

## Dependencies

The packages have these runtime dependencies:

- Core: none besides the .NET base class library.
- Onnx: the core package only, and the OTS item **ONNX Runtime** (see _OnnxRuntime Integration Design_ in the OTS
  design).
- MiniLM and BertBaseCased: the core and Onnx packages.

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
   calls `EnsureModelAsync` when the model is an `IDownloadableWordSenseModel` (optional, and for words the lexicon
   alone resolves `ClassifyAsync` ensures lazily with the
   default and environment download settings, and a model that is never needed is never downloaded). The Basic model
   is not downloadable and needs no files. An encoder model uses the files of its model
   directory when one was given, otherwise reuses valid files in the per-user store, otherwise downloads each missing
   file from the mirror (explicit option or environment variables) and then from the GitHub LFS copy, verifying every
   file against its pinned SHA-256. A composite model ensures all of its downloadable members concurrently. A failure
   is reported as a `ModelUnavailableException` (no source could provide a file), a `ModelIntegrityException` (a
   supplied file does not match its digest) or a `DownloadConfigurationException` (invalid options or environment
   settings); the original exception is its inner exception.
2. The caller passes the tokenized words to `ClassifyAsync`. An encoder model loads its embedded weights, matches
   them to the lexicon and opens the ONNX session on first need; the session and weights are shared by all callers
   of the model; embedded weights that are missing, damaged or not for this lexicon are reported as a
   `ModelIntegrityException`. A composite model classifies the whole text with both of its models.
3. The words are one running text joined by single spaces.
4. For every word the model looks it up in `WordLexicon`, which returns the analyses of the word (lemma, part of speech
   and features): Basic asks for the lower-case form of the word, the encoder models for the word as written, and
   `WordLexicon.Lookup` itself tries the exact spelling first and the lower-case folding second. The model then lists
   the fine senses of each analysis from the sense catalog:
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
     are scored, and the level is `PartOfSpeech`. That lexicon-only result is what the Onnx classifier returns on its own;
     the MiniLM and BertBaseCased composites replace it by the answer of the Basic model for the word.
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
7. For each result the caller reads `ClassifyResult.Key`: the key is built from the result alone (`WordKey.KeyOf`): the
   normalized spelling of the word, the lemma, part of speech and features of the selected analysis, and a label (the
   answered sense at the sense level, otherwise the most probable ranked sense of the selected analysis, otherwise
   none). An unknown word and a result without a selected analysis have no key. The caller looks the key up in its own
   map; the keys of that map come from `WordLexicon.Keys()` (or the file that the builder command `keys` writes), which
   is built from the same analyses and catalog senses, so every key a model can return is in it, whatever the model.
   Where the spelling is a heteronym (the heteronym table, written by the builder command `heteronyms`), the key, not
   the spelling, tells the meanings apart.

At build time the builder reads the data and the corpus and writes the lexicon blob embedded in core and the weights
blobs (with their thresholds) embedded in the encoder packages. It also writes the key file and the heteronym table
(`keys`, `heteronyms`; the quality-checks job of the build workflow publishes both as the `word-keys` artifact) and the
senses appendix, which explains the keys and lists the heteronyms.

## Design Constraints

- **Platform**: the packages target net8.0, net9.0 and net10.0 and run on Windows, Linux and macOS. The encoder
  models run on the CPU through ONNX Runtime.
- **Offline use**: the lexicon and the Basic model need no network. An encoder model needs the network only to
  download its files once; supplying a model directory avoids all network access.
- **Thread safety**: the lexicon and the models are safe for concurrent use. `IWordSenseModel` is
  `IDisposable` and owns the lifetime (the ONNX session and the downloader state); dispose it when it is no longer
  needed (disposing while calls are running is safe, they complete), and disposal is idempotent and thread-safe.
  `EnsureModelAsync` (of a downloadable model) and `ClassifyAsync`
  throw `ObjectDisposedException` after the model is disposed.
- **Security**: downloaded files are accepted only when they match their pinned SHA-256; mirror credentials are
  sent only to the mirror's own origin and are rejected for plain http except on a loopback host; redirects are
  followed manually so credentials never cross origins; credentials never appear in a printed option value.
- **Public surface**: the packages expose only the types needed by a caller: the contracts (including
  `ClassifyResult`, `AnalysisCandidate`, `SenseCandidate`, `DownloadOptions`, the typed exceptions and
  `FallbackWordSenseModel`) and `WordLexicon` in
  core, `BasicWordSenseModel`, the abstract `OnnxWordSenseModel`, `MiniLMWordSenseModel` and
  `BertBaseCasedWordSenseModel`. Everything else is internal and visible only to the builder and the test projects.
  The result types are immutable, expose plain properties (so they serialize without custom code) and have a valid
  default value.
- **Language scope**: English text; words are supplied already tokenized by the caller.

### Deviations and Known Limitations

- There is no sentence-level normalization of capitalization or possessives beyond what the models do themselves: the
  Basic tagger uses a capitalized flag and a suffix table, the Basic classifier uses the written case to choose between
  a capitalized and a lower-case lemma of the same spelling (a capital at the start of a sentence, a capitalized word
  next to another capitalized word, an upper-case, single-letter or mixed-case word and the words of a title-case
  segment carry no such evidence), and the encoder sees the text as
  given (the cased model keeps case, the uncased model lower-cases it). A word that an encoder model scores itself (a
  trained lemma, or a form with a chooser) is not subject to that case rule: a form with capitalized and lower-case
  lemmas and no chooser is answered through the Basic model.
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
- `DownloadOptions` is a sealed record; its validation happens when a model uses it (see _DownloadMirror Unit
  Design_), not in the record, and an invalid value reaches the caller as a `DownloadConfigurationException` (which is
  not an `ArgumentException`).
- `IWordSenseModel` is `IDisposable` and owns the lifetime; the Basic model's `Dispose` only marks the model as
  disposed. Disposing a model while a classification is running is safe for every model: the running call completes,
  new calls throw `ObjectDisposedException`, and an encoder model releases its ONNX session after the last running
  call.
- The `model_int8.onnx` file of `bert-base-cased-int8` is the derived encoder export, not the upstream masked
  language model export.

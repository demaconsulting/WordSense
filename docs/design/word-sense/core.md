## Core Subsystem Design

![Core Structure](CoreView.svg)

### Overview

The core subsystem is the package `DemaConsulting.WordSense`. It defines the vocabulary every other package builds
on: the embedded word lexicon (the analyses of every known spelling), the sense catalog (every fine sense with its
readable id, OEWN sense key and, where curated, group) and the public model interface with the answer values. It also
holds the built-in statistical model, `BasicWordSenseModel`, so that the package answers every word in context by
itself, with no model download and no ONNX Runtime. (Before version 1.0 that model was the separate package
`DemaConsulting.WordSense.Basic`; it was merged into the core package, and the encoder packages compose it with their
encoder through `FallbackWordSenseModel`.) The subsystem contains no encoder weights, no ONNX code and no third-party
dependency, so a consumer that only needs to know which senses a word can have, or that writes its own model, installs
nothing else.

The statistical model looks every word up in the lexicon and chooses among the analyses (lemma, part of speech and
features) of an ambiguous word with two small statistical models whose tables are embedded in the assembly: a
second-order hidden Markov model (HMM) that estimates the part of speech of every word of a sentence, and a naive Bayes
cue model over the neighboring words that separates analyses that share a part of speech. A fusion step combines both
into one probability per analysis. The statistics carry no evidence that separates fine senses or groups of senses, so
the model answers an ambiguous word at the part of speech level and names a sense only where the selected analysis has
exactly one sense (its lemma and part of speech have exactly one sense). Every analysis of a known word is listed with
its probability. It is small and fast and less accurate than the encoder models; its measured accuracy is reported in
the repository's user guide (`docs/user_guide/accuracy.md` and the model comparison table
`docs/user_guide/model-table.md`). The tables are trained by the builder from the CC0 open part-of-speech corpus and the
CC0 sentence corpus of this repository.

The subsystem contains eight units:

- **WordLexicon**: the embedded lexicon of known words and the sense catalog, with its internal blob reader.
- **WordSenseContracts**: `IWordSenseModel`, `IDownloadableWordSenseModel`, the composite `FallbackWordSenseModel`,
  `DownloadOptions`, the typed exceptions (`WordSenseException`, `ModelUnavailableException`,
  `ModelIntegrityException` and `DownloadConfigurationException`),
  `ClassifyResult`, `AnalysisCandidate`, `SenseCandidate`, `SenseLevel`, `WordDetails`, `PartOfSpeech`, `SenseInfo` and
  `SenseGroup`.
- **BasicWordSenseModel**: the public statistical model; implements `IWordSenseModel` only (it needs no files and is
  not downloadable).
- **BasicClassifier**: the classifier the model creates; splits the text into sentence segments, decides the answer
  level of every word and drives the others.
- **BasicTables**: the immutable statistics read once from the embedded table `basic.bin`.
- **PosTagger**: the HMM that returns the part-of-speech posterior of every token of a sentence.
- **CueModel**: the extraction of the context features and the scoring of the analyses of one form.
- **SenseFusion**: the combination of the posterior and the cue scores into analysis probabilities.

`PosTag` (the tag constants) is a small helper of the tagger and the tables. Boundaries: apart from the statistical
model, the subsystem does not classify ambiguous words in context; that is the work of the encoder packages. It does
not download anything; `DownloadOptions` only carries the configuration that an encoder package interprets (the
statistical model is not downloadable and has no use for it). The composite `FallbackWordSenseModel` adds no knowledge
of its own: it only chooses,
word by word, between the results of the two models it is given (and lets the second model split the tied variants of
a slot of the first, without changing the probability of any slot), and forwards an explicit ensure to the members that
are downloadable.

### Interfaces

The subsystem exposes:

- `WordLexicon`: the lookup of a word in the embedded word image (`Lookup`, `TryLookup`, `Contains`, `FormsOf`), the
  sense catalog (`SensesOf`, `TryGetSense`, `TryGetSenseByKey`, `TryGetGroup`) and `Version`, a string that identifies
  the sense inventory of the image;
- `IWordSenseModel`: the classification contract that models implement, `IDownloadableWordSenseModel`, its optional
  extension with `EnsureModelAsync` for models that need files, `FallbackWordSenseModel`, a composite of two models
  (downloadable itself), and `BasicWordSenseModel`, the built-in statistical model, which has a public parameterless
  constructor. The Basic model is not downloadable; `ClassifyAsync` classifies at once, so no ensure is needed;
- `ClassifyResult`, `AnalysisCandidate`, `SenseCandidate`, `SenseLevel`, `WordDetails` and `PartOfSpeech`: the answer
  values;
- `SenseInfo` and `SenseGroup`: the records of the catalog;
- `DownloadOptions`: the download configuration passed to `EnsureModelAsync`;
- `WordSenseException`, `ModelUnavailableException`, `ModelIntegrityException` and `DownloadConfigurationException`:
  the typed failures. In the core, `WordLexicon` and the embedded statistics table report a missing or damaged
  embedded resource as a `ModelIntegrityException`.

It consumes the embedded resources `wordsense.img` and `basic.bin` and nothing else. The internals (the image reader
and the six statistical classes) are visible to the builder (which trains and writes the table with the same feature
hash and tag constants) and to the core test project only.

The MiniLM and BertBaseCased packages reference this package and compose `BasicWordSenseModel` with their encoder
model through `FallbackWordSenseModel`: an ambiguous word that the encoder does not score is answered with the part of
speech level answer of the statistical model. The Onnx package depends on the core package only, and does not use the
statistical model.

### Design

#### Lexicon and Catalog

`WordLexicon` opens `wordsense.img` lazily and exactly once through `WordImage`, which reads the image in place (no
dictionary is built). A lookup decodes only the analyses of the word asked for; the analysis sections are decoded when
the image is opened, and the catalog sections (the sense ids, the OEWN key suffixes, the group tables, the
lemma and part-of-speech blocks and the key index) are decoded the first time a catalog method needs them, so a
caller that only classifies words pays nothing for the 128 thousand senses of the catalog until a model asks for the
senses of a lemma. The contracts unit has no behavior of its own beyond the value semantics of its records; it depends
on `WordLexicon` only through the documentation of the classifier contract, which defines the lookup of every word as
the common first step of every model.

The two kinds of question are kept apart. `Lookup` answers a morphological question (which analyses can this spelling
have) and never expands an analysis into its senses; `SensesOf` answers a semantic question (which senses does this
lemma and part of speech have, in the order of Open English WordNet). A model combines them: it looks the spelling up,
then asks for
the senses of each analysis.

The catalog gives group ids a namespace of their own. A sense id is `lemma#label` and is frozen; a group id is
`lemma#group:label`, so it can never equal a sense id, and the image reader and the builder reject a group id without
the `#group:` marker. Group ids are versioned per data release and are not frozen. `WordLexicon.Version` is the first
16 lower-case hexadecimal digits of the inventory hash of the image (the hash that the encoder weights and the
statistical table are bound to), so it changes whenever an analysis, a sense or a group changes; consumers compare it
for equality only, and a consumer that stores group ids saves the version with them. A group answer always leads to
its members: `ClassifyResult.GroupId` with `TryGetGroup` gives `SenseGroup.SenseIds` in inventory order.

Every known word that a model answers also has one stable key, `ClassifyResult.Key` (the format
`{spelling}|{lemma}/{Part}[/{Features}][#{label}]`, the type `WordKey`), and an unknown or unanswered word has none
(null): the key names the analysis of the answer and, where known, its sense, and it is computed from the fields of the
result alone, so it is the same for every model, those of third parties included. `WordLexicon.Keys()` enumerates every
key that any model can return (a guaranteed superset: for every normalized spelling and analysis the key without a label
and one key for each catalog sense of the lemma and part of speech; the key of a result and the enumeration are built
from the same analyses and catalog senses, so the guarantee holds by construction), so a consumer can build a complete
map from key to its own data; WordSense itself never carries phonemes. The details are in the `WordSenseContracts` and
`WordLexicon` unit documents.

The lexicon, the contracts and the statistical model do not call each other beyond the lexicon lookups: a model looks
up the analyses of a word, asks for their senses, and returns `ClassifyResult` values built from the
`AnalysisCandidate` list of the word (analyses with their probabilities and, where scored, their senses), the selected
`WordDetails`, a `SenseLevel`, the confidence at that level and the ids of the sense or group that it chose.

#### Classification by the Statistical Model

`BasicWordSenseModel.ClassifyAsync` delegates to a lazily created `BasicClassifier` that shares the process-wide
`BasicTables.Default`, which is loaded on first use. The classifier normalizes each word (lower case, typographic
apostrophe straightened; tokens without a letter become empty), splits the list into segments that end after a `.`,
`!` or `?` token, and processes each segment independently:

1. every token becomes a tagger token: a token without a letter is fixed to the punctuation tag (the other tag when it
   contains a digit); a word carries the set of parts of speech of its lexicon analyses and whether it is capitalized;
2. `PosTagger` computes the tag posterior of every token of the segment;
3. for each word, in order, the lexicon analyses of the lower-case form decide the answer and its level; the written case
   then weighs down the analyses
   whose lemma has the other case (see the unit design):
   - no analysis (or an empty key): `ClassifyResult.Unknown`: not known, no analyses, no details, no group or sense id,
     level none, confidence 0;
   - exactly one analysis: it is listed with probability 1 (a shared array, cached per word, so the common words do not
     allocate it again). With exactly one sense for its lemma and part of speech, that sense is listed with probability
     1 and the answer is at the sense level with that sense (no group, normally), confidence 1; with several senses the
     answer is at the part of speech level, confidence 1;
   - several analyses: `CueModel` extracts the features of the word from the segment and scores the analyses against the
     cue table of the word or, failing that, of the lemma of its first analysis (the column of an analysis is the column
     of its lemma and part of speech whose feature items are all contained in the analysis features, the one with the
     most items winning; a column with empty features stands for any features),
     `SenseFusion` turns the posterior and the cue scores into one probability per analysis, and every analysis is
     listed with its probability. The senses belong to a lemma and a part of speech (a slot), not to the analyses of a
     slot that differ only in features (`cut` as an infinitive and as a past tense, `deer` as a singular and a plural),
     so the slots are listed by the sum of the probabilities of their analyses, largest first, and the analyses of a
     slot by their own probability (a stable sort, so a tie keeps the lexicon order). The first is
     the selected analysis: the best variant of the most probable slot. The answer is at the part of speech level with
     the probability of the slot of the selected analysis as
     its confidence, or at the sense level (same confidence) when the selected analysis has exactly one sense.

Every word that has a lexicon analysis is answered with details, number words such as `one` or `twenty` included; only
a word without analyses and a token without a letter are unknown. The model never answers at the group level, and it
lists no senses for an analysis whose lemma and part of speech have several senses (it scores analyses and not fine
senses), so it never names a sense for such an analysis, because its statistics do not separate the senses. An
analysis whose lemma and part of speech have exactly one sense lists that sense with probability 1, whatever the
probability of the analysis, because no evidence is needed to choose it.

An ambiguous word is therefore always answered, and its confidence is the probability of the slot of the selected
analysis (the sum over its feature variants, which the split among them never lowers), at
least one over the number of lemmas and parts of speech of the word and at most 1. The confidence is a relative
score, not a calibrated probability,
but a higher value is more often right: the builder's `evaluate basic` reports the accuracy per confidence bucket and
the coverage and precision at fixed thresholds, so a caller can trade coverage for precision with a threshold. Ties go
to the analysis that comes first in the lexicon order of the form.

#### Statistical Table

`basic.bin` is a Brotli-compressed container written only by the builder (`train-basic`) and read only by
`BasicTables`. After decompression it holds, in this order: the format magic; the 32-byte hash of the analysis
inventory of the word image it was trained for; seven little-endian doubles (gamma, beta, lambda 1 to 3, the tagger
weight of the fusion and the inverse temperature of the cues); the tag count; the unigram, bigram and trigram tag
counts (the sentence boundary is the last index); the tabled words with their closed-class mask and per-tag counts; the
suffix table; and the cue tables, keyed by training lemma (16-bit feature hashes and one signed byte weight per feature
and analysis, in steps of 1/8 nat). The cue analyses are named by lemma, part of speech and features and resolved
against the analysis inventory of the word image at load: a hash mismatch or an unknown or duplicate analysis is a load
failure, so the table is rebuilt whenever the inventory changes. Words, suffixes and lemmas are in ordinal order, so
equal training data gives byte-identical tables. The tag counts come from the representative part of the open corpus
(`corpus/open`), the cue tables from the fine-sense anchors and the chooser anchors of `corpus/`. The reader validates
the layout and throws `InvalidDataException` for anything malformed (the embedded default table maps that to a
`ModelIntegrityException`). The format (magic `WSB4`: the header with the weight of the tense model, and after the cue
tables the tense section with the shared past/non-past table) is described in detail
in the `BasicTables` unit design.

#### Determinism and Concurrency

The result of the statistical model depends only on the input words and the embedded table. The classifier and the
tables hold no mutable state, the scratch buffers are rented from the shared array pool for the duration of one call,
and the lazy loading of the table is thread-safe, so one classifier (or many) may be used from any number of threads.
The subsystem performs no file or network access and has no ONNX dependency.

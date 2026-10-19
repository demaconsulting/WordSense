## Basic Subsystem Design

![Basic Structure](BasicView.svg)

### Overview

The Basic subsystem is the package `DemaConsulting.WordSense.Basic`. It is a word-sense model for callers that want
no model files, no download and no ONNX Runtime. It looks every word up in the core lexicon and chooses among the
analyses (lemma, part of speech and features) of an ambiguous word with two small statistical models whose tables are
embedded in the assembly: a second-order hidden Markov model (HMM) that estimates the part of speech of every word of
a sentence, and a naive Bayes cue model over the neighboring words that separates analyses that share a part of speech.
A fusion step combines both into one probability per analysis. The statistics carry no evidence that separates fine
senses or groups of senses, so the model answers an ambiguous word at the part of speech level and names a sense only
where the lemma and part of speech of the word have exactly one sense. It is small and fast and less accurate than the
encoder models; its measured accuracy is reported in the repository's build notes.

The subsystem contains six units:

- **BasicWordSenseModel**: the only public type; implements `IWordSenseModel`.
- **BasicClassifier**: the classifier the model creates; splits the text into sentence segments, decides the answer
  level of every word and drives the others.
- **BasicTables**: the immutable statistics read once from the embedded table `basic.bin`.
- **PosTagger**: the HMM that returns the part-of-speech posterior of every token of a sentence.
- **CueModel**: the extraction of the context features and the scoring of the analyses of one form.
- **SenseFusion**: the combination of the posterior and the cue scores into analysis probabilities.

`PosTag` (the tag constants) is a small helper of the tagger and the tables. The subsystem references the core package
only and uses only the `WordLexicon` lookups (`Lookup` for the analyses of a form, `SensesOf` for the senses of a
lemma and part of speech) and the analysis inventory of the word image.

### Interfaces

It exposes `BasicWordSenseModel` with a public parameterless constructor. `EnsureModelAsync` returns immediately and
ignores the mirror; `ClassifyAsync` classifies at once, so a lazy ensure is trivial. It consumes the `WordLexicon`
lookups and the contracts of the core package (`WordSense`, `WordDetails` and `SenseLevel`). All other types are
internal and visible to the builder (which trains and writes the table with the same feature hash and tag constants)
and to the Basic test project.

The Onnx package references this package and uses `BasicWordSenseModel` as the statistical fallback: an ambiguous word
without trained weights is answered with the part of speech level answer of this model. The Basic package itself
depends on the core package only.

### Design

#### Classification

`BasicWordSenseModel.ClassifyAsync` delegates to a lazily created `BasicClassifier` that shares the process-wide
`BasicTables.Default`, which is loaded on first use. The classifier normalizes each word (lower case, typographic
apostrophe straightened; tokens without a letter become empty), splits the list into segments that end after a `.`,
`!` or `?` token, and processes each segment independently:

1. every token becomes a tagger token: a token without a letter is fixed to the punctuation tag (the other tag when it
   contains a digit); a word carries the set of parts of speech of its lexicon analyses and whether it is capitalized;
2. `PosTagger` computes the tag posterior of every token of the segment;
3. for each word, in order, the lexicon analyses of the normalized form decide the answer and its level:
   - no analysis (or an empty key): level none, no details, no group or sense id, confidence 0;
   - exactly one analysis whose lemma and part of speech have exactly one sense: level sense with that sense and its
     group, confidence 1;
   - exactly one analysis with several senses: level part of speech with that analysis, confidence 1;
   - several analyses: `CueModel` extracts the features of the word from the segment and scores the analyses,
     `SenseFusion` turns the posterior and the cue scores into one probability per analysis, and the result is the
     most probable analysis at the part of speech level with that probability as its confidence.

Every word that has a lexicon analysis is answered with details, number words such as `one` or `twenty` included; only
a word without analyses and a token without a letter are unknown. The model never answers at the group level, and it
never names a sense for a word whose lemma and part of speech have several senses, because its statistics do not
separate them.

An ambiguous word is therefore always answered, and its confidence is the probability of the chosen analysis, at least
one over the number of analyses and at most 1. The confidence is a relative score, not a calibrated probability, but a
higher value is more often right: the builder's `evaluate basic` reports the accuracy per confidence bucket and the
coverage and precision at fixed thresholds, so a caller can trade coverage for precision with a threshold. Ties go to
the analysis that comes first in the lexicon order of the form.

#### Statistical Table

`basic.bin` is a Brotli-compressed container written only by the builder (`train-basic`) and read only by
`BasicTables`. After decompression it holds, in this order: the format magic; the 32-byte hash of the analysis
inventory of the word image it was trained for; seven little-endian doubles (gamma, beta, lambda 1 to 3, the tagger
weight of the fusion and the inverse temperature of the cues); the tag count; the unigram, bigram and trigram tag
counts (the sentence boundary is the last index); the tabled words with their closed-class mask and per-tag counts; the
suffix table; and the cue tables of the ambiguous lemmas (16-bit feature hashes and one signed byte weight per feature
and analysis, in steps of 1/8 nat). The cue analyses are named by lemma, part of speech and features and resolved
against the analysis inventory of the word image at load: a hash mismatch or an unknown or duplicate analysis is a load
failure. Words, suffixes and lemmas are in ordinal order, so equal training data gives byte-identical tables. The
builder rolls the analysis labels of its training sentences up from the fine-sense corpus. The reader validates the
layout and throws `InvalidDataException` for anything malformed. The format is described in detail in the
`BasicTables` unit design.

#### Determinism and Concurrency

The result depends only on the input words and the embedded table. The classifier and the tables hold no mutable
state, the scratch buffers are rented from the shared array pool for the duration of one call, and the lazy loading of
the table is thread-safe, so one classifier (or many) may be used from any number of threads. The subsystem performs no
file or network access and has no ONNX dependency.

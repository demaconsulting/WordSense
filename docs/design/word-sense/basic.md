## Basic Subsystem Design

![Basic Structure](BasicView.svg)

### Overview

The Basic subsystem is the package `DemaConsulting.WordSense.Basic`. It is a word-sense model for callers that want
no model files, no download and no ONNX Runtime. It looks every word up in the core lexicon and chooses among the
senses of an ambiguous word with two small statistical models whose tables are embedded in the assembly: a
second-order hidden Markov model (HMM) that estimates the part of speech of every word of a sentence, and a naive Bayes
cue model over the neighboring words that separates senses that share a part of speech. A fusion step combines both
into one probability per sense. It is small and fast and less accurate than the encoder models; its measured accuracy
is reported in the repository's build notes.

The subsystem contains six units:

- **BasicWordSenseModel**: the only public type; implements `IWordSenseModel`.
- **BasicClassifier**: the classifier the model creates; splits the text into sentence segments and drives the others.
- **BasicTables**: the immutable statistics read once from the embedded table `basic.bin`.
- **PosTagger**: the HMM that returns the part-of-speech posterior of every token of a sentence.
- **CueModel**: the extraction of the context features and the scoring of the senses of one lemma.
- **SenseFusion**: the combination of the posterior and the cue scores into sense probabilities.

`PosTag` (the tag constants) is a small helper of the tagger and the tables. The subsystem references the core package
only and uses only the public `WordLexicon.Words`.

### Interfaces

It exposes `BasicWordSenseModel` with a public parameterless constructor. `EnsureModelAsync` returns immediately and
ignores the mirror; `ClassifyAsync` classifies at once, so a lazy ensure is trivial. It consumes `WordLexicon.Words`
and the contracts of the core
package. All other types are internal and visible to the builder (which trains and writes the table with the same
feature hash and tag constants) and to the Basic test project.

### Design

#### Classification

`BasicWordSenseModel.ClassifyAsync` delegates to a lazily created `BasicClassifier` that shares the process-wide
`BasicTables.Default`, which
is loaded on first use. The classifier normalizes each word (lower case, typographic apostrophe straightened; tokens
without a letter become empty), splits the list into segments that end after a `.`, `!` or `?` token, and processes
each segment independently:

1. every token becomes a tagger token: a token without a letter is fixed to the punctuation tag (the other tag when it
   contains a digit); a word carries the set of parts of speech the lexicon lists for it and whether it is capitalized;
2. `PosTagger` computes the tag posterior of every token of the segment;
3. for each word, in order:
   - no lexicon entry (or an empty key): `(word, null, 0)`;
   - one entry: `(word, entry, 1.0)`;
   - several entries: `CueModel` extracts the features of the word from the segment and scores the senses of its
     lemma, `SenseFusion` turns the posterior and the cue scores into one probability per sense, and the result is the
     most probable sense with that probability as its confidence.

An ambiguous word is therefore always answered, and its confidence is the probability of the winner, at least one over
the number of senses and at most 1. The confidence is a relative score, not a calibrated probability, but a higher value
is more often right: the builder's `evaluate basic` reports the accuracy per confidence bucket and the coverage and
precision at fixed thresholds, so a caller can trade coverage for precision with a threshold. Ties go to the sense
that comes first in the lexicon entry.

#### Statistical Table

`basic.bin` is a Brotli-compressed container in the format `WSB1`, written only by the builder (`train-basic`) and read
only by `BasicTables`. After decompression it holds, in this order: the magic `WSB1`; seven little-endian doubles
(gamma, beta, lambda 1 to 3, the tagger weight of the fusion and the inverse temperature of the cues); the tag count;
the unigram, bigram and trigram tag counts (the sentence boundary is the last index); the tabled words with their
closed-class mask and per-tag counts; the suffix table; and the cue tables of the ambiguous lemmas (16-bit feature hashes
and one signed byte weight per feature and sense, in steps of 1/8 nat). Words, suffixes and lemmas are in ordinal order,
so equal training data gives byte-identical tables. The reader validates the layout and throws `InvalidDataException`
for anything malformed. The format is described in detail in the `BasicTables` unit design.

#### Determinism and Concurrency

The result depends only on the input words and the embedded table. The classifier and the tables hold no mutable
state, the scratch buffers are rented from the shared array pool for the duration of one call, and the lazy loading of
the table is thread-safe, so one classifier (or many) may be used from any number of threads. The subsystem performs no
file or network access and has no ONNX dependency.

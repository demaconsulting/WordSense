### BasicTables Unit Design

![Core Structure](CoreView.svg)

#### Purpose

`BasicTables` holds the immutable statistics of the Basic model and reads them from the embedded table `basic.bin`:
the tag transition probabilities and the word and suffix statistics of the tagger, the hyper-parameters of the runtime,
the cue tables of the ambiguous lemmas and the shared tense table. It also defines the tag set through `PosTag`. The
table is bound to the word image by the hash of the analysis inventory.

#### Data Model

- `PosTag`: the 13 tags (noun, pronoun, verb, adjective, adverb, preposition, conjunction, interjection, determiner,
  auxiliary, infinitive marker, punctuation, other) and the sentence boundary pseudo tag (index 13). The first ten
  (noun to auxiliary) have the numeric values of `PartOfSpeech`; infinitive marker, punctuation and other have no part of
  speech. Open classes are noun, verb, adjective and adverb.
- `BasicTables`: the header values `Gamma`, `Beta`, `Lambda1` to `Lambda3`, `HmmWeight`, `InvTemperature`,
  `TenseWeight`, `ClauseSmoothing` and the four numbers of the word prior (`PriorShare`, `PriorSmoothing`,
  `PriorSenses` and `PriorExponent`); the
  unigram, bigram and trigram counts and the clause bigram and trigram counts (`ClauseBigrams`, `ClauseTrigrams`: the
  counts of the positions whose clause already has a verb, never above the totals; the counts of the other positions are
  the difference); the derived `Transition` (interpolated trigram, bigram and unigram probabilities,
  row-major over 14 x 14 x 14), `ClauseTransition` (two copies of that layout, the first for a clause without a verb and
  the second for a clause that has one), `TagPrior` and `SuffixFallback`; the `Words` (`WordEntry`: closed-class mask
  and per-tag
  counts) and the `Cues` (`LemmaCue`: the analyses as `CueAnalysis` with lemma, part of speech, features and quantized
  prior, the ascending feature hashes and the feature-major signed byte weights), both frozen dictionaries. Every array
  of the statistics (`Unigrams`, `Bigrams`, `Trigrams`, `ClauseBigrams`, `ClauseTrigrams`, `Transition`,
  `ClauseTransition`, `TagPrior`, `SuffixFallback`, the `Counts` of a
  `WordEntry` and the `Analyses`, `Hashes` and `Weights` of a `LemmaCue`) is exposed as an `ImmutableArray<T>`, a
  read-only view that wraps the array the reader filled without copying it; the reader hands the arrays over and keeps
  no reference, so no consumer can change them and the hot paths (`PosTagger`, `CueModel.Score`) read them through
  `ReadOnlySpan<T>` (`AsSpan`) with no allocation.
- `TenseCue` (the `Tense` property, null for a table without one): the shared tense table, a quantized prior (the log
  odds of a past verb against a non-past one in the training text, in eighths of a nat), the ascending feature hashes
  and one signed byte weight per hash (the log likelihood ratio of past against non-past), all read-only views like the
  cue tables. It is trained on all verbs, so it has no analyses and is not tied to the inventory.
- The word prior is not stored in the table. Its four numbers are in the header and its evidence, the WordNet 3.0 tag
  counts and the senses of the lemma and part of speech of every analysis of a word, is in the word image that the
  table is bound to (`WordImage.UsageTotalsOf`, read by `WordSupport`, see the `PosTagger` design). Computing it at run
  time instead of tabling it keeps `basic.bin` small (a table of the prior of all 28,000 ambiguous spellings would add
  well over 100 KB), cannot disagree with the image, and follows an image rebuild without a retraining.
- Each `LemmaCue` has a reliability, one byte (`ReliabilityByte`, 0 to 255; `Reliability` is the byte over 255): the
  factor on the cue scores of the table in the fused score of an analysis. The builder measures how well a table predicts
  held-out sentences of its own anchors and damps the tables that predict them near or below chance (see the builder
  design, "Cue reliability"); a table that is fully reliable stores 255 and counts at full strength.
- The analysis inventory hash: a 32-byte hash that is the inventory hash of the word image (its spelling, analysis,
  feature, entry, sense, group and lemma-block sections). It identifies the inventory the cue columns were trained
  against: `basic.bin` names its analyses, so it is rebuilt whenever the inventory changes, and a table that carries
  another hash is refused.
- `FeatureRef`: one context feature (kind and token) and its 16-bit hash, a 32-bit FNV-1a over the UTF-16 code units of
  the feature name (the token, prefixed with `L1:` or `R1:` for an immediate neighbor, and for the tense model `LW:` and
  `RW:` for the left and right window and `NA:` for the governing function word) folded by xor-ing the upper half
  into the lower half. The builder uses the same hash, so training and inference cannot diverge.
- The table format (Brotli, then little-endian; integers are 7-bit varints, strings are a varint byte length and
  UTF-8): the four-byte magic `WSB6` that names the format and its revision (`WSB3`, without the tense weight and the
  tense table, `WSB4`, without the clause counts and the clause smoothing, and `WSB5`, without the word prior and the
  reliability of the cue tables, are refused); the 32-byte analysis
  inventory hash;
  thirteen doubles
  (gamma, beta, lambda 1 to 3, tagger weight, inverse temperature, tense weight, clause smoothing, prior share, prior
  smoothing, prior sense weight, prior exponent); the tag count (13);
  the unigram (14),
  bigram
  (14 x 14) and trigram (14 x 14 x 14) counts, followed by the clause bigram (14 x 14) and clause trigram (14 x 14 x 14)
  counts; the word count and per word the word, a flags byte (bit 0: closed
  class), a 16-bit closed-class mask when closed, a 16-bit presence mask and one count per set bit; the suffix count and
  per suffix the suffix and four counts (noun, verb, adjective, adverb); the lemma count and per lemma the lemma, the
  reliability byte, the
  analyses (lemma string, part of speech byte, features string, signed prior byte), the delta-coded ascending feature
  hashes and one signed byte weight per feature and analysis; and the tense section, a flag byte (0: none, 1: a table
  follows) and the signed prior byte, the delta-coded ascending feature hashes (at least one) and one signed byte weight
  per hash. Words, suffixes and lemmas are in ascending ordinal
  order. At load each analysis is resolved against the analysis inventory of the word image.

#### Key Methods

- `Load(blob)`: decompresses and reads a table, validates it and derives the transition table. The blob must be exactly
  one complete Brotli stream (the decoder reports how many input bytes it consumed, and bytes after the end of the
  stream are rejected). It checks the magic, the
  analysis inventory hash against the word image, the hyper-parameters (finite and in range; the prior share between 0
  and 1, the prior exponent above 0 and up to 16, and for a share above 0 a positive slot smoothing or sense weight),
  the tag count, that the
  clause counts do not exceed the total counts, that
  words, suffixes and lemmas are strictly ascending, the flags and masks, positive word counts, the analysis counts and
  parts of speech, that every analysis exists in the inventory and occurs once in its lemma table, strictly ascending
  feature hashes, the bounds of every number, that the tense section has a valid flag, at least one feature and as many
  weights as hashes, that every string is well-formed UTF-8 (malformed bytes are rejected,
  never replaced) and that no bytes trail the decompressed content.
- `Default`: the table embedded in the assembly, loaded on first use under a thread-safe lazy initializer through
  `LoadEmbedded(Stream?)`, which reports a missing, corrupt or foreign table as a `ModelIntegrityException`.
- `TryFindSuffix(key, out probabilities)`: the distribution over noun, verb, adjective and adverb of the longest tabled
  suffix of one to three characters of a word (binary search), as a read-only view, or `false` when none is tabled;
  probabilities are `(count + 0.1) / (total + 0.4)`.
- `LemmaCue.RowOf(analysis)`: the column of an analysis in a cue table. The columns are analyses (lemma, part of speech
  and features): a column with empty features stands for any features of that lemma and part of speech, and a column
  with features (for example `read`'s `Tense=Past+VerbForm=Part` and `Tense=Pres`) is a candidate for an analysis whose
  feature items include all of its items. Among the candidate columns of the same lemma and part of speech the one with
  the most feature items wins (the first on a tie), and the empty-features column always qualifies, so it is the
  fallback of every analysis of its lemma and part of speech; the column of a second analysis of one part of speech
  (the past of `read`) is therefore not the column of the first.
- `BasicTables.FindCue(word, first)`: the cue table of a word: the table named by the normalized word first (a form that
  spans lemmas, such as `left` or `bases`, has its own table) and then the table of the lemma of the first analysis;
  null when neither exists.
- The derivation interpolates `Transition = lambda3 * p3 + lambda2 * p2 + lambda1 * p1` from the counts, so each row
  that has observations is a probability distribution. `ClauseTransition` interpolates the same way from the counts of
  one clause state (the clause counts for the state with a verb, the totals minus the clause counts for the other) and
  mixes the result with `Transition` by the weight `m / (m + ClauseSmoothing)`, where `m` is the number of trigram counts
  of the context (the two previous tags) in that state. A context without a count in the state has the weight 0 and so
  gives exactly `Transition`, and a table without clause counts behaves as before. Where the state has counts, the
  weight is below 1 for every finite smoothing, so a larger smoothing moves the result toward `Transition` and a very
  large one comes close to it, but the result equals `Transition` only for a context without a count in the state (or,
  in the limit, for an infinite smoothing).

#### Error Handling

`BasicTables.Load` throws `InvalidDataException` for a data that is not Brotli, a wrong
magic or format revision (including the retired `WSB3`, `WSB4` and `WSB5`), a table built for another analysis inventory
(the message
names both hashes), an unknown or
duplicate analysis, invalid hyper-parameters, a different tag count, unordered words, suffixes, lemmas or feature
hashes, invalid flags, masks, analysis counts or parts of speech, oversized numbers, a truncated blob, bytes after the
end of the Brotli stream and trailing bytes. `Default` loads the embedded table through `LoadEmbedded(Stream?)`, which
maps a missing embedded resource and
each of these failures to a `ModelIntegrityException` with the original `InvalidDataException` as its inner exception
(the builder and the tests that call `Load` directly still see `InvalidDataException`).

#### Determinism and Thread Safety

The table is immutable after `Load`: the statistics are read-only views (`ImmutableArray<T>`, frozen dictionaries of
immutable entries) that no consumer can write through, so an instance can be shared by any number of threads, and the
same bytes always give the same table. The format is written deterministically by the builder.

#### Dependencies

The core `PartOfSpeech`, `WordDetails` and the analysis inventory of the word image, and the .NET Brotli and frozen
collection types.

#### Callers

`BasicClassifier`, `PosTagger` and `CueModel`; the builder (`BasicTablesWriter`, `BasicTrainer`, `BasicTuner` and
`evaluate basic`) for the tag constants, the feature hash and verifying the written table. The builder's `train-basic`
rolls the analysis labels of its training sentences up from the fine-sense corpus and writes the cue columns by
analysis.

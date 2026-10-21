### BasicTables Unit Design

![Core Structure](CoreView.svg)

#### Purpose

`BasicTables` holds the immutable statistics of the Basic model and reads them from the embedded table `basic.bin`:
the tag transition probabilities and the word and suffix statistics of the tagger, the hyper-parameters of the runtime,
and the cue tables of the ambiguous lemmas. It also defines the tag set through `PosTag`. The table is bound to the
word image by the hash of the analysis inventory.

#### Data Model

- `PosTag`: the 13 tags (noun, pronoun, verb, adjective, adverb, preposition, conjunction, interjection, determiner,
  auxiliary, infinitive marker, punctuation, other) and the sentence boundary pseudo tag (index 13). The first ten
  (noun to auxiliary) have the numeric values of `PartOfSpeech`; infinitive marker, punctuation and other have no part of
  speech. Open classes are noun, verb, adjective and adverb.
- `BasicTables`: the header values `Gamma`, `Beta`, `Lambda1` to `Lambda3`, `HmmWeight` and `InvTemperature`; the
  unigram, bigram and trigram counts; the derived `Transition` (interpolated trigram, bigram and unigram probabilities,
  row-major over 14 x 14 x 14), `TagPrior` and `SuffixFallback`; the `Words` (`WordEntry`: closed-class mask and per-tag
  counts) and the `Cues` (`LemmaCue`: the analyses as `CueAnalysis` with lemma, part of speech, features and quantized
  prior, the ascending feature hashes and the feature-major signed byte weights), both frozen dictionaries.
- The analysis inventory hash: a 32-byte hash that is the inventory hash of the word image (its spelling, analysis,
  feature, entry, sense, group and lemma-block sections). It identifies the inventory the cue columns were trained
  against: `basic.bin` names its analyses, so it is rebuilt whenever the inventory changes, and a table that carries
  another hash is refused.
- `FeatureRef`: one context feature (kind and token) and its 16-bit hash, a 32-bit FNV-1a over the UTF-16 code units of
  the feature name (the token, prefixed with `L1:` or `R1:` for an immediate neighbor) folded by xor-ing the upper half
  into the lower half. The builder uses the same hash, so training and inference cannot diverge.
- The table format (Brotli, then little-endian; integers are 7-bit varints, strings are a varint byte length and
  UTF-8): the four-byte magic `WSB3` that names the format and its revision; the 32-byte analysis inventory hash;
  seven doubles
  (gamma, beta, lambda 1 to 3, tagger weight, inverse temperature); the tag count (13); the unigram (14), bigram
  (14 x 14) and trigram (14 x 14 x 14) counts; the word count and per word the word, a flags byte (bit 0: closed
  class), a 16-bit closed-class mask when closed, a 16-bit presence mask and one count per set bit; the suffix count and
  per suffix the suffix and four counts (noun, verb, adjective, adverb); the lemma count and per lemma the lemma, the
  analyses (lemma string, part of speech byte, features string, signed prior byte), the delta-coded ascending feature
  hashes and one signed byte weight per feature and analysis. Words, suffixes and lemmas are in ascending ordinal
  order. At load each analysis is resolved against the analysis inventory of the word image.

#### Key Methods

- `Load(blob)`: decompresses and reads a table, validates it and derives the transition table. It checks the magic, the
  analysis inventory hash against the word image, the hyper-parameters (finite and in range), the tag count, that
  words, suffixes and lemmas are strictly ascending, the flags and masks, positive word counts, the analysis counts and
  parts of speech, that every analysis exists in the inventory and occurs once in its lemma table, strictly ascending
  feature hashes, the bounds of every number, that every string is well-formed UTF-8 (malformed bytes are rejected,
  never replaced) and that no bytes trail.
- `Default`: the table embedded in the assembly, loaded on first use under a thread-safe lazy initializer.
- `FindSuffix(key)`: the distribution over noun, verb, adjective and adverb of the longest tabled suffix of one to three
  characters of a word (binary search), or null when none is tabled; probabilities are `(count + 0.1) / (total + 0.4)`.
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
  that has observations is a probability distribution.

#### Error Handling

`Load` and `Default` throw `InvalidDataException` for a missing embedded resource, data that is not Brotli, a wrong
magic or format revision, a table built for another analysis inventory (the message names both hashes), an unknown or
duplicate analysis, invalid hyper-parameters, a different tag count, unordered words, suffixes, lemmas or feature
hashes, invalid flags, masks, analysis counts or parts of speech, oversized numbers, a truncated blob and trailing
bytes.

#### Determinism and Thread Safety

The table is immutable after `Load`, so an instance can be shared by any number of threads, and the same bytes always
give the same table. The format is written deterministically by the builder.

#### Dependencies

The core `PartOfSpeech`, `WordDetails` and the analysis inventory of the word image, and the .NET Brotli and frozen
collection types.

#### Callers

`BasicClassifier`, `PosTagger` and `CueModel`; the builder (`BasicTablesWriter`, `BasicTrainer`, `BasicTuner` and
`evaluate basic`) for the tag constants, the feature hash and verifying the written table. The builder's `train-basic`
rolls the analysis labels of its training sentences up from the fine-sense corpus and writes the cue columns by
analysis.

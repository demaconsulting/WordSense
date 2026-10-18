### BasicTables Unit Design

![Basic Structure](BasicView.svg)

#### Purpose

`BasicTables` holds the immutable statistics of the Basic model and reads them from the embedded table `basic.bin`:
the tag transition probabilities and the word and suffix statistics of the tagger, the hyper-parameters of the runtime,
and the cue tables of the ambiguous lemmas. It also defines the tag set through `PosTag`.

#### Data Model

- `PosTag`: the 13 tags (noun, pronoun, verb, adjective, adverb, preposition, conjunction, interjection, determiner,
  auxiliary, infinitive marker, punctuation, other) and the sentence boundary pseudo tag (index 13). The first eight have
  the numeric values of `PartOfSpeech`. Open classes are noun, verb, adjective and adverb.
- `BasicTables`: the header values `Gamma`, `Beta`, `Lambda1` to `Lambda3`, `HmmWeight` and `InvTemperature`; the
  unigram, bigram and trigram counts; the derived `Transition` (interpolated trigram, bigram and unigram probabilities,
  row-major over 14 x 14 x 14), `TagPrior` and `SuffixFallback`; the `Words` (`WordEntry`: closed-class mask and per-tag
  counts) and the `Cues` (`LemmaCue`: the senses as `CueSense` with part of speech, meaning and quantized prior, the
  ascending feature hashes and the feature-major signed byte weights), both frozen dictionaries.
- `FeatureRef`: one context feature (kind and token) and its 16-bit hash, a 32-bit FNV-1a over the UTF-16 code units of
  the feature name (the token, prefixed with `L1:` or `R1:` for an immediate neighbor) folded by xor-ing the upper half
  into the lower half. The builder uses the same hash, so training and inference cannot diverge.
- Format `WSB1` (Brotli, then little-endian; integers are 7-bit varints, strings are a varint byte length and UTF-8):
  the magic `WSB1`; seven doubles (gamma, beta, lambda 1 to 3, tagger weight, inverse temperature); the tag count (13);
  the unigram (14), bigram (14 x 14) and trigram (14 x 14 x 14) counts; the word count and per word the word, a flags byte
  (bit 0: closed class), a 16-bit closed-class mask when closed, a 16-bit presence mask and one count per set bit; the
  suffix count and per suffix the suffix and four counts (noun, verb, adjective, adverb); the lemma count and per lemma
  the lemma, the senses (part of speech byte, meaning, signed prior byte), the delta-coded ascending feature hashes and
  one signed byte weight per feature and sense. Words, suffixes and lemmas are in ascending ordinal order.

#### Key Methods

- `Load(blob)`: decompresses and reads a table, validates it and derives the transition table. It checks the magic, the
  hyper-parameters (finite and in range), the tag count, that words, suffixes and lemmas are strictly ascending, the
  flags and masks, positive word counts, the sense counts and parts of speech, strictly ascending feature hashes, the
  bounds of every number, that every string is well-formed UTF-8 (malformed bytes are rejected, never replaced) and
  that no bytes trail.
- `Default`: the table embedded in the assembly, loaded on first use under a thread-safe lazy initializer.
- `FindSuffix(key)`: the distribution over noun, verb, adjective and adverb of the longest tabled suffix of one to three
  characters of a word (binary search), or null when none is tabled; probabilities are `(count + 0.1) / (total + 0.4)`.
- `LemmaCue.RowOf(details)`: the column of a lexicon sense in a cue table, matched by part of speech and meaning.
- The derivation interpolates `Transition = lambda3 * p3 + lambda2 * p2 + lambda1 * p1` from the counts, so each row
  that has observations is a probability distribution.

#### Error Handling

`Load` and `Default` throw `InvalidDataException` for a missing embedded resource, data that is not Brotli, a wrong
magic, invalid hyper-parameters, a different tag count, unordered words, suffixes, lemmas or feature hashes, invalid
flags, masks, sense counts or parts of speech, oversized numbers, a truncated blob and trailing bytes.

#### Determinism and Thread Safety

The table is immutable after `Load`, so an instance can be shared by any number of threads, and the same bytes always
give the same table. The format is written deterministically by the builder.

#### Dependencies

The core `PartOfSpeech` and `WordDetails`, and the .NET Brotli and frozen collection types.

#### Callers

`BasicClassifier`, `PosTagger` and `CueModel`; the builder (`BasicTablesWriter`, `BasicTrainer`, `BasicTuner` and
`evaluate basic`) for the tag constants, the feature hash and verifying the written table.

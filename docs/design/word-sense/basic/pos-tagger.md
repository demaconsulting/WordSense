### PosTagger Unit Design

![Basic Structure](BasicView.svg)

#### Purpose

`PosTagger` is the part-of-speech tagger of the Basic model: a second-order hidden Markov model over thirteen tags that
returns, for every token of one sentence, the posterior probability of each tag given the whole sentence.

#### Data Model

- `TaggerToken(Key, DictionaryMask, Capitalized, ForcedTag)`: one token. A word token has its lower-case key, the bit
  mask of the parts of speech the lexicon lists (0 for an unknown word) and whether it is capitalized; a fixed token
  (punctuation, number) has exactly one possible tag.
- The possible tags of a position are the lexicon tags plus the closed-class tags of the tabled word. For a word with no
  possible tag at all (unknown): when capitalized and not first in the sentence, noun only; otherwise the four open
  classes, weighted by the distribution of the longest known suffix (or the fallback distribution).
- The emission weight of a possible tag of a known word is `(p(tag | word) / p(tag)) ^ gamma`, where `p(tag | word)`
  is `(count + beta * q) / (total + beta)` and `q` is the prior of the tag renormalized over the possible tags; a word
  without counts has equal weights. An unknown word uses `suffix probability / p(tag)`.
- The transitions are the interpolated trigram, bigram and unigram probabilities of `BasicTables`.
- The lattice is sparse: for each position a matrix over the possible tags of the previous position (the boundary for
  the first) and of the position itself, so the work per word is the product of the possible-tag counts of three
  neighbors rather than of all tags.

#### Key Methods

`Posteriors(tables, sentence, posteriors)` fills the emission rows and possible tags, then runs the scaled forward pass,
the scaled backward pass and combines them: the posterior of a tag is the normalized sum over the previous tag of the
forward and backward values. If a row sums to zero (a path the transitions rule out) it falls back to the normalized
emissions. The result has 13 probabilities per token, row-major; each row sums to 1 over the possible tags and is 0
elsewhere. An empty sentence needs no work. No Viterbi decoding is done because the classifier needs the probabilities.

#### Error Handling

A null table throws `ArgumentNullException`; a result buffer shorter than 13 values per token throws
`ArgumentException`. Unknown words are not errors.

#### Determinism and Thread Safety

The computation is pure and uses only the immutable tables: the same input always gives bit-identical posteriors. The
scratch space (integer, emission, forward and backward buffers) is rented from `ArrayPool` for the call and returned,
so the tagger does not allocate per word and is safe for concurrent use.

#### Dependencies

`BasicTables` (transitions, tag prior, words, suffixes, gamma and beta) and `PosTag`.

#### Callers

`BasicClassifier`.

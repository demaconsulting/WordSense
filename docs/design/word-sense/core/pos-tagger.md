### PosTagger Unit Design

![Core Structure](CoreView.svg)

#### Purpose

`PosTagger` is the part-of-speech tagger of the Basic model: a second-order hidden Markov model over thirteen tags that
returns, for every token of one sentence, the posterior probability of each tag given the whole sentence.

#### Data Model

- `TaggerToken(Key, DictionaryMask, Capitalized, ForcedTag, Possessive, NonFinite, Support)`: one token. A word token has
  its
  lower-case key, the bit mask of the parts of speech the lexicon lists (0 for an unknown word) and whether it is
  capitalized; a fixed token (punctuation, number) has exactly one possible tag. A possessive that is written as one
  token (`technician's`) carries its stem as the key and the flag `Possessive` (see "Possessive expansion").
  `NonFinite` says that the lexicon lists the word as a verb only in forms that are never the finite verb of a clause
  (see "Clause State"). `Support` is the catalog support of the word (see "Word Prior"), or null.
- The possible tags of a position are the lexicon tags plus the closed-class tags of the tabled word. For a word with no
  possible tag at all (unknown): when capitalized and not first in the sentence, noun only; otherwise the four open
  classes, weighted by the distribution of the longest known suffix (or the fallback distribution).
- The emission weight of a possible tag of a known word is `(p(tag | word) / p(tag)) ^ gamma`, where `p(tag | word)`
  is `(count + beta * r) / (total + beta)` and `r` is the target of the pseudo counts: the prior of the tag renormalized
  over the possible tags (`q`), or, for a word with catalog support, the mix `(1 - share) * q + share * prior` with the
  word prior of the next section; a word without counts has `p(tag | word) = r`, which is equal weights without support.
  An unknown word uses `suffix probability / p(tag)`.
- The transitions are the interpolated trigram, bigram and unigram probabilities of `BasicTables`, in two copies, one
  for each clause state (see "Clause state").
- The lattice is sparse: for each position a matrix over the possible tags of the previous position (the boundary for
  the first), of the position itself and of the clause state, so the work per word is twice the product of the
  possible-tag counts of three neighbors rather than of all tags.

#### Clause State

The hidden state of the model is the pair of the last two tags and one bit: whether the current clause already has a
verb. The state carries the knowledge that a clause has one finite verb: after a determiner and a noun a verb follows
about 43% of the time when the clause has no verb yet and about 11% when it has one (the open corpus training split),
so `Run the spell check` no longer reads `check` as a second verb. A clause starts at the beginning of the sentence
segment and after any token that is tagged conjunction or punctuation; it has a verb from the first token tagged verb on
(`PosTag.ClauseVerbMask`, `PosTag.ClauseBreakMask` and `PosTag.NextClause`; the auxiliary tag does not count as a verb).
One exception keeps a gerund from taking the verb of its clause: when the first token of the segment is listed by the
lexicon only as a non-finite verb (every verb analysis is a gerund, `VerbForm=Ger`, or a participle that is not also a
past tense, `VerbForm=Part` without `Tense=Past`: `Resolving`, `Launching`, `Broken`), it does not give its clause a
verb, because in `Resolving this issue requires care` the verb of the clause is still to come; an infinitive (`Remove`,
`Bring`), a past tense and an unknown word at the start do. The rule reads the lexicon, not the ending of the word, so
`Bring the tool` and `Ring the bell` are imperatives. Without the exception, a gerund at the start of a clause read as
a noun in 2 of the 4 development cases in which a determiner follows (`Resolving this`, `Having a`), because the bit
made the noun-phrase reading of the opening words more probable.
The tag is chosen by the path, so the bit is carried through the lattice: the state after a token is a function of the
state before it and of the tag of the token. The probability of a tag depends on the two previous tags and on the state
before it (`BasicTables.ClauseTransition`, one copy of the transitions for each state). The same rule counts the
training transitions (`TaggerCounts`), so the runtime and the counts cannot disagree.

#### Word Prior

The word prior estimates `P(tag | word)` for every word that the sense catalog knows, from shipped licensed data only: the
WordNet 3.0 tag counts and the senses of the lemmas that the spelling can be (`WordSupport`). No treebank is used. A word
without training counts has equal emission weights in every part of speech without the prior, so the transitions alone
decide.

An audit of the tagger errors motivated the prior: on the noun-verb tokens of the treebank development and test files,
words without counts were 12.5 percent wrong and words with counts 6.5 percent. The audit looked at both files. Every
setting of the prior is chosen on the development sets only; the test figures are reported, never tuned.

- `WordSupport.Of(key, analyses)` totals, for each of the four open-class tags (noun, verb, adjective, adverb), the
  WordNet 3.0 tag counts of the senses (`WordImage.UsageTotalsOf`), the senses and the number of slots (lemma and part of
  speech pairs with at least one sense) of the distinct slots among the analyses. The analyses of one slot (the past and
  the infinitive) count once. A closed class adds nothing: WordNet does not tag it and its project sense has no count, so
  it keeps its generic share. The support is null when no analysis has a sense. It is cached by key in a small
  direct-mapped cache of immutable entries, safe for concurrent use. `BasicClassifier.MakeToken` looks it up only for a
  word with two or more possible parts of speech; with one, the emission decides nothing.
- `TryFillPrior` turns the totals into a distribution over the possible tags. The weight of an open-class tag that has a
  slot is `(tag count + smoothing * slots + sense weight * senses) ^ exponent`. The smoothing keeps a part of speech that
  was never tagged possible, the sense weight is the proxy of a word whose lemmas have no tag count, and an exponent below
  1 flattens the counts. The weights are computed in log space and shifted by the largest log weight before they are
  exponentiated, so the sum lies between 1 and the number of tags for any exponent. They are normalized and scaled to the
  share of the generic tag prior that the open-class tags have among the possible tags. A possible tag without a slot
  keeps its generic share, so the prior never changes the odds between a part of speech of the catalog and a closed
  class. A weight or a sum that is not a finite number throws `InvalidDataException`; `BasicTables.Load` bounds the
  exponent to 0 (exclusive) to 16 so that a loaded table cannot reach it.
- The prior is the target of the pseudo counts of the emission, by the share `PriorShare` of the table; the rest of the
  target is the generic tag prior. A word without counts follows the prior, and a word with counts keeps them (the `beta`
  pseudo counts weigh against the counts of the word: 7 pseudo counts against 20 tokens are about a quarter). A prior
  share of 0, or a token without support, gives the emission of the counts and the generic prior alone.
- It is computed at run time from the image and not tabled in `basic.bin`: the evidence is in the image that the table
  is bound to, a table of the prior of all 28,000 ambiguous spellings would add well over 100 KB and could disagree with
  the image, and an image rebuild needs no retraining of the prior. The cost is one lookup of the lemma blocks per
  analysis of an ambiguous word, once per distinct word thanks to the cache.
- The prior aggregates over the lemmas of a spelling, not over its forms. The counts of `present` do not know that
  `presents` is mostly the noun plural in modern text (WordNet 3.0 tags verb senses of `present` 114 times and noun
  senses 21 times). Training counts of the word correct this; a rare inflected form with a verb-heavy lemma is the main
  weakness.

#### Possessive Expansion

The counts were made on text in which the possessive marker is a token of its own (`technician 's rank`: noun, other,
noun), but the documented input keeps the possessive inside the token (`technician's`). A token with the flag
`Possessive` is therefore expanded inside `Posteriors` into two positions of the model, the stem (the possible tags of
the possessive analyses, noun or pronoun) and a fixed token of the tag other; the posterior of the stem is the result
for the caller's token. The caller's positions and results stay one to one: the result buffer has one row for each
token, and the classifier still answers the joined word with its own analysis (`Poss=Yes`).

#### Key Methods

`Posteriors(tables, sentence, posteriors)` expands the possessives (a sentence without one runs directly), fills the
emission rows and possible tags, then runs the scaled forward pass, the scaled backward pass and combines them: the
posterior of a tag is the normalized sum over the previous tag and the clause state of the forward and backward values.
If a row sums to zero (a path the transitions rule out) it falls back to the normalized emissions. The result has 13
probabilities per token, row-major; each row sums to 1 over the possible tags and is 0 elsewhere. An empty sentence
needs no work. No Viterbi decoding is done because the classifier needs the probabilities.

#### Error Handling

A null table throws `ArgumentNullException`; a result buffer shorter than 13 values per token throws
`ArgumentException` (the number of tokens of the caller, expanded or not). Unknown words are not errors.
`WordSupport.Of` throws `ArgumentNullException` for a null key and `ModelIntegrityException` when the embedded lexicon
or its sense catalog is missing or corrupt; `WordSupport.TryFillPrior` throws `InvalidDataException` for a weight that is
not a finite number.

#### Determinism and Thread Safety

The computation is pure and uses only the immutable tables: the same input always gives bit-identical posteriors. The
scratch space (token, integer, emission, forward and backward buffers) is rented from `ArrayPool` for the call and
returned, so the tagger does not allocate per word and is safe for concurrent use.

#### Dependencies

`BasicTables` (transitions, tag prior, words, suffixes, gamma, beta and the word prior numbers), `WordSupport` (the
catalog evidence of a word, from the word image) and `PosTag`.

#### Callers

`BasicClassifier`.

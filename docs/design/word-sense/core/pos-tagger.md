### PosTagger Unit Design

![Core Structure](CoreView.svg)

#### Purpose

`PosTagger` is the part-of-speech tagger of the Basic model: a second-order hidden Markov model over thirteen tags that
returns, for every token of one sentence, the posterior probability of each tag given the whole sentence.

#### Data Model

- `TaggerToken(Key, DictionaryMask, Capitalized, ForcedTag, Possessive, NonFinite)`: one token. A word token has its
  lower-case key, the bit mask of the parts of speech the lexicon lists (0 for an unknown word) and whether it is
  capitalized; a fixed token (punctuation, number) has exactly one possible tag. A possessive that is written as one
  token (`technician's`) carries its stem as the key and the flag `Possessive` (see "Possessive expansion").
  `NonFinite` says that the lexicon lists the word as a verb only in forms that are never the finite verb of a clause
  (see "Clause State").
- The possible tags of a position are the lexicon tags plus the closed-class tags of the tabled word. For a word with no
  possible tag at all (unknown): when capitalized and not first in the sentence, noun only; otherwise the four open
  classes, weighted by the distribution of the longest known suffix (or the fallback distribution).
- The emission weight of a possible tag of a known word is `(p(tag | word) / p(tag)) ^ gamma`, where `p(tag | word)`
  is `(count + beta * q) / (total + beta)` and `q` is the prior of the tag renormalized over the possible tags; a word
  without counts has equal weights. An unknown word uses `suffix probability / p(tag)`.
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

#### Determinism and Thread Safety

The computation is pure and uses only the immutable tables: the same input always gives bit-identical posteriors. The
scratch space (token, integer, emission, forward and backward buffers) is rented from `ArrayPool` for the call and
returned, so the tagger does not allocate per word and is safe for concurrent use.

#### Dependencies

`BasicTables` (transitions, tag prior, words, suffixes, gamma and beta) and `PosTag`.

#### Callers

`BasicClassifier`.

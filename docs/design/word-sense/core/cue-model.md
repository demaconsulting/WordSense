### CueModel Unit Design

![Core Structure](CoreView.svg)

#### Purpose

`CueModel` is the context model of the Basic model: it extracts the context features of a target word and scores the
candidate analyses of the word from the per-lemma table of hashed features, a naive Bayes classifier. It separates
analyses that share a part of speech (the past and the present of `read`, the lemmas of a form such as `left`), which
the tagger cannot.

#### Data Model

- A feature is a `FeatureRef` of a kind and a lower-case token: a window feature (any distinct token that contains a
  letter within five positions of the target, the target excluded), a left feature (the token directly before the
  target) or a right feature (the token directly after it). The immediate neighbors are always written as left and
  right features, whatever they are (even punctuation); at most 12 features arise.
- The tense model (the shared table of `BasicTables`, see the `BasicClassifier` section _Tense Evidence_) reads richer
  features of the same target: the cue features, then the distinct letter-bearing tokens within five positions told apart
  by side (a left-window feature and a right-window feature), the edge of the segment as the left neighbor of the first
  word (no token with a letter before the target) and the right neighbor of the last word (none after it), so that a
  quotation mark before the first word or the closing punctuation after the last word does not hide the edge, and the
  governing feature, the nearest token to the left within six positions
  that is a function word carrying the tense of a verb phrase (`IsGovernor`: forms of `be`, `have` and `do`, the modals,
  `to`, negations and contractions; a closed class of English grammar, not a list of verbs). At most 23 features arise.
- The noun/verb model (the shared table of `BasicTables`, see the `BasicClassifier` section _Noun/Verb Evidence_) reads
  eight features of the same target: the word and the class of each of the two tokens before it and of the two tokens
  after it. A neighbor outside the segment is the segment edge (word and class); a neighbor without a letter is its own
  text as the word and `<punct>` as the class. The class of a neighbor comes from its lexicon analyses
  (`ContextClassOf`): `auxiliary` when one is an auxiliary, `verb` when every analysis is a verb that can be finite,
  `nonfinite` when every analysis is a gerund or a participle, `ambiguous-verb` or `ambiguous` when there are several
  parts of speech with or without a verb, the name of the part of speech when there is one, and `<unknown>` without an
  analysis. The classes are a closed set of grammatical classes, not a list of words. The target is never a feature.
- The context is limited to the sentence segment the classifier passes in.
- A lemma's `LemmaCue` table, keyed by training lemma (or by a form that spans lemmas), holds ascending 16-bit feature
  hashes and one signed byte per feature and analysis in
  steps of 1/8 nat (`Quantum`), plus a quantized log prior per analysis. The columns are analyses (lemma, part of
  speech and features), so a table can hold the analyses of several lemmas of one form; a column with empty features
  stands for any features of that lemma and part of speech. Only features that were
  frequent enough in training have weights; collisions of hashes were resolved by the builder.

#### Key Methods

- `Extract(lower, index, features)`: writes the distinct window features in position order, then the left and right
  features that exist, and returns the count.
- `Score(cue, features, analyses, scores)`: looks up each feature hash by binary search; for each candidate analysis
  finds its column (`LemmaCue.RowOf`: among the columns of its lemma and part of speech whose feature items are all
  contained in the features of the analysis, the one with the most items; the empty-features column always qualifies)
  and scores
  `(prior + the sum of the weights of the found features) * 1/8`. A candidate analysis the table has no column for
  scores `NaN`, which the fusion fills. A feature without an entry adds nothing, so a word with no known feature scores
  its prior.
- `ExtractTense(lower, index, features)`: the features of `Extract`, then the segment edge feature (the left edge when no
  token with a letter stands before the target, the right edge when none stands after it), the left-window and
  right-window features in position order and the governing feature, and returns the count.
- `ScoreTense(cue, features)`: the log odds of past against non-past of a verb in its context: `(prior + the sum of the
  weights of the found features) * 1/8` over the tense table, by binary search of each feature hash.
- `ExtractNounVerb(lower, index, lexicon, features)`: the eight noun/verb features in a fixed order (first left, second
  left, first right, second right, each as a word feature and a class feature), and returns 8.
- `ScoreNounVerb(cue, features)`: the log odds of verb against noun that the context says: the sum of the weights of the
  found features `* 1/8` over the noun/verb table (no prior), by binary search of each feature hash.
- `HasLetter(token)`: whether a token contains a letter.

#### Error Handling

A null table or a default (uninitialized) analysis array throws `ArgumentNullException`; so does a null tense table, a null
noun/verb table or a null lexicon.
Missing analyses and unknown
features are normal and give `NaN` and no contribution.

#### Determinism and Thread Safety

The functions are pure and read only immutable tables, so equal input gives equal scores and any number of threads may
call them. They use the stack for scratch space.

#### Dependencies

`BasicTables` types (`LemmaCue`, `TenseCue`, `NounVerbCue`), `FeatureRef` (declared with `CueModel`) and the core `WordDetails`.

#### Callers

`BasicClassifier`, and the builder (`BasicTrainer` and `NounVerbStacker` extract the training features, cue, tense and
noun/verb, with the same code).

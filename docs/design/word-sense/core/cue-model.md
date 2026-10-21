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
- `HasLetter(token)`: whether a token contains a letter.

#### Error Handling

A null table or a default (uninitialized) analysis array throws `ArgumentNullException`. Missing analyses and unknown
features are normal and give `NaN` and no contribution.

#### Determinism and Thread Safety

The functions are pure and read only immutable tables, so equal input gives equal scores and any number of threads may
call them. They use the stack for scratch space.

#### Dependencies

`BasicTables` types (`LemmaCue`), `FeatureRef` (declared with `CueModel`) and the core `WordDetails`.

#### Callers

`BasicClassifier`, and the builder (`BasicTrainer` extracts the training features with the same code).

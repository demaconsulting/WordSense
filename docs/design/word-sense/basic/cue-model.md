### CueModel Unit Design

![Basic Structure](BasicView.svg)

#### Purpose

`CueModel` is the context model of the Basic model: it extracts the context features of a target word and scores the
senses of its lemma from the lemma's table of hashed features, a naive Bayes classifier. It separates senses that
share a part of speech, which the tagger cannot.

#### Data Model

- A feature is a `FeatureRef` of a kind and a lower-case token: a window feature (any distinct token that contains a
  letter within five positions of the target, the target excluded), a left feature (the token directly before the
  target) or a right feature (the token directly after it). The immediate neighbors are always written as left and
  right features, whatever they are (even punctuation); at most 12 features arise.
- The context is limited to the sentence segment the classifier passes in.
- A lemma's `LemmaCue` table holds ascending 16-bit feature hashes and one signed byte per feature and sense in steps of
  1/8 nat (`Quantum`), plus a quantized log prior per sense. Only features that were frequent enough in training have
  weights; collisions of hashes were resolved by the builder.

#### Key Methods

- `Extract(lower, index, features)`: writes the distinct window features in position order, then the left and right
  features that exist, and returns the count.
- `Score(cue, features, senses, scores)`: looks up each feature hash by binary search; for each candidate sense finds
  its column (part of speech and meaning) and scores `(prior + the sum of the weights of the found features) * 1/8`.
  A candidate the table has no column for scores `NaN`, which the fusion fills. A feature without an entry adds
  nothing, so a word with no known feature scores its prior.
- `HasLetter(token)`: whether a token contains a letter.

#### Error Handling

A null table or sense array throws `ArgumentNullException`. Missing senses and unknown features are normal and give
`NaN` and no contribution.

#### Determinism and Thread Safety

The functions are pure and read only immutable tables, so equal input gives equal scores and any number of threads may
call them. They use the stack for scratch space.

#### Dependencies

`BasicTables` types (`FeatureRef`, `LemmaCue`) and the core `WordDetails`.

#### Callers

`BasicClassifier`, and the builder (`BasicTrainer` extracts the training features with the same code).

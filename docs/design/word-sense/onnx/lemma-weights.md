### LemmaWeights Unit Design

![Onnx Structure](OnnxView.svg)

#### Purpose

`LemmaWeights` supplies the trained per-lemma classifiers: it reads the embedded weights blob, matches each lemma to
the word image, computes the feature of a pooled vector, scores the fine senses of a lemma, derives the group scores
and decides the level of an answer from the lemma's own thresholds.

#### Data Model

- `WeightsData(ModelId, InventoryHash, Dimension, Mean, Lemmas)`: the decoded blob; `InventoryHash` is the 32-byte hash
  of the spelling, analysis, feature, entry, sense, group and lemma-block sections of the word image the weights were
  trained for, `Mean` is the
  global (frozen) mean of the training vectors.
- `WeightedLemma(Lemma, SenseIds, FineThreshold, GroupThreshold, Biases, Weights, Choosers)`: one lemma. `SenseIds` are
  the fine sense ids (`lemma#slug`, the sense ids of the public API) in column order, so row `i` of the weights scores
  sense `i`. There is one one-versus-rest row and one bias per sense, and at least two senses. The thresholds are
  margins that are not negative: `FineThreshold` for the fine level and `GroupThreshold` for the group level, which is
  chosen and used only for a lemma that has groups. A level
  that never reached the precision target carries the largest finite float as its threshold ("never"), which no finite
  margin meets. For every lemma the number of `SenseIds` equals the number of rows; reader and writer reject any other
  entry.
- `FormChooser(Form, Threshold, Labels, Biases, Weights)`: an optional per-form classifier of a lemma entry for a form
  whose analyses belong to several lemmas or to several tenses of one lemma. A label names the lemma of an analysis or,
  for the forms of one lemma that differ in tense, the analysis (lemma and features). It has one one-versus-rest row
  and one bias per label and its own threshold. The chooser of a form is stored with the first lemma of the form in
  ordinal order and is found through the form.
- `WeightedLemmaTable`: the lemmas of the weights, found by lemma name, each with the indexes of its senses in the word
  image and the group of each sense (taken from the image, not stored in the blob); the choosers found by form.
- Blob format `WSX3` (Brotli): the magic `WSX3`, the model id, the 32-byte inventory hash of the word image, the
  dimension, a flag byte (0 for dense; sparse weights are not supported), the mean (little-endian float32), the lemma
  count, then per lemma in ordinal order: the lemma, `t_fine`, `t_group` (float32), the sense count `n`, the `n` sense
  ids, the `n` biases (float32), the `n` rows of the weights as float16 (IEEE half, little endian, row major), the
  chooser count and per chooser the form, its threshold, its label count `k`, the labels, the `k` biases and the `k`
  rows as float16. Reading widens every half to a float32, so scoring is plain float arithmetic. The half precision
  halves the size of the blob; the builder gates it: the measured fine accuracy of a model with the stored weights
  differs from that with full-precision weights by no more than 0.2 percentage point.
- The features are dense, finite and of unit length. `FeatureExtractor` computes them (global mean centering, then
  unit scaling) and is shared by training and inference; `VectorMath` holds the dot product. `FeatureExtractor`,
  `WeightedLemmaTable` and `VectorMath` are internal helpers of the classifier path; they stay in their own files (one
  responsibility each, shared with the builder) instead of being folded into `LemmaWeights`, and this unit's review
  set covers all of them.

#### Key Methods

- `WeightsBlob.Read`: decodes and validates the blob. It does not check the inventory hash against an image; it returns
  it, and `WeightedLemmaTable` checks it.
- `WeightedLemmaTable.LoadEmbedded(assembly, resource, modelId, hiddenSize)`: reads the blob and resolves every sense
  id against the word image; a different inventory hash, an unknown or duplicate sense or lemma, or a sense count that
  differs from the weights is a load failure (`InvalidDataException`). Nothing is dropped.
- `FeatureExtractor.Center(vector, mean)`: the feature of a pooled vector.
- Scoring:
  - `Scores` gives the fine scores of a lemma: for each sense the dot product of its row with the feature plus its
    bias (the one-versus-rest activation).
  - `Rank` orders the candidate senses by score and returns the best, the second best and the margin between them; a
    single candidate has an infinite margin.
  - `GroupScores` derives the score of each group of the candidates of a lemma that has groups: it takes the softmax of
    the candidates' fine scores and sums the probabilities of the senses of each group (a sense in no group stands
    alone). The groups are ranked and their margin is the
    difference of the best two group scores; a single group has an infinite margin. No group rows are stored.
  - `Candidates` ranks the senses of the lemma and part of speech of an analysis by the softmax of their fine
    scores: the probabilities sum to one over the listed senses and equal probabilities keep inventory order. They become
    the `Senses` of the `AnalysisCandidate` of that analysis (the probability of a sense given its analysis, the
    renormalization within the analysis being done by the classifier).
  - `Confidence(margin, threshold)` is a monotonic function of the margin, bounded to 0 to 1, 0 for a non-numeric
    margin and 1 for an infinite one; the confidence of a result is the probability of its answer at its level (see
    _OnnxClassifier Unit Design_), which this function orders but does not replace.
  - `Level` decides the kind of answer with the lemma's own `FineThreshold` and `GroupThreshold`, never shared
    values: the fine sense when the fine margin reaches `FineThreshold` and the best sense lies in the selected
    analysis, otherwise, for a best sense that is in a group, the group when the group margin reaches
    `GroupThreshold`, otherwise the part of speech. A threshold that is "never" cannot be met by any margin. A chooser
    is scored the same way over its labels with its own threshold.

#### Error Handling

`InvalidDataException` for a missing resource, a model id or dimension that differs from the expected one, an unknown
magic or a wrong version, a sparse flag, a corrupt, truncated, inconsistent or non-finite blob, a lemma with fewer than
two senses, an empty or duplicate sense id, a negative threshold, a malformed UTF-8 lemma, a zero dimension, a count
that disagrees with the data present, or trailing data. A feature of the wrong dimension is reported as a mismatch to
the caller, which then gives the word the lexicon-only answer.

#### Dependencies

`WordLexicon` (senses, groups and the inventory hash of the word image) and `PartOfSpeech`.

#### Callers

`OnnxWordSenseModel` (loads the table), `OnnxClassifier` (scores and decides the level) and the builder (writes and
trains the weights, and chooses the thresholds).

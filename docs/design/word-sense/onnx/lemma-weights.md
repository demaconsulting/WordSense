### LemmaWeights Unit Design

![Onnx Structure](OnnxView.svg)

#### Purpose

`LemmaWeights` supplies the trained per-lemma classifiers: it reads the embedded weights blob, matches each lemma to
the lexicon, computes the feature of a pooled vector and scores the senses of a lemma.

#### Data Model

- `WeightsData(ModelId, Dimension, Mean, Lemmas)`: the decoded blob; `Mean` is the global mean of the training
  vectors.
- `WeightedLemma(Lemma, Senses, Weights)` with `WeightedWord(Lemma, PartOfSpeech, Meaning)` senses, and the weights of
  one lemma: kind (`Binary` or `OneVersusRest`), abstain margin (not negative), biases and the row-major weight
  matrix.
- `WeightedLemmaTable`: the lemmas of the weights, found by lemma name, each with the indexes of its classes in the
  word image.
- Blob format `WSX2` (Brotli): model id, the 32-byte class-inventory hash of the word image, dimension, a flag byte
  (0 for dense; sparse weights are not supported), the
  mean (little-endian floats), the lemma count, then per lemma in ordinal order the lemma, the abstain margin, the
  kind, the class ids (`lemma#label`) of the senses and the biases and weights. The Binary kind needs exactly two
  classes. The classes are resolved against the image at load; a hash mismatch or an unknown class is a load failure.
- The features are dense, finite and of unit length. `FeatureExtractor` computes them (global mean centering, then
  unit scaling) and is shared by training and inference; `VectorMath` holds the dot product. `FeatureExtractor`,
  `WeightedLemmaTable` and `VectorMath` are internal helpers of the classifier path; they stay in their own files (one
  responsibility each, shared with the builder) instead of being folded into `LemmaWeights`, and this unit's review
  set covers all of them.

#### Key Methods

- `WeightsBlob.Read`: decodes and validates the blob.
- `WeightedLemmaTable.LoadEmbedded(assembly, resource, modelId, hiddenSize)`: reads the blob and resolves every class id
  against the word image; a different class hash, an unknown or duplicate class or lemma, or a class count that
  differs from the weights is a load failure (`InvalidDataException`). Nothing is dropped.
- `FeatureExtractor.Center(vector, mean)`: the feature of a pooled vector.
- Scoring: `Scores` gives the sense scores (a binary lemma gives the pair minus and plus the activation), `Rank`
  orders the candidate senses and `Confidence` turns the margin and abstain margin into a confidence.

#### Error Handling

`InvalidDataException` for a missing resource, a model id or dimension that differs from the expected one, an unknown
or sparse format, a corrupt, truncated, inconsistent or non-finite blob, or trailing data. A feature of the wrong
dimension is reported as a mismatch to the caller.

#### Dependencies

`WordLexicon` and `PartOfSpeech`.

#### Callers

`OnnxWordSenseModel` (loads the table), `OnnxClassifier` (scores) and the builder (writes and trains the weights).

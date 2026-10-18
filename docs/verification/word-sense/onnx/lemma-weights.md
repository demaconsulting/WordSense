### LemmaWeights Verification

#### Verification Approach

The unit covers `LemmaWeights` (scores, ranking and the confidence function), the `WeightsBlob` reader and the
`WeightedLemmaTable` that resolves weights against the word image. Tests in `test/DemaConsulting.WordSense.Onnx.Tests`
(`LemmaWeightsTests`, `WeightsBlobTests`, `WeightedLemmaTableTests`, `EmbeddedWeightsTests`) use hand-made weights, tiny
blobs built by a test helper (the writer lives in the builder) and a hand-made image. The
embedded-weights test uses the real weights of the model packages.

#### Test Environment

Standard xUnit v3 runner. No model files, no network access. `EmbeddedWeightsTests` reads the weights embedded in the
MiniLM and BertBaseCased assemblies.

#### Acceptance Criteria

All scenarios below pass on every platform and target framework of the CI matrix.

#### Test Scenarios

- **Scores and ranking**: for a binary and for a one-versus-rest weights set the scores equal the dot product plus the
  bias, the best sense and the margin to the second-best are returned, and a single sense has an infinite margin.
- **Confidence is monotonic**: the confidence never decreases as the margin grows, is clamped to 0 and 1, and equals 0.5
  where the margin equals the abstain margin.
- **Invalid weights are rejected**: a wrong shape, a non-finite value and a feature of the wrong length throw
  `ArgumentException`.
- **Blob round trip**: a small valid blob read by `WeightsBlob` gives the model id, dimension, mean vector and every
  lemma with its senses and weights.
- **Corrupt blob is rejected**: an unknown format marker (`WSX1`, `WSX3`), a sparse-weights flag, data cut at every
  byte (so at every section boundary), a lemma or sense count that disagrees with the data present, and trailing data
  each throw `InvalidDataException`. The reader does not check the class hash; it is returned as it is and
  `WeightedLemmaTable` rejects a foreign one.
- **Class ids and kinds are checked**: an empty or duplicate class id, a class count that does not fit the kind (a
  Binary lemma needs exactly two) and a zero dimension are rejected with `InvalidDataException`; for every model kind
  the writer refuses an entry whose class ids differ in number from the sense count of its weights (a Binary lemma with
  three classes, a one-versus-rest lemma with a class count that does not fit its weights), and the reader applies the
  same rule.
- **Resolution at load**: every class of the weights is found in the word image by id (`KnownClasses_AreFoundById`);
  another class hash, an unknown class, a duplicate class or lemma and a class count mismatch each throw.
- **Embedded weights are checked on load**: weights trained for another model id, another dimension or another class
  inventory (class hash) are refused with `InvalidDataException`, as is a class that the image does not know, and a
  missing resource is reported.
- **Shipped weights resolve completely**: the embedded weights of MiniLM and of BertBaseCased carry the class hash of
  the shipped image and every class resolves (`EmbeddedWeights_*_MatchesWordImage`).

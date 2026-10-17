### LemmaWeights Verification

#### Verification Approach

The unit covers `LemmaWeights` (scores, ranking and the confidence function), the `WeightsBlob` reader and the
`WeightedLemmaTable` that matches weights to the lexicon. Tests in `test/DemaConsulting.WordSense.Onnx.Tests`
(`LemmaWeightsTests`, `WeightsBlobTests`, `WeightedLemmaTableTests`, `EmbeddedWeightsTests`) use hand-made weights, tiny
blobs built by a test helper (the writer lives in the builder) and a hand-made lexicon for the all-or-nothing match. The
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
- **Corrupt blob is rejected**: an unknown format marker, truncated data, inconsistent counts and trailing data each throw
  `InvalidDataException`.
- **All-or-nothing match**: a lemma whose senses equal the lexicon (lemma, part of speech and meaning, in the same
  order) is kept; if any sense differs, or the lemma is absent from the lexicon, the whole lemma is dropped and listed in
  the dropped names.
- **Embedded weights are checked on load**: weights trained for another model id or another dimension are refused with
  `InvalidDataException`, and a missing resource is reported.
- **Shipped weights drop zero lemmas**: the embedded weights of MiniLM and of BertBaseCased, matched against the embedded
  lexicon, have `DroppedLemmas` equal to 0.

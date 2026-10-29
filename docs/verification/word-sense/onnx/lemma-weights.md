### LemmaWeights Verification

#### Verification Approach

The unit covers `LemmaWeights` (fine scores, group scores, ranking, levels and the confidence function), the
`WeightsBlob` reader and the `WeightedLemmaTable` that resolves weights against the word image. Tests in
`test/DemaConsulting.WordSense.Onnx.Tests` (`LemmaWeightsTests`, `WeightsBlobTests`, `WeightedLemmaTableTests`,
`FeatureExtractorTests`, `VectorMathTests`, `EmbeddedWeightsTests`) use hand-made weights, tiny blobs built by a test
helper (the writer lives in the builder and is tested through the reader) and a hand-made image. The embedded-weights
test uses the real weights of the model packages.

#### Test Environment

Standard xUnit v3 runner. No model files, no network access. `EmbeddedWeightsTests` reads the weights embedded in the
MiniLM and BertBaseCased assemblies.

#### Acceptance Criteria

All scenarios below pass on every platform and target framework of the CI matrix.

#### Test Scenarios

- **Fine scores and ranking** (`Scores_OneVersusRest_ReturnsOneScorePerRow`, `Rank_ReturnsBestAndMargin`): the scores
  equal the dot product plus the bias, one per sense row, the best sense and the margin to the second-best are returned,
  and a single candidate has an infinite margin.
- **Group scores are derived from the fine scores** (`GroupScores_SumTheFineProbabilitiesOfTheGroup`): the score of a
  group equals the sum of the normalized fine probabilities of its candidate senses, the group scores sum to one, and a
  single
  group has an infinite margin.
- **Confidence is monotonic** (`Confidence_IsMonotonicAndClamped`): the confidence never decreases as the margin grows
  and is clamped to 0 and 1 (the confidence of a result is the probability of its answer, which the classifier
  verifies).
- **Level uses the lemma's own thresholds** (`Level_UsesTheLemmasOwnThresholds`): two lemmas with the same scores and
  different thresholds are answered at different levels; a threshold of "never" is not met by a finite margin.
- **Invalid weights are rejected**: a wrong shape, a non-finite value and a feature of the wrong length throw
  `ArgumentException`.
- **Feature extraction** (`FeatureExtractor_Center_Vector_SubtractsTheMeanAndScalesToUnitLength`,
  `FeatureExtractor_Center_EmptyMean_ScalesThePooledVector`, `FeatureExtractor_Center_VectorEqualToMean_ReturnsNull`,
  `FeatureExtractor_Center_NonFiniteValue_ReturnsNull`, `FeatureExtractor_Center_MeanOfOtherDimension_Throws`,
  `FeatureExtractor_Center_NullVector_Throws`, `FeatureExtractor_Center_EmptyVector_ReturnsNull`; tests in
  `FeatureExtractorTests`): a pooled vector of (4, 6) with the mean (1, 2) gives the unit feature (0.6, 0.8); an empty
  mean subtracts nothing and the pooled array is not changed; a vector equal to the mean, an empty vector and a vector
  or mean with a NaN or infinite value give null (never a NaN feature); a mean whose length differs from the vector
  (shorter or longer) throws `ArgumentException` naming `mean`, and a null vector throws `ArgumentNullException`.
- **Dot product** (`VectorMath_Dot_LengthsAroundTheSimdWidth_EqualTheScalarSum`,
  `VectorMath_Dot_ShorterFirstVector_UsesItsLength`, `VectorMath_Dot_EmptyVectors_IsZero`,
  `VectorMath_Dot_RightShorterThanLeft_Throws`; tests in `VectorMathTests`): with small integer values (exact in single
  precision) the dot product equals the scalar sum for a length of one, one below, at and one above the SIMD width,
  below and above two and three widths with a remainder, and 384, 385 and 768 values; extra values after the length of
  the first vector are ignored; two empty vectors give zero; a second vector shorter than the first throws
  `ArgumentException` naming `right`, with and without a SIMD block.
- **Blob round trip** (`WordSenseBuilder_WeightsBlobWriter_Write_RoundTripsThroughTheReader`,
  `WeightsBlob_Read_FineSenseLemma_KeepsSenseIds`, `WeightsBlob_Read_Thresholds_AreKept`,
  `WeightsBlob_Read_FormChooser_IsKept`): a small valid blob read by `WeightsBlob` gives the model id, inventory hash,
  dimension, mean vector and every lemma with its sense ids in column order, biases, rows, both thresholds and its form
  choosers; a blob without lemmas gives empty weights (`WeightsBlob_Read_NoLemmas_ReturnsEmptyWeights`,
  `WordSenseBuilder_WeightsBlobWriter_Write_NoLemmas_RoundTrips`).
- **Reduced precision stays within the gate** (`WordSenseBuilder_WeightsBlobWriter_ReducedPrecision_StaysWithinTheAccuracyGate`):
  the weights stored as float16 change the measured fine accuracy of a model by no more than 0.2 percentage point
  against full precision.
- **Corrupt blob is rejected** (`WeightsBlob_Read_NotCompressed_Throws`, `UnknownMagic_Throws`, `Truncated_Throws`,
  `TruncatedAtEveryLength_Throws`, `CountMismatch_Throws`, `WrongVersionOrFlag_Throws`, `MalformedUtf8Lemma_Throws`,
  `TrailingBytes_Throws`): data that is not compressed, an unknown format marker (`WSX1`, `WSX2`), a wrong version or a
  sparse-weights flag, data cut at every byte (so at every section boundary), a lemma or sense count that disagrees
  with the data present, a malformed lemma and trailing data each throw `InvalidDataException`. The reader does not check
  the inventory hash; it is returned as it is (`AnyInventoryHash_IsReturnedUnchecked`) and `WeightedLemmaTable`
  rejects a foreign one.
- **Sense ids and thresholds are checked** (`LemmaWithOneSense_Throws`, `EmptyOrDuplicateSenseId_Throws`,
  `NegativeThreshold_Throws`, `ZeroDimension_Throws`, `TwoLemmaBlob_IsAccepted`,
  `WordSenseBuilder_WeightsBlobWriter_Write_SenseCountMismatch_Throws`): a lemma with fewer than two senses, an empty or
  duplicate sense id, a negative threshold and a zero dimension are rejected with `InvalidDataException`; a valid blob
  of two lemmas is accepted; the writer refuses an entry whose sense ids differ in number from the rows of its weights,
  and the reader applies the same rule.
- **Resolution at load** (`WeightedLemmaTable_Constructor_KnownSenses_AreFoundById`,
  `OtherInventoryHash_ThrowsNamingBothHashes`, `UnknownSense_Throws`, `DuplicateSense_Throws`, `DuplicateLemma_Throws`,
  `SenseCountMismatch_Throws`, `GroupThresholdWithoutGroupedSenses_Throws`): every sense of the weights is found in the
  word image by id; another inventory hash (the
  message names both hashes), an unknown sense, a duplicate sense or lemma, a sense count mismatch and a group
  threshold for a lemma none of whose senses is in a group each throw.
- **Weight shapes** (`LemmaWeights_Constructor_InvalidShape_Throws`): a dimension that is zero or negative, one row,
  a mismatching weight count and non-finite values are rejected with `ArgumentException`.
- **Embedded weights are checked on load** (`WeightedLemmaTable_LoadEmbedded_WrongModelId_Throws`): weights trained for
  another model id, another dimension or another inventory are refused with `InvalidDataException`, and a missing
  resource is reported.
- **Embedded weights are read at their exact size** (`WeightedLemmaTable_ReadResource_ReturnsExactlyTheBytesOfTheResource`):
  a resource whose size is just above a power of two is returned as an array of exactly its length with the same
  bytes, and a missing resource is reported as invalid data.
- **Shipped weights resolve completely** (`EmbeddedWeights_MiniLM_MatchesWordImage`,
  `EmbeddedWeights_BertBaseCased_MatchesWordImage`): the embedded weights of MiniLM and of BertBaseCased carry the
  inventory hash of the shipped image and every sense of every lemma resolves.

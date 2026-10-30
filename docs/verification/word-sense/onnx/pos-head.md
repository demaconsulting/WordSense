### PosHead Verification

#### Verification Approach

The unit covers the `PosHeadBlob` reader, the binding checks and the arithmetic of `PosHead`. Tests in
`test/DemaConsulting.WordSense.Onnx.Tests` (`PosHeadBlobTests`, `PosHeadTests`, `EmbeddedHeadTests`) use a tiny head of
hidden size 4 whose weights are multiples of 0.25, which half precision holds exactly. The test-local writer
`HeadBlobWriter` builds the blobs; the production writer lives in the builder and is tested through the reader
(`WordSenseBuilder_PosHeadBlobWriter_Write_RoundTripsThroughTheReader`). Damaged blobs are made by flipping bytes,
cutting the blob and rewriting the section list; where a test needs a valid trailing hash it recomputes it. The
arithmetic is checked against an independent computation with plain loops over the same weights. The embedded-head test
uses the real heads of the model packages.

#### Test Environment

Standard xUnit v3 runner. No model files, no network access. `EmbeddedHeadTests` reads the heads embedded in the MiniLM
and BertBaseCased assemblies and the pinned digests of their ONNX files from the model descriptions; it does not open
the ONNX files.

#### Acceptance Criteria

All scenarios below pass on every platform and target framework of the CI matrix.

#### Test Scenarios

- **Blob round trip** (`PosHeadBlob_Read_RoundTrip_ReturnsEveryField`,
  `WordSenseBuilder_PosHeadBlobWriter_Write_RoundTripsThroughTheReader`): a written head reads back with its model id,
  hidden size, encoder digest, feature layout, class names, training-file digest, regularization strength, variant,
  weights (the half-precision values) and biases.
- **Class names** (`PosHeadBlob_ExpectedClassNames_AreThePartsOfSpeechInOrder`): the expected names are `Noun`,
  `Pronoun`, `Verb`, `Adjective`, `Adverb`, `Preposition`, `Conjunction`, `Interjection`, `Determiner` and `Auxiliary`,
  in that order.
- **Damaged blob is rejected** (`PosHeadBlob_Read_FlippedByte_ThrowsChecksum`,
  `PosHeadBlob_Read_BadMagicOrTruncated_Throws`): a flipped byte near the start, in the middle, before the hash and in the
  hash fails with a message about the checksum; another magic, an empty blob and a blob cut at several lengths throw
  `InvalidDataException`.
- **Structure is checked behind a valid hash** (`PosHeadBlob_Read_TruncatedBodyWithValidHash_Throws`,
  `PosHeadBlob_Read_InvalidSections_Throws`, `PosHeadBlob_Read_NoHeadSection_Throws`): a body cut at several lengths and
  hashed again, a body with a trailing byte, one weight too few, a second section with the id of the head, a section list
  without the head, a NaN bias and an infinite weight each throw `InvalidDataException`.
- **Bounds and varints** (`PosHeadBlob_Read_OversizedHiddenSize_ThrowsInvalidData`,
  `PosHeadBlob_Read_MaximumHiddenSizeWithoutWeights_ThrowsInvalidData`,
  `PosHeadBlob_Read_VarintWithInvalidFifthByte_ThrowsInvalidData`,
  `PosHeadBlob_Read_VarintOfMaximumInt_IsReadAsThatNumber`): a hidden size of 16,385, of 700,000,000, of 1,000,000,000
  and of 2,147,483,647 throws exactly `InvalidDataException` (not an `OverflowException`), also through the binding; the
  largest hidden size passes the bounds and fails only for its missing weights; a feature layout whose fifth byte sets a
  bit above bit 30 or continues (which would wrap to the valid layout 1) throws `InvalidDataException`; the layout
  2,147,483,647 in five bytes reads as that number.
- **Unknown sections** (`PosHeadBlob_Read_UnknownSection_SkippedWhenOptionalRejectedWhenRequired`): an unknown section
  marked optional is skipped and the head reads as before; the same section marked required throws, and the message
  names the section.
- **Binding** (`PosHead_LoadEmbedded_MatchingBinding_Loads`, `PosHead_LoadEmbedded_WrongEncoderDigest_Throws`,
  `PosHead_LoadEmbedded_WrongClassOrder_Throws`, `PosHead_LoadEmbedded_WrongModelSizeOrLayout_Throws`,
  `PosHead_LoadEmbedded_MissingResource_Throws`): a head with the matching model id, hidden size, encoder digest and
  classes loads and reports its hidden size and class count; another encoder digest (the message names the encoder file),
  two classes swapped, a renamed class, another model id, another hidden size, another feature layout and a missing
  resource each throw `InvalidDataException`.
- **Posterior** (`PosHead_Posterior_TinyHead_MatchesIndependentComputation`): the probabilities of the tiny head equal
  the independent computation to 12 decimals and sum to one.
- **Missing vectors are zeros** (`PosHead_Posterior_MissingVectors_AreZeros`): a null neighbor or word gives the same
  probabilities as a vector of zeros, and the probabilities of a null word with null neighbors equal those of three
  zero vectors.
- **Determinism** (`PosHead_Posterior_RepeatedCalls_AreIdentical`): the same input gives bit-identical probabilities on
  two calls.
- **Invalid use** (`PosHead_Posterior_WrongLength_Throws`, `PosHead_NullArguments_Throw`): a vector that is too long or
  too short throws `ArgumentException`; a null posterior or head data throws `ArgumentNullException`.
- **Restriction** (`PosHead_Restrict_AllowedMask_RenormalizesAllowed`): with noun and verb allowed, the two kept
  probabilities are divided by their sum, they sum to one and the other parts of speech are zero.
- **Restriction falls back** (`PosHead_Restrict_NothingAllowed_FallsBackToPosterior`): a mask that allows nothing, and a
  mask that allows only a part of speech of probability zero, return the posterior unchanged, as a new array.
- **Shipped heads are bound to their encoders** (`EmbeddedHead_MiniLM_MatchesEncoderAndClasses`,
  `EmbeddedHead_BertBaseCased_MatchesEncoderAndClasses`, `EmbeddedHead_AgainstTheOtherEncoder_IsRejected`): the head
  embedded in each package loads through the model, carries the model id, hidden size (384 and 768), class names and
  variant `word+-1`, records the pinned digest of the ONNX file of its package, gives probabilities that sum to one and
  a restriction to the noun that gives the noun probability one; the head of MiniLM read against the digest of the BERT
  encoder, or against the model id of BERT, is rejected.

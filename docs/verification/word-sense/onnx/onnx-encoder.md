### OnnxEncoder Verification

#### Verification Approach

Tests in `test/DemaConsulting.WordSense.Onnx.Tests/OnnxEncoderTests.cs` run the real MiniLM int8 model and vocabulary
through `ModelAssets` (skipped when the Git LFS content is absent), because the encoder's contract is the shape and
determinism of the vectors of a real ONNX session. Constructor validation scenarios that need no real model run without
it. ONNX Runtime is limited to two intra-op threads in the tests. The windows are checked with small maximum sequence
lengths (16 and 20) so that a short text needs several windows. `PossessiveViewTests.cs` tests the possessive view
without a model, against the lexicon of the core package.

#### Test Environment

Standard xUnit v3 runner. `model/all-MiniLM-L6-v2-int8/` (Git LFS) for the session scenarios. No network access.

#### Acceptance Criteria

All scenarios below pass on every platform and target framework of the CI matrix.

#### Test Scenarios

- **Deterministic vectors**: encoding the same words and targets twice gives identical vectors, one per target, each of
  the model's hidden size (384 for MiniLM).
- **No targets or no tokens**: no targets give an empty result and a target without word pieces gives a null entry.
- **Long text is windowed**: a text longer than the maximum sequence length still returns a vector for each target.
- **Concurrent calls**: parallel calls on one encoder give the same vectors as sequential calls.
- **All words of a short text** (`OnnxEncoder_EncodeAllAsync_ShortText_EqualsSinglePassVectors`,
  `OnnxEncoder_EncodeAllAsync_EmptyOrNoTokens_ReturnsEmptyOrNull`): for a text that fits one window every word gets
  exactly the vector that `EncodeAsync` gives it, from one encoder pass; an empty text gives no vectors and a word
  without word pieces gives null.
- **All words of a long text** (`OnnxEncoder_EncodeAllAsync_LongText_UsesOnePassPerWindow`): 60 one-piece words with a
  window of 14 word pieces take 8 passes (windows start at 0, 7, ..., 42 and at the last possible start 46), not 60, and
  every word gets a vector.
- **Vectors stay close to a centered window** (`OnnxEncoder_EncodeAllAsync_LongText_StaysCloseToSinglePassAtWindowBorders`):
  for about 50 words and a window of 20 word pieces the windowed vectors have a mean cosine above 0.85 to the vectors
  that a window centered on each word gives, none below 0.5, and the first and the last word, which keep the text edge as
  context, are above 0.9.
- **Each word is pooled from its most central window** (`OnnxEncoder_EncodeAllAsync_LongText_PoolsEachWordFromItsMostCentralWindow`):
  the vector of every word equals, bit for bit, the vector of the window that an independent rule picks (the largest
  smaller side of context, the text edge unlimited, the earliest window on a tie), encoded on its own.
- **Possessive view** (`OnnxEncoder_EncodeAllAsync_PossessiveView_PoolsStemAndMarkerApart`): for `The technician's tool
  broke .` the view has the tokens `The technician 's tool broke .`, the first-token indexes 0, 1, 3, 4, 5, the vectors
  of the split text, and the vector of the stem differs from that of the joined word.
- **Possessive split** (`PossessiveView_Expand_Possessives_SplitsStemAndMarker`,
  `PossessiveView_Expand_NoPossessives_IsIdentity`): a possessive with a straight or a typographic apostrophe is split
  into stem and marker and every word maps to its first token; words that the predicate does not call possessive, a
  word that is only its marker and an empty text stay as they are.
- **Possessive by lexicon** (`PossessiveView_IsLexiconPossessive_DecidesByLexicon`, `PossessiveView_NullArguments_Throw`):
  `technician's`, `Technician’s` and `sergeants'` are possessives; `it's`, `don't`, `record`, `'s`, `xyzzyq's` and `.`
  are not; a null list, predicate or word throws `ArgumentNullException`.
- **Invalid use**: null words or targets, a target index outside the word list, a cancelled token and use after disposal
  each throw (`OnnxEncoder_EncodeAsync_InvalidUse_Throws`); `EncodeAllAsync` likewise rejects a null list, a null word,
  a null predicate, a cancelled token and use after disposal (`OnnxEncoder_EncodeAllAsync_InvalidUse_Throws`).
- **Constructor validation**: a missing model file throws `FileNotFoundException`; a file that is not an ONNX model is
  rejected with an exception; a sequence length outside 8 to 512 or a negative thread count is rejected.

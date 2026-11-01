### OnnxEncoder Verification

#### Verification Approach

Tests in `test/DemaConsulting.WordSense.Onnx.Tests/OnnxEncoderTests.cs` run the real MiniLM int8 model and vocabulary
through `ModelAssets` (skipped when the files have not been fetched), because the encoder's contract is the shape and
determinism of the vectors of a real ONNX session. Constructor validation scenarios that need no real model run without
it. ONNX Runtime is limited to two intra-op threads in the tests. The windows are checked with small maximum sequence
lengths (16 and 20) so that a short text needs several windows. `PossessiveViewTests.cs` tests the possessive view
without a model, against the lexicon of the core package. `OnnxEncoderLayerTests.cs` tests the sense layers with a tiny
tapped graph that `TappedGraph` writes as ONNX bytes: the hidden state of layer `n` at a token is the token id times a
constant vector of the layer, so every expected vector is computed without a model file.

#### Test Environment

Standard xUnit v3 runner. `model/all-MiniLM-L6-v2/v1/` (fetched) for the session scenarios. No network access.

#### Acceptance Criteria

All scenarios below pass on every platform and target framework of the CI matrix.

#### Test Scenarios

- **Sense layers** (`OnnxEncoder_EncodeAsync_SenseLayers_ReturnsMeanOfLayers`,
  `OnnxEncoder_EncodeAllAsync_SenseLayers_EqualsEncodeAsyncAndWindows`): with the layers 9 to 12 the sense vector of a
  word of one piece and of a word of two pieces equals the mean of the four per-layer means, and the vector of a text
  in windows of 8 tokens equals the vector of the same word asked alone.
- **Head vector is the last layer** (`OnnxEncoder_EncodeHeadAllAsync_SenseLayers_ReturnsLastLayerUnchanged`,
  `OnnxEncoder_EncodeAsync_NoSenseLayers_ReturnsLastLayer`): the head vectors of the layered encoder are bit-identical
  to the vectors of an encoder without sense layers on the same file, the view of the layered encoder has the last layer
  as `Vectors` and the layer mean as `SenseVectors`, and an encoder without sense layers has no sense vectors in the
  view.
- **Missing tap** (`OnnxEncoder_Constructor_MissingTapOutput_ThrowsInvalidData`,
  `OnnxWordSenseModel_ClassifyAsync_MissingTapOutput_ThrowsModelIntegrity`): a graph without `hidden_10`, and a graph
  without taps, are rejected with the name of the missing output; a model whose description has sense layers reports it
  as
  `ModelIntegrityException` and releases the encoder, and the same description over a graph with every tap answers.
- **Invalid sense layers** (`OnnxEncoder_Constructor_InvalidSenseLayers_ThrowsArgument`,
  `FeatureLayout_Describe_Layers_ReturnsLayout`): one layer, no layer, layers that do not increase and a layer below 1
  are rejected; the layout of the layers is `last`, a range for consecutive layers and a list otherwise.
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
- **No word that fits a window is cut** (`OnnxEncoder_PlanWindows_WordStraddlingGridBorder_GetsWindowThatContainsIt`,
  `OnnxEncoder_PlanWindows_WordNearTextEnd_AddedWindowStaysInsideText`,
  `OnnxEncoder_PlanWindows_RandomWordLengths_EveryFittingWordIsContained`): with a window of 14 word pieces, a word at
  the pieces 6 to 14 straddles the grid windows at 0 and 7, so the plan has the windows 0, 6, 7, 14 and 21 and the word
  is assigned to the window 6; the same holds next to the end of the text; and for 300 random mixes of word lengths and
  window sizes every word that fits is inside its window, the windows are increasing and distinct, the first starts at 0
  and the last ends at the end of the text.
- **A word that cannot fit** (`OnnxEncoder_PlanWindows_WordLongerThanWindow_StartsItsOwnWindow`,
  `OnnxEncoder_EncodeAllAsync_WordLongerThanWindow_IsPooledOverItsFirstPieces`): a word of 20 pieces with a window of 14
  gets a window that starts at the word, a word without pieces gets none, and the vector of the word equals, bit for
  bit, that of its first 14 pieces encoded alone.
- **A straddling word is pooled over all its pieces** (`OnnxEncoder_EncodeAllAsync_WordStraddlingWindowBorder_IsPooledOverAllItsPieces`):
  the vector of a word of 9 pieces at the pieces 6 to 14 equals, bit for bit, that of the window that starts at the
  word, encoded on its own.
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

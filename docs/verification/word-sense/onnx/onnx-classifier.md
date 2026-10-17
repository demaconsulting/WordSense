### OnnxClassifier Verification

#### Verification Approach

Tests in `test/DemaConsulting.WordSense.Onnx.Tests/OnnxClassifierTests.cs` build the classifier over the real MiniLM int8
encoder (through `ModelAssets`, skipped when the Git LFS content is absent) with hand-made weights matched against the
real embedded lexicon, so the expected sense and confidence follow from the arithmetic; scenarios whose words need no
context do not depend on the model at all. Substitute weights are used so that a specific sense wins. Real-model accuracy
is verified in the MiniLM and BertBaseCased subsystem documents.

#### Test Environment

Standard xUnit v3 runner. No network access. The real-model scenarios need `model/all-MiniLM-L6-v2-int8/` (Git LFS) and
are skipped when it is absent; the others need no model files.

#### Acceptance Criteria

All scenarios below pass on every platform and target framework of the CI matrix.

#### Test Scenarios

- **Unknown word**: a word with no lexicon entry is `(word, null, 0)` and needs no encoder result.
- **Single-sense word**: a word with one entry is `(word, entry, 1.0)` and needs no encoder result.
- **Encoder only when needed**: the encoder runs once for a text in which at least one word has two or more candidates
  and intact weights, and never for a text in which no word does.
- **Context-resolved word**: with two or more candidates and intact weights, `Details` is the best-scoring sense even when
  the confidence is low, and only the word's own senses compete (the senses of its lemma that the surface form cannot
  have are ignored).
- **Confidence is monotonic in the margin**: for a growing margin between the best and second-best score the confidence
  never decreases, is clamped to 0 and 1, and equals 0.5 where the margin equals the lemma's abstain margin.
- **Missing weights**: a word with two or more senses whose lemma has no weights is `(word, null, 0)`, and the encoder is
  not asked for it.
- **Encoder returns no vector**: a target with no word pieces, or a vector of the wrong length, leaves the word unresolved.
- **Words are returned as written**: the result array is parallel to the input, holds each word as given, and is
  case-insensitive in lookup (including the curly apostrophe).
- **Argument validation and cancellation**: a null word list and a null word throw `ArgumentException` or
  `ArgumentNullException`; a cancelled token throws `OperationCanceledException`.

## Onnx Subsystem Verification Design

This document describes the verification of the Onnx subsystem, the package `DemaConsulting.WordSense.Onnx`: the
shared implementation of the encoder models. It holds the abstract `OnnxWordSenseModel`, the classifier, the encoder,
the WordPiece tokenizer, the model downloader with its mirror, and the lemma weights.

### Verification Approach

The subsystem is verified at three levels. The pure units (`WordPieceTokenizer`, `LemmaWeights` with its blob and table,
`DownloadMirror`) are verified with hand-built inputs. The `ModelDownloader` is verified against a loopback
`HttpListener` (`LocalServer`) that serves small files, failures, corrupt files and redirects; pinned digests are
substituted for the test files, so no test reaches a public host. The model-level units (`OnnxWordSenseModel`,
`OnnxClassifier`, `OnnxEncoder`) are verified with fake weights (with chosen thresholds, so that each answer level is
reached), the Basic model as the fallback and either a fake encoder input or the committed int8 model through the
helper `ModelAssets`, which skips a test when the Git LFS content is absent. Tests live in
`test/DemaConsulting.WordSense.Onnx.Tests`; the concrete model packages are verified in their own subsystem documents.

### Test Environment

Standard xUnit v3 runner on net8.0, net9.0 and net10.0. The
downloader tests open a loopback HTTP listener on an ephemeral port and a temporary store directory. The real-model
tests need `model/all-MiniLM-L6-v2-int8/` (Git LFS) and are skipped when it is absent. Environment variables are never
modified: the mirror environment is read through an injected getter.

### Acceptance Criteria

All subsystem scenarios pass on every platform and target framework of the CI matrix, and the unit scenarios of the
seven units pass.

### Test Scenarios

#### Words Resolve Only As Far As Needed

A text with single-sense words, unknown words, untrained ambiguous words and one trained ambiguous word runs the
encoder only for the trained word, and a text without a trained ambiguous word never runs the encoder and needs no model
file (`OnnxClassifier_ClassifyAsync_OnlyAmbiguousWordsAreEncoded`; see the `OnnxClassifier` and `OnnxWordSenseModel`
unit documents).

#### Words Are Answered at the Level the Lemma's Thresholds Allow

A trained word is answered at the sense level when its fine margin reaches the lemma's fine threshold
(`OnnxClassifier_ClassifyAsync_MarginAboveFineThreshold_AnswersSense`), otherwise at the group level when its group
margin reaches the group threshold
(`OnnxClassifier_ClassifyAsync_FineBelowAndGroupAboveThreshold_AnswersGroup`), otherwise with the best group and a
confidence below one half (`OnnxClassifier_ClassifyAsync_BothBelowThreshold_AnswersBestGroupBelowOneHalf`).

#### Untrained Words Fall Back to the Part of Speech

An ambiguous word whose lemma has no usable weights is answered with the analysis of the Basic model at the part of
speech level, never with a guessed fine sense
(`OnnxClassifier_ClassifyAsync_LemmaWithoutWeights_AnswersPartOfSpeechFromBasic`).

#### Confidence Grows with the Margin

The confidence of a context-resolved word is monotonic in the margin of the answered level and is one half at the
lemma's threshold for that level
(`OnnxClassifier_ClassifyAsync_LargerMargin_GivesHigherConfidence`,
`OnnxClassifier_ClassifyAsync_MarginAtThreshold_GivesConfidenceOneHalf`; see the `OnnxClassifier` and `LemmaWeights` unit
documents).

#### A Form of Several Lemmas Gets Its Lemma from the Context

A form such as `left` is answered with the senses of the lemma that the form chooser picks from the sentence
(`OnnxClassifier_ClassifyAsync_FormOfTwoLemmas_ChoosesTheLemmaFromContext`).

#### Weights Match the Word Image

Weights are matched to the word image by sense id and by the hash of the sense, group and analysis inventory; any
mismatch is a load failure and nothing is dropped. The weights embedded in the model packages resolve completely against
the shipped image (`EmbeddedWeights_MiniLM_MatchesWordImage`; see the `LemmaWeights` unit document).

#### Model Files Come From a Verified Source

Model files are obtained from the mirror and then GitHub LFS, verified against their SHA-256 digest, stored atomically
under `<store>/<model-id>/`, and downloaded once even when requested concurrently (see the `ModelDownloader` and
`DownloadMirror` unit documents).

#### Disposal Releases the Encoder and the Fallback Model

Disposing a model releases the encoder session and the fallback model it owns, deferring the release while a
classification runs, and is harmless when repeated
(`OnnxWordSenseModel_Dispose_CalledTwice_IsHarmless`, `OnnxWordSenseModel_Dispose_ReleasesTheFallbackModel`; see the
`OnnxWordSenseModel` unit document).

#### Tokenization Equals the Reference

The WordPiece tokenizer reproduces the reference Hugging Face tokenizer for the uncased vocabulary, and honours the
cased setting (see the `WordPieceTokenizer` unit document).

#### XML Documentation Examples Compile

Every `<code>` example of the shipped XML documentation file is extracted and compiled against the real public API
(`XmlDocExampleTests`).

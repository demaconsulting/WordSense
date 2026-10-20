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
reached), a test double standing in for a fallback model where a scenario needs one, and either a fake encoder input or
the committed int8 model through the
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
(`OnnxClassifier_ClassifyAsync_MarginAboveFineThreshold_AnswersSense`), otherwise, for a lemma that has groups and a
best sense in a group, at the group level, with the group id only, when its group
margin reaches the group threshold
(`OnnxClassifier_ClassifyAsync_FineBelowAndGroupAboveThreshold_AnswersGroup`) and with a lower confidence when it does
not (`OnnxClassifier_ClassifyAsync_BothBelowThreshold_AnswersBestGroupBelowOneHalf`); a lemma without groups steps from
the sense to the part of speech, never to a group
(`OnnxClassifier_ClassifyAsync_LemmaWithoutGroups_StepsFromSenseToPartOfSpeech`). A known word is never answered at
the level none. These scenarios belong to the increment that adds the scoring of fine senses with the encoder.

#### Analyses and Senses Are Ranked and Sum to One

A trained lemma lists every analysis of the word with its probability (summing to one) and, inside each analysis, the
ranked senses of its lemma and part of speech with their probabilities given the analysis (summing to one), whatever
the level answered; a single-sense word and a lemma without weights list no scored senses
(`OnnxClassifier_ClassifyAsync_TrainedLemma_ListsRankedCandidatesSummingToOne`,
`OnnxClassifier_ClassifyAsync_SingleSenseWord_ListsNoCandidates`,
`OnnxClassifier_ClassifyAsync_LemmaWithoutWeights_ListsNoCandidates`).

#### Words Without Scored Senses Are Answered from the Lexicon

A known word whose lemma has no usable weights is answered from the lexicon alone: its analyses are equally likely,
the first is selected, no senses are scored and the level is the part of speech, never a guessed fine sense (a word
with a single analysis is certain about it, and at the sense level when its lemma and part of speech have exactly one
sense), and without any other model package
(`OnnxClassifier_ClassifyAsync_LemmaWithoutWeights_AnswersAnalysesWithoutScoredSenses`,
`OnnxClassifier_ClassifyAsync_SingleAnalysisSeveralSenses_AnswersPartOfSpeech`,
`OnnxClassifier_ClassifyAsync_SeveralAnalysesFirstWithOneSense_StaysAtPartOfSpeech`,
`OnnxClassifier_ClassifyAsync_SeveralAnalysesLaterWithOneSense_StaysAtPartOfSpeech`). The package references only the
core package (`OnnxWordSense_Assembly_ReferencesOnlyCore`).

#### Confidence Grows with the Margin

The confidence of a context-resolved word is the probability of its answer at its level and is monotonic in the margin
of the answered level
(`OnnxClassifier_ClassifyAsync_LargerMargin_GivesHigherConfidence`; see the `OnnxClassifier` and `LemmaWeights` unit
documents).

#### A Form of Several Lemmas Gets Its Lemma from the Context

A form such as `left` is answered with the senses of the lemma that the form chooser picks from the sentence
(`OnnxClassifier_ClassifyAsync_FormOfTwoLemmas_ChoosesTheLemmaFromContext`).

#### Weights Match the Word Image

Weights are matched to the word image by sense id and by the inventory hash of the image; any
mismatch is a load failure and nothing is dropped. The weights embedded in the model packages resolve completely against
the shipped image (`EmbeddedWeights_MiniLM_MatchesWordImage`; see the `LemmaWeights` unit document).

#### Model Files Come From a Verified Source

Model files are obtained from the mirror and then GitHub LFS, verified against their SHA-256 digest, stored atomically
under `<store>/<model-id>/`, and downloaded once even when requested concurrently (see the `ModelDownloader` and
`DownloadMirror` unit documents).

#### Disposal Releases the Encoder

Disposing a model releases the encoder session, deferring the release while a classification runs, and is harmless when
repeated (`OnnxWordSenseModel_Dispose_CalledTwice_IsHarmless`; see the `OnnxWordSenseModel` unit document).

#### Tokenization Equals the Reference

The WordPiece tokenizer reproduces the reference Hugging Face tokenizer for the uncased vocabulary, and honours the
cased setting (see the `WordPieceTokenizer` unit document).

#### XML Documentation Examples Compile

Every `<code>` example of the shipped XML documentation file is extracted and compiled against the real public API
(`XmlDocExampleTests`).

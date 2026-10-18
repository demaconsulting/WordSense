## Onnx Subsystem Verification Design

This document describes the verification of the Onnx subsystem, the package `DemaConsulting.WordSense.Onnx`: the
shared implementation of the encoder models. It holds the abstract `OnnxWordSenseModel`, the classifier, the encoder,
the WordPiece tokenizer, the model downloader with its mirror, and the lemma weights.

### Verification Approach

The subsystem is verified at three levels. The pure units (`WordPieceTokenizer`, `LemmaWeights` with its blob and table,
`DownloadMirror`) are verified with hand-built inputs. The `ModelDownloader` is verified against a loopback
`HttpListener` (`LocalServer`) that serves small files, failures, corrupt files and redirects; pinned digests are
substituted for the test files, so no test reaches a public host. The model-level units (`OnnxWordSenseModel`,
`OnnxClassifier`, `OnnxEncoder`) are verified with fake weights and either a fake encoder input or the committed int8
model through the helper `ModelAssets`, which skips a test when the Git LFS content is absent. Tests live in
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

A text with single-sense words, unknown words and one heteronym runs the encoder only for the heteronym, and a text
without a heteronym never runs the encoder (see the `OnnxClassifier` unit document).

#### Confidence Grows with the Margin

The confidence of a context-resolved word is monotonic in the margin between the best and second-best sense (see the
`OnnxClassifier` unit document).

#### Weights Match the Lexicon Per Lemma

A lemma whose trained senses are not the first senses of its lexicon entry loses its weights and is answered with its
first sense at low confidence (see the `LemmaWeights` unit document). Dictionary senses after the trained ones are
allowed and are never ranked.

#### Model Files Come From a Verified Source

Model files are obtained from the mirror and then GitHub LFS, verified against their SHA-256 digest, stored atomically
under `<store>/<model-id>/`, and downloaded once even when requested concurrently (see the `ModelDownloader` and
`DownloadMirror` unit documents).

#### Tokenization Equals the Reference

The WordPiece tokenizer reproduces the reference Hugging Face tokenizer for the uncased vocabulary, and honours the
cased setting (see the `WordPieceTokenizer` unit document).

#### XML Documentation Examples Compile

Every `<code>` example of the shipped XML documentation file is extracted and compiled against the real public API
(`XmlDocExampleTests`).

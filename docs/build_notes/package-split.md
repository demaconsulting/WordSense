# Package Split

This note records the split of the single `DemaConsulting.WordSense` package into a core package and a MiniLM
package, the decisions behind it, and the evidence that the accuracy did not change.

## Packages

- `DemaConsulting.WordSense` (core): the public API (`WordSenseClassifier`, `WordAnalysis`, `WordDetails`,
  `Pronunciation`, `PartOfSpeech`, `AnswerBasis`), the lexicon table (`WSNS` v6), the weights table format (`WSNW` v1)
  and the `IWordSenseModel` abstraction. No dependencies.
- `DemaConsulting.WordSense.MiniLM`: `MiniLMWordSenseModel` (ONNX encoder), `WordPieceTokenizer`, the model catalog
  and downloader, `DownloadMirror`, the `MiniLMWordSense` factory and the embedded MiniLM weights table. Depends on
  `DemaConsulting.WordSense` and ONNX Runtime.

The core package has no ONNX or tokenizer dependency. `WordSenseClassifier.Create()` works from the lexicon alone: a
word with one part of speech is answered, a contextual multi-sense word abstains with `AnswerBasis.Dictionary` and
its candidates. `MiniLMWordSense.CreateClassifierAsync` returns the same classifier wired to the MiniLM model.

## Decisions

- **Weights are per model.** The weights table (`WSNW`) records the model ID, the SHA-256 of the model file, the
  SHA-256 of the lexicon it was trained against, the dimension, the mean vector and the per-lemma weights. The
  classifier checks them against the model (`IWordSenseModel.EnsureWeightsMatch`, which throws
  `WordSenseModelMismatchException`), so weights are never applied to a different model or lexicon.
- **Model files live in `model/<model-id>/`.** The same model ID names the mirror folder, the per-user store folder
  and the GitHub LFS path. See `model-provenance.md`.
- **One weights resource per model** is embedded in the model package as
  `weights.<model-id>.<onnx-sha256>.bin`, trained by `tools/WordSense.Builder` from the shared corpus.
- **No shared `WordSense.Onnx` package yet.** The ONNX session and tokenizer code could be shared by several ONNX
  models, but with one model it would be an abstraction without a second user. It is deferred until a second model
  exists.
- **Behavior is unchanged.** The split moves code and splits the table; it does not retrain or change the scoring.

## Accuracy Evidence

The builder `build` and `real-eval` reports before the split (`artifacts/baseline`) and after it (`artifacts/after`)
are byte-identical.

| Measure | Before | After |
| ------- | -----: | ----: |
| Holdout best-sense accuracy (12,789 rows) | 96.91 % | 96.91 % |
| Holdout best-meaning accuracy | 97.20 % | 97.20 % |
| Holdout accuracy of answered rows | 98.73 % | 98.73 % |
| Holdout coverage (answered) | 90.74 % | 90.74 % |

The real-text evaluation after the split (689 real sentences of 39 lemmas, `real-eval`):

| Set | Rows | Best sense | Meaning | Answered | Answered accuracy |
| --- | ---: | ---: | ---: | ---: | ---: |
| real-eval | 689 | 92.60 % | 94.63 % | 84.76 % | 96.40 % |
| llm-holdout-same-lemmas | 1263 | 97.70 % | 97.78 % | 95.17 % | 98.17 % |
| real-restored | 1256 | 93.15 % | 94.98 % | 84.16 % | 97.16 % |
| llm-holdout-restored-lemmas | 579 | 94.47 % | 95.16 % | 89.12 % | 97.67 % |
| real-gap | 48 | 93.75 % | 93.75 % | 89.58 % | 97.67 % |
| llm-holdout-gap-lemmas | 62 | 91.94 % | 91.94 % | 90.32 % | 98.21 % |

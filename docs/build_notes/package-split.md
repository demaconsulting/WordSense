# Package Split

This note records the split of WordSense into five NuGet packages and the rules behind it.

## Packages

Package dependencies:

- `DemaConsulting.WordSense` (core): `IWordSenseModel`, `MirrorOptions`, `WordSense`,
  `WordDetails`, `PartOfSpeech` and `WordLexicon`. Depends on nothing.
- `DemaConsulting.WordSense.Basic`: `BasicWordSenseModel` with a small embedded statistical table (part-of-speech
  tagger and context cues); no files.
  Depends on the core.
- `DemaConsulting.WordSense.Onnx`: the abstract `OnnxWordSenseModel`, the encoder, the tokenizer, the downloader and
  the weights reader. Depends on the core and ONNX Runtime.
- `DemaConsulting.WordSense.MiniLM`: `MiniLMWordSenseModel` with embedded weights. Depends on Onnx.
- `DemaConsulting.WordSense.BertBaseCased`: `BertBaseCasedWordSenseModel` with embedded weights. Depends on Onnx.

```text
WordSense.MiniLM ───────┐
                        ├──> WordSense.Onnx ──> WordSense (core)
WordSense.BertBaseCased ┘                            ^
WordSense.Basic ─────────────────────────────────────┘
```

The namespace of each package equals the package name.

## Rules

- **The core knows nothing of models.** It has no vectors, weights, ONNX or tokenizer, and it names no model package
  in code. A model is anything that implements `IWordSenseModel`.
- **Basic and Onnx depend on the core only.** The encoder packages depend on `Onnx`; nothing depends on an encoder
  package.
- **Weights belong to a model package.** Each encoder package embeds one `weights.bin` that was trained for its exact
  model files (dense `WSX1` format) and is checked against their SHA-256 digests.
- **Model files live in `model/<model-id>/`**, which also names the mirror folder, the per-user store folder and the
  GitHub LFS path. See `model-provenance.md`.
- **The word table is shared.** `WordLexicon` serves the analyses of all known spellings, including the
  inflections that the builder `compile-image` command materializes at build time, so no model needs morphology at run time.
- **Lifetime.** `IWordSenseModel` is `IDisposable`: the model owns the ONNX session and the downloader state, so it owns
  the lifetime. The Basic model's `Dispose` releases nothing but honors the same contract. Disposing during
  `EnsureModelAsync` cancels the download (`OperationCanceledException`).
- **Mirror configuration** is the sealed record `MirrorOptions` in the core, so every model package takes the same
  argument; models that need no files ignore it.

## Deviations from the Earlier Design

The classifier of the single package decided capitalization and possessive cases itself; these heuristics were dropped,
because the word table now contains the inflected forms and the models classify what the table knows. Only the dense
weights format is supported.

## Superseded History

An earlier split divided the single package into a core and a MiniLM package, with `WordSenseClassifier`,
`WordAnalysis`, `AnswerBasis` and the `MiniLMWordSense` factory. That design is replaced by the five packages above,
and its accuracy figures (a byte-identical before and after comparison of the split, about 97 percent best-sense
accuracy on the holdout) are history and not a statement about the current packages; the current figures are in
`model-comparison.md` and `basic-evaluation.md`.

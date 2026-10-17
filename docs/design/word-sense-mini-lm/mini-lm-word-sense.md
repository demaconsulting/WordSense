# MiniLMWordSense Unit Design

## Overview

`MiniLMWordSense` is the static entry point of the MiniLM package. It resolves the model files, builds the
`MiniLMWordSenseModel`, loads the weights that are embedded in the assembly, and returns a core
`WordSenseClassifier` that owns the model. `MiniLMWordSenseOptions` carries the settings. The unit contains
`MiniLMWordSense.cs` and `MiniLMWordSenseOptions.cs`.

## Data Model

- `MiniLMWordSense.ModelId`: the identifier of the model, `all-MiniLM-L6-v2-int8`, as pinned by the model catalog.
- `MiniLMWordSenseOptions`: `ModelDirectory` (a directory with the model files; they are verified, nothing is
  downloaded), `Downloader` (`WordSenseModelDownloaderOptions`; ignored when `ModelDirectory` is set),
  `MaxSequenceLength` (default 256) and `IntraOpThreads` (default 0, meaning the runtime decides).
- The embedded weights: the resource `weights.<model-id>.<onnx-sha256>.bin`, a `WordSenseWeightsTable` that was trained
  for this model and the embedded lexicon. The name carries the model id and the SHA-256 of the ONNX file, so the
  resource that belongs to a model file is found by its identity.

## Methods

- `CreateClassifierAsync(options, classifierOptions, ct)`:
  1. Validates `MaxSequenceLength` (8 to 512), `IntraOpThreads` (not negative) and the classifier options (an
     `AbstainMarginScale` that is NaN or negative), before any file is touched.
  2. Resolves the model location: with a `ModelDirectory` through `WordSenseModelLocation.FromDirectory` (no network,
     no mirror environment is read), otherwise through `WordSenseModelDownloader.EnsureModelAsync`.
  3. Loads the embedded lexicon and the weights, and creates the `MiniLMWordSenseModel`.
  4. Returns `new WordSenseClassifier(table, model, weights, classifierOptions, ownsModel: true)`; the model is
     disposed if any step after its creation fails.
- `LoadWeights()`: reads the embedded weights table for the pinned model.

## Error Handling

- `ArgumentOutOfRangeException` for an invalid option, before any download or verification.
- `FileNotFoundException` and `InvalidDataException` from a supplied `ModelDirectory` whose files are missing or do
  not match the pinned digests; `WordSenseModelDownloadException` when every download source failed.
- `WordSenseTableFormatException` when the embedded weights are missing or malformed.
- `WordSenseModelMismatchException` when the weights do not belong to the model or to the embedded lexicon.
- `OperationCanceledException` when the token is cancelled during the download; other `IOException` and
  `UnauthorizedAccessException` failures of the store propagate.

## Interactions

Uses `WordSenseModelDownloader`, `WordSenseModelLocation` and `WordSenseModelCatalog` (model files),
`MiniLMWordSenseModel` (the model), and the core `WordSenseTable`, `WordSenseWeightsTable` and `WordSenseClassifier`.
Called by applications and by `WordSenseBuilder`, which loads the same weights to evaluate real text.

# System Design

This document provides the system-level design for the MiniLM package, `DemaConsulting.WordSense.MiniLM`.

![WordSense MiniLM Structure](WordSenseMiniLMView.svg)

## Architecture

The MiniLM package supplies the first model for the core WordSense classifier: the int8 `all-MiniLM-L6-v2` sentence
transformer, run locally with ONNX Runtime, together with the weights that were trained for it. It depends on the core
package `DemaConsulting.WordSense` and on Microsoft.ML.OnnxRuntime; the core package does not know it. The system
consists of these units:

- **MiniLMWordSense**: static factory that resolves the model files, loads the embedded weights, and returns a
  `WordSenseClassifier` that owns the model
- **MiniLMWordSenseModel**: implementation of `IWordSenseModel` over an ONNX Runtime session; it owns the tokenizer,
  windows long text and pools the word pieces of each target
- **WordPieceTokenizer**: BERT uncased word-piece tokenizer with character offsets
- **DownloadMirror**: validated user mirror configuration
- **WordSenseModelDownloader**: ordered, verified model download (mirror, Hugging Face, GitHub LFS) with the model
  catalog that pins the digests of the files

There are no subsystems. Each model has an identifier (the model id, here `all-MiniLM-L6-v2-int8`) that names its
folder everywhere: `model/<model-id>/` in the repository, `<mirror>/<model-id>/<file>` on a mirror and
`<store>/<model-id>/` in the per-user store. The weights are an embedded resource named
`weights.<model-id>.<onnx-sha256>.bin`, keyed by the model id and the SHA-256 of the ONNX file, so a weights table
can only be matched to the model it was trained for.

A shared package for ONNX-based models (a `WordSense.Onnx` package) is intentionally not created yet. With one model
the tokenizer, the ONNX session handling and the model management stay in this package; the shared package is
deferred until a second model exists and shows what the models really share.

## External Interfaces

| Interface                                  | Direction | Format                | Constraints                     |
|--------------------------------------------|-----------|-----------------------|---------------------------------|
| `MiniLMWordSense.CreateClassifierAsync`    | In        | Async factory         | Model available                 |
| `MiniLMWordSense.LoadWeights`              | Out       | Weights table         | Embedded resource, verified     |
| `MiniLMWordSenseModel` constructor         | In        | Model, vocabulary     | Files exist                     |
| `EnsureModelAsync(options)`                | In/Out    | Async call            | SHA-256 verified                |
| `WordSenseModelLocation.FromDirectory`     | In/Out    | Directory to location | Missing or altered file throws  |
| `DownloadMirror`, `Options.Mirror`         | In        | Record / options      | Absolute http(s) URL            |
| `WORDSENSE_MIRROR_*`                       | In        | Environment           | Used if no option mirror        |
| `MiniLMWordSenseOptions.MaxSequenceLength` | In        | Option                | 8 to 512                        |
| `MiniLMWordSenseOptions.IntraOpThreads`    | In        | Option                | Not negative                    |
| `MiniLMWordSenseOptions.ModelDirectory`    | In        | Option                | Files verified, no download     |
| `MiniLMWordSenseOptions.Downloader`        | In        | Option                | Ignored with a `ModelDirectory` |

### Download sources and order

`WordSenseModelDownloader` obtains each missing or invalid model file from the first source that
delivers bytes with the pinned SHA-256 digest:

1. **User mirror** when configured: `{mirrorBase}/{model-id}/{file}`. The programmatic
   `WordSenseModelDownloaderOptions.Mirror` wins over the environment variables.
2. **Hugging Face** `https://huggingface.co/sentence-transformers/all-MiniLM-L6-v2/resolve/main/{file}`,
   only for files whose pinned digest is the upstream file. The int8 ONNX model is quantized locally and
   is **not** the upstream `model_qint8_*.onnx` (the upstream file has a different size and digest), so
   only `vocab.txt` is requested from Hugging Face.
3. **GitHub LFS** `https://media.githubusercontent.com/media/demaconsulting/WordSense/{ref}/model/{model-id}/{file}`
   for every file.

A failed request, timeout, block or digest mismatch moves on to the next source; credentials are sent
only to the mirror. If every source fails, `WordSenseModelDownloadException` lists each attempt.

## Error Handling

- `WordSenseModelDownloadException`: `EnsureModelAsync` (and `CreateClassifierAsync` without a `ModelDirectory`)
  throws it when every download source failed for a missing or invalid model file; the message lists each attempt.
- `WordSenseModelMismatchException`: creating the classifier throws it when the embedded weights do not belong to the
  model or to the core lexicon.
- `WordSenseTableFormatException`: loading the embedded weights throws it when the resource is missing or malformed.
- `FileNotFoundException` and `InvalidDataException`: a supplied `ModelDirectory` throws the former when a model
  file is missing and the latter when a file does not match its pinned SHA-256; no download is attempted.
- `ArgumentOutOfRangeException`: an invalid `MaxSequenceLength`, `IntraOpThreads` or classifier option is rejected
  before any model file is downloaded or verified.
- Invalid arguments (for example an invalid mirror URL) throw `ArgumentException` or its derived types.

## Dependencies

The runtime dependencies are the core package `DemaConsulting.WordSense` and Microsoft.ML.OnnxRuntime (see
_OTS Integration Design_ and the _ONNX Runtime_ design). The model files are not part of the package; they are
downloaded on first use, or supplied through `MiniLMWordSenseOptions.ModelDirectory`. The weights are embedded.
The OTS items used for building and verifying the system are those listed in the core system design.

## Risk Control Measures

N/A - WordSense is a text-analysis library with no safety-critical functionality requiring risk
control measures (IEC 62304 §5.3.3). Model integrity is protected by pinned SHA-256 digests, and the embedded
weights are bound to the model and the lexicon by digests.

## Data Flow

**Creation path:**

1. `CreateClassifierAsync` validates the options, then resolves the model directory: `ModelDirectory` if set,
   otherwise `WordSenseModelDownloader.EnsureModelAsync` (verifies existing files, downloads the rest).
2. The model is created from the ONNX file and the vocabulary (tokenizer, ONNX session, SHA-256 of the ONNX file), the
   embedded lexicon and weights are loaded, and a `WordSenseClassifier` that owns the model is returned. The
   classifier constructor checks that the weights match the model and the lexicon.

**Embedding path (inside `EncodeAsync`):**

1. The words are tokenized into word pieces with the range of pieces of each word.
2. When the whole text fits the window, the model runs once; otherwise it runs once per target on a window centered
   on the target.
3. The hidden states of the word pieces of each target are averaged; the vector is returned without centering, and the
   classifier centers and normalizes it with the weights table mean.

## Design Constraints

- **Determinism**: same text and model give the same vectors.
- **Integrity**: no model file is used unless its SHA-256 matches the pinned digest.
- **Concurrency**: the model is safe for concurrent calls; the session, tokenizer and catalog are shared and read only.
- **Portability**: the assembly targets net8.0, net9.0 and net10.0; native code comes only from ONNX Runtime.
- **Accuracy**: the classification results equal those before the split into a core and a model package.
- **Quality**: zero warnings, requirements traceable to tests.

### Integration Patterns

- **NuGet Packaging**: the package `DemaConsulting.WordSense.MiniLM`, which references `DemaConsulting.WordSense`.
- **Git LFS**: every model file in `model/<model-id>/` (except its README and SHA256SUMS) is stored
  with Git LFS and served through the GitHub media endpoint.
- **CI/CD Integration**: automated build, test and quality validation.

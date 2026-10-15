# System Design

This document provides the system-level design for WordSense.

![WordSense Structure](WordSenseView.svg)

## Architecture

WordSense is a .NET library that decides which sense an ambiguous English word carries in a given
text. It ships as one NuGet package, `DemaConsulting.WordSense`, whose only runtime dependency is
Microsoft.ML.OnnxRuntime. The system consists of these units, all in one assembly:

- **WordPieceTokenizer**: BERT uncased word-piece tokenizer with character offsets
- **WordSenseTable**: embedded binary table of per-lemma linear classifiers, word forms and abstention margins
- **OnnxWordSenseEncoder**: ONNX Runtime wrapper around the int8 `all-MiniLM-L6-v2` model
- **SenseRecord**: immutable tagged sense records (WordSense), tag scoring and text segmentation
- **WordSenseClassifier**: public entry point that combines the units above
- **DownloadMirror**: validated user mirror configuration
- **WordSenseModelDownloader**: ordered, verified model download (mirror, Hugging Face, GitHub LFS)
- **WordSenseBuilder**: non-shipped tool (`tools/WordSense.Builder`) that trains the table from the corpus

There are no subsystems. The classifier collaborates with the tokenizer, encoder and table; the
downloader uses the mirror configuration and the model catalog. The builder reuses the tokenizer,
encoder, table and feature extraction so that training and inference share one code path.

## External Interfaces

| Interface                          | Direction | Format                               | Constraints                |
|------------------------------------|-----------|--------------------------------------|----------------------------|
| `CreateAsync(options)`             | In        | Async factory                        | Model available            |
| `ClassifyAsync(words)`             | In/Out    | Words to `WordSense?[]`              | Parallel to input          |
| `ClassifyDetailAsync(words)`       | In/Out    | Words to `WordSenseDetail?[]`        | Margins, runner-up         |
| `ClassifyTagAsync`, `IsTagAsync`   | In/Out    | Words, tags to `string?[]`/`bool?[]` | `null` if absent or unsure |
| `ClassifyAsync(text)`              | In/Out    | `string` to list of tokens           | Tokens tile the input      |
| `EnsureModelAsync(options)`        | In/Out    | Async call                           | SHA-256 verified           |
| `DownloadMirror`, `Options.Mirror` | In        | Record / options                     | Absolute http(s) URL       |
| `WORDSENSE_MIRROR_*`               | In        | Environment                          | Used if no option mirror   |
| `IWordSenseEncoder`                | In        | Interface                            | Test substitutable         |

### Download sources and order

`WordSenseModelDownloader` obtains each missing or invalid model file from the first source that
delivers bytes with the pinned SHA-256 digest:

1. **User mirror** when configured: `{mirrorBase}/all-MiniLM-L6-v2/{file}`. The programmatic
   `WordSenseModelDownloaderOptions.Mirror` wins over the environment variables.
2. **Hugging Face** `https://huggingface.co/sentence-transformers/all-MiniLM-L6-v2/resolve/main/{file}`,
   only for files whose pinned digest is the upstream file. The int8 ONNX model is quantized locally and
   is **not** the upstream `model_qint8_*.onnx` (the upstream file has a different size and digest), so
   only `vocab.txt` is requested from Hugging Face.
3. **GitHub LFS** `https://media.githubusercontent.com/media/demaconsulting/WordSense/{ref}/model/{file}`
   for every file.

A failed request, timeout, block or digest mismatch moves on to the next source; credentials are sent
only to the mirror. If every source fails, `WordSenseModelDownloadException` lists each attempt.

## Dependencies

The runtime dependency is Microsoft.ML.OnnxRuntime 1.28.0 (see _OTS Integration Design_ and the
_ONNX Runtime_ design). The model file `model_int8.onnx` is not part of the package; it is
downloaded on first use, or supplied through `WordSenseClassifierOptions.ModelDirectory`. The
following OTS items are used for building and verifying this system (not consumed at runtime):

- **BuildMark**, **FileAssert**, **Pandoc**, **ReqStream**, **ReviewMark**, **SarifMark**,
  **SonarMark**, **SysML2Tools**, **VersionMark**, **WeasyPrint** and **xUnit**; see each item's
  design document.

## Risk Control Measures

N/A - WordSense is a text-analysis library with no safety-critical functionality requiring risk
control measures (IEC 62304 §5.3.3). Model integrity is protected by pinned SHA-256 digests and by
the table's trailing digest.

## Data Flow

**Creation path:**

1. `CreateAsync` resolves the model directory: `ModelDirectory` if set, otherwise
   `WordSenseModelDownloader.EnsureModelAsync` (verifies existing files, downloads the rest).
2. The vocabulary is loaded into the tokenizer, the ONNX session is created, and the embedded table
   is loaded; the table's model digest must equal the encoder's digest.

**Classification path:**

1. The text is split into tiles (word, whitespace run or single character).
2. Words that are known forms of a lemma become targets; if there are none, no inference is done.
3. The tokenizer produces word pieces with offsets; the encoder runs once for the sentence (a window
   around each target for long texts).
4. For each target the hidden states of its word pieces are pooled, centered with the table mean and
   normalized; the lemma's linear model yields the best sense and margin; a margin below the lemma's
   abstention threshold yields no sense.

## Design Constraints

- **Determinism**: same text and model give the same result; the table is built reproducibly.
- **Integrity**: no model file is used unless its SHA-256 matches the pinned digest.
- **Portability**: the assembly targets net8.0, net9.0 and net10.0; native code comes only from
  ONNX Runtime.
- **Quality**: zero warnings, requirements traceable to tests.

### Platform Support

| Target Framework | Runtime / Environment |
|------------------|-----------------------|
| `net8.0`         | .NET 8 LTS            |
| `net9.0`         | .NET 9                |
| `net10.0`        | .NET 10               |

The library is supported on Windows, Linux and macOS.

### Integration Patterns

- **NuGet Packaging**: one package, `DemaConsulting.WordSense`.
- **Git LFS**: every model file in `model/` (except its README and SHA256SUMS) is stored
  with Git LFS and served through the GitHub
  media endpoint.
- **CI/CD Integration**: automated build, test and quality validation.
- **Review Management**: file review with ReviewMark review-sets.

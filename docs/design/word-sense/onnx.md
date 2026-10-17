## Onnx Subsystem Design

![Onnx Structure](OnnxView.svg)

### Overview

The Onnx subsystem is the package `DemaConsulting.WordSense.Onnx`. It holds everything the encoder models share:
verified model files, the ONNX encoder and its tokenizer, the trained per-lemma weights and the classifier that
scores the senses of an ambiguous word. It is the only package with a third-party runtime dependency, ONNX Runtime.
Its single public type is the abstract `OnnxWordSenseModel`; the model packages derive from it and supply the model
identity.

The subsystem contains seven units:

- **OnnxWordSenseModel**: file management, lazy loading and the creation of classifiers.
- **OnnxClassifier**: lexicon lookup and sense scoring.
- **OnnxEncoder**: tokenization, inference and mean pooling.
- **WordPieceTokenizer**: the BERT WordPiece tokenizer, uncased or cased.
- **ModelDownloader**: ordered, verified download of model files.
- **DownloadMirror**: validated mirror configuration.
- **LemmaWeights**: the weights blob reader, the matching to the lexicon, the feature computation and the scoring.

### Interfaces

It exposes the abstract `OnnxWordSenseModel` (implements `IWordSenseModel` and `IDisposable`) with the properties
`HiddenSize` and `ModelId`. Its constructor is private protected, so only the WordSense model packages can derive.
It consumes the core contracts and `WordLexicon.Words`, the ONNX Runtime inference API, HTTP(S) for downloads, the
file system for the model store, and the embedded weights of the derived model's assembly. All other members are
internal and visible to the MiniLM and BertBaseCased packages, the builder and the test projects.

The model description passed by a derived class is an internal `ModelSpec`: the model id, the files with their
SHA-256 digests, the ONNX file name, the vocabulary file name, whether the tokenizer lower-cases, the assembly and
resource name of the weights, and the hidden size.

### Design

The units collaborate as follows:

1. `OnnxWordSenseModel.EnsureModelAsync` verifies an explicit model directory with `ModelDownloader.Verify`, or calls
   `ModelDownloader.EnsureAsync`, which builds the mirror with `DownloadMirror` (from `MirrorOptions` or the
   environment), reuses valid files from the store and downloads the others.
2. `OnnxWordSenseModel.Create` loads the embedded weights once through `LemmaWeights` (blob reader and
   all-or-nothing match to the lexicon), opens one `OnnxEncoder` once (which builds a `WordPieceTokenizer` and the ONNX
   session) and returns an `OnnxClassifier` over the shared encoder and weights.
3. `OnnxClassifier.ClassifyAsync` resolves every word from the lexicon, asks `OnnxEncoder` for the pooled vector of
   the words that need context, turns each vector into a feature with the global mean of the weights, and scores the
   word's candidate senses with `LemmaWeights`.

Training and inference share the feature computation, so the builder and the classifier cannot diverge. The encoder
and the weights are shared by all classifiers of one model and are released when the model is disposed.

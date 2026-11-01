# Model: bert-base-cased

The encoder files that the embedded weights of `src/DemaConsulting.WordSense.BertBaseCased` were trained against. The
files are not in the repository: they are the assets of the GitHub release `models/bert-base-cased/v1`.

| Asset | Purpose |
| ----- | ------- |
| `model-int8-dynamic.onnx` | ONNX encoder, int8 dynamically quantized (output `last_hidden_state`, 768 dimensions) |
| `config.json` | Model configuration published with the upstream export (hidden size 768) |
| `vocab.txt` | BERT cased WordPiece vocabulary (28,996 entries) |
| `tokenizer.json` | Reference tokenizer definition (used only to generate the tokenizer parity fixture) |
| `LICENSE` | The Apache License 2.0 that covers the model files |
| `<asset>.sha256` | The SHA-256 of each asset above, for humans and scripts |

This folder holds the provenance: this file, `LICENSE` and `SHA256SUMS`, the SHA-256 of every asset. The library trusts
the digests pinned in the package, never the release.

## Versions

| Version | Quantization | Export |
| ------- | ------------ | ------ |
| `v1` | `int8-dynamic` | `Xenova/bert-base-cased` int8 export, cut at the output of the last encoder layer (`tools/model-prep/make-bert-encoder.py`) |

A version is a model export: the upstream checkpoint, the graph cut, the opset and the output taps. A change of the
full-precision graph is a new version. A release is append only; see `tools/model-release/README.md`.

## Where the files go

- `EnsureModelAsync` downloads them into `<store>/bert-base-cased/v1/`, from the mirror (when configured) and then from
  the release assets.
- `dotnet run --project tools/WordSense.Builder -- fetch-models` fetches them into `model/bert-base-cased/v1/`
  (git-ignored); the tests and the Builder read that folder.

## Provenance

- Base model: [`bert-base-cased`](https://huggingface.co/bert-base-cased) (BERT, 12 layers, hidden size 768,
  cased vocabulary), licensed under the Apache License 2.0.
- The upstream int8 export (`Xenova/bert-base-cased`, `onnx/model_int8.onnx`, 109,200,172 bytes, SHA-256
  `b4505ab8506e2e7dd53c43f26570191e5b09180353b9c5ce1b764bad736bc841`) is a masked-language-model export that exposes
  only `logits`. It is ONNX Runtime dynamically quantized to int8.
- `model-int8-dynamic.onnx` is derived from it by `tools/model-prep/make-bert-encoder.py`, which cuts the graph at the
  output of the last encoder layer and exposes it as `last_hidden_state`. No weights are changed; the language-model
  head is dropped.
- The content differs from the upstream file: a mirror that serves the upstream file under the asset name fails
  SHA-256 verification and the download falls back to the release assets.
- `vocab.txt`, `config.json` and `tokenizer.json` are the files published with the upstream export.

## License

The model files are licensed under the Apache License 2.0; the full text is in [LICENSE](LICENSE) and is also a release
asset. The copyright is held by Google and the other authors of the base model `bert-base-cased` (Google LLC, see its
model page above) and of the int8 export `Xenova/bert-base-cased` on which the encoder is based; this repository claims
no copyright in them.

Changes: `model-int8-dynamic.onnx` is derived from the upstream int8 export by cutting the graph at the output of the
last encoder layer, exposed as `last_hidden_state`, and dropping the language-model head. No weight is changed.
`vocab.txt`, `config.json` and `tokenizer.json` are the files published with the upstream export. The authors of the
base model do not endorse these files.

# Model: all-MiniLM-L6-v2

The encoder files that the embedded weights of `src/DemaConsulting.WordSense.MiniLM` were trained against. The files are
not in the repository: they are the assets of the GitHub release `models/all-MiniLM-L6-v2/v1`.

| Asset | Purpose |
| ----- | ------- |
| `model-int8-dynamic.onnx` | ONNX encoder, int8 dynamically quantized (output `last_hidden_state`, 384 dimensions) |
| `vocab.txt` | BERT uncased WordPiece vocabulary (30,522 entries) |
| `tokenizer.json` | Reference tokenizer definition (only for the tokenizer parity fixture; not downloaded) |
| `LICENSE` | The Apache License 2.0 that covers the model files |
| `<asset>.sha256` | The SHA-256 of each asset above, for humans and scripts |

This folder holds the provenance: this file, `LICENSE` and `SHA256SUMS`, the SHA-256 of every asset. The library trusts
the digests pinned in the package, never the release.

## Versions

| Version | Quantization | Export |
| ------- | ------------ | ------ |
| `v1` | `int8-dynamic` | `sentence-transformers/all-MiniLM-L6-v2`, `last_hidden_state`, dynamic int8 quantization |

A version is a model export: the upstream checkpoint, the graph cut, the operator set and the output taps. A change of the
full-precision graph is a new version. A release is append only; see `tools/model-release/README.md`.

## Where the files go

- `EnsureModelAsync` downloads them into `<store>/all-MiniLM-L6-v2/v1/`, from the mirror (when configured) and then from
  the release assets.
- `dotnet run --project tools/WordSense.Builder -- fetch-models` fetches them into `model/all-MiniLM-L6-v2/v1/`
  (git-ignored); the tests and the Builder read that folder.

## Provenance

- Base model: [`sentence-transformers/all-MiniLM-L6-v2`](https://huggingface.co/sentence-transformers/all-MiniLM-L6-v2)
  (BERT, 6 layers, hidden size 384, uncased vocabulary), licensed under the Apache License 2.0.
- `model-int8-dynamic.onnx` was produced locally during the feasibility spike by exporting the base model to ONNX
  (`last_hidden_state` output) and applying ONNX Runtime dynamic int8 quantization. The exact quantization command
  was not recorded.
- It is **not** byte-identical to any upstream `onnx/model_qint8_*.onnx` file published in the Hugging Face repository
  (the upstream `model_qint8_avx512.onnx` is about 24.1 MB while this file is 22,972,370 bytes), so the Hugging Face
  source is never used for it.
- `vocab.txt` and `tokenizer.json` are the files shipped with the base model.

## License

The model files are licensed under the Apache License 2.0; the full text is in [LICENSE](LICENSE) and is also a release
asset. The copyright is held by the authors of the base model `sentence-transformers/all-MiniLM-L6-v2` (the
sentence-transformers project and the authors of the models it derives from, see its model page above); this repository
claims no copyright in them.

Changes: `model-int8-dynamic.onnx` is not the unchanged upstream file. The base model was exported to ONNX (output
`last_hidden_state`) and quantized to int8 with ONNX Runtime dynamic quantization, so the weights are changed
(quantized). `vocab.txt` and `tokenizer.json` are the files shipped with the base model. The authors of the base model
do not endorse these files.

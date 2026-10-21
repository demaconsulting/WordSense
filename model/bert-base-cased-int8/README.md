# WordSense Model Files: bert-base-cased-int8

These are the exact encoder files that the embedded weights table of
`src/DemaConsulting.WordSense.BertBaseCased` was trained against.

| File | Purpose | Storage |
| ---- | ------- | ------- |
| `model_int8.onnx` | ONNX encoder, int8 quantized (output `last_hidden_state`, 768 dimensions) | Git LFS |
| `config.json` | Model configuration published with the upstream export (hidden size 768) | Git LFS |
| `vocab.txt` | BERT cased WordPiece vocabulary (28,996 entries) | Git LFS |
| `tokenizer.json` | Reference tokenizer definition (used only to generate the tokenizer parity fixture) | Git LFS |
| `SHA256SUMS` | Pinned SHA-256 digests of the files above (of the real content, not the LFS pointers) | Git |
| `LICENSE` | The Apache License 2.0 that covers the model files above | Git |
| `README.md` | This file | Git |

## Provenance

- Base model: [`bert-base-cased`](https://huggingface.co/bert-base-cased) (BERT, 12 layers, hidden size 768,
  cased vocabulary), licensed under the Apache License 2.0.
- The upstream int8 export (`Xenova/bert-base-cased`, `onnx/model_int8.onnx`, 109,200,172 bytes, SHA-256
  `b4505ab8506e2e7dd53c43f26570191e5b09180353b9c5ce1b764bad736bc841`, which a local mirror may also serve) is a
  masked-language-model export that exposes only `logits`.
- `model_int8.onnx` in this folder (previously named `encoder_int8.onnx`) is derived from it by
  `tools/model-prep/make-bert-encoder.py`, which cuts the graph at the
  output of the last encoder layer and exposes it as `last_hidden_state`. No weights are changed; the language-model
  head is dropped.
- The file name matches the upstream and LAN-mirror layout (`onnx/model_int8.onnx`), but the content differs from the
  upstream file: this one is the derived encoder. A mirror that serves the upstream file under this name fails
  SHA-256 verification and the download falls back to the repository.
- `vocab.txt`, `config.json` and `tokenizer.json` are the files published with the upstream export.

All downloadable assets (every file except this README, `LICENSE` and `SHA256SUMS`) are Git LFS objects, served from
`https://media.githubusercontent.com/media/demaconsulting/WordSense/{ref}/model/bert-base-cased-int8/{file}`.
The model ID `bert-base-cased-int8` names this folder everywhere: `<mirror>/<model-id>/` on a mirror,
`<store>/<model-id>/` in the per-user store and `<ref>/model/<model-id>/` on the LFS endpoint. A missing file is requested
from the mirror first (the argument of `EnsureModelAsync` or the `WORDSENSE_MIRROR_URL` environment variable), then from
the LFS endpoint; Hugging Face is not a download source. The weights embedded in the package are valid only for the exact
`model_int8.onnx` listed in `SHA256SUMS`, and all four files are pinned and downloaded.

## License

The model files `model_int8.onnx`, `config.json`, `vocab.txt` and `tokenizer.json` are licensed under the Apache License
2.0; the full text is in [LICENSE](LICENSE). The copyright is held by Google and the other authors of the base model
`bert-base-cased` (Google LLC, see its model page above) and of the int8 export `Xenova/bert-base-cased` on which the
encoder is based; this repository claims no copyright in them.

Changes: `model_int8.onnx` is derived from the upstream int8 export by cutting the graph at the output of the last
encoder layer, exposed as `last_hidden_state`, and dropping the language-model head. No weight is changed.
`vocab.txt`, `config.json` and `tokenizer.json` are the files published with the upstream export. The authors of the
base model do not endorse these files.

## Integrity

Every runtime download is verified against the SHA-256 digests in `SHA256SUMS` (also pinned in source). A file that
fails verification is discarded and the next download source is tried.

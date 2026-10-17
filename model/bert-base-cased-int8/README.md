# WordSense Model Files: bert-base-cased-int8

These are the exact encoder files that the embedded weights table of
`src/DemaConsulting.WordSense.BertBaseCased` was trained against.

| File | Purpose | Storage |
| ---- | ------- | ------- |
| `encoder_int8.onnx` | ONNX encoder, int8 quantized (output `last_hidden_state`, 768 dimensions) | Git LFS |
| `vocab.txt` | BERT cased WordPiece vocabulary (28,996 entries) | Git LFS |
| `tokenizer.json` | Reference tokenizer definition (used only to generate the tokenizer parity fixture) | Git LFS |
| `SHA256SUMS` | Pinned SHA-256 digests of the files above (of the real content, not the LFS pointers) | Git |
| `README.md` | This file | Git |

## Provenance

- Base model: [`bert-base-cased`](https://huggingface.co/bert-base-cased) (BERT, 12 layers, hidden size 768,
  cased vocabulary), licensed under the Apache License 2.0.
- The upstream int8 export (`Xenova/bert-base-cased`, `onnx/model_int8.onnx`, 109,200,172 bytes, SHA-256
  `b4505ab8506e2e7dd53c43f26570191e5b09180353b9c5ce1b764bad736bc841`, also on the LAN mirror at
  `http://192.168.1.13:8000/bert-base-cased/`) is a masked-language-model export that exposes only `logits`.
- `encoder_int8.onnx` is derived from it by `tools/model-prep/make-bert-encoder.py`, which cuts the graph at the
  output of the last encoder layer and exposes it as `last_hidden_state`. No weights are changed; the language-model
  head is dropped.
- `vocab.txt` and `tokenizer.json` are the files published with the upstream export.

All downloadable assets (every file except this README and `SHA256SUMS`) are Git LFS objects, served from
`https://media.githubusercontent.com/media/demaconsulting/WordSense/{ref}/model/bert-base-cased-int8/{file}`.

## Integrity

Every runtime download is verified against the SHA-256 digests in `SHA256SUMS` (also pinned in source). A file that
fails verification is discarded and the next download source is tried.

# WordSense Model Files

These are the exact encoder files that the embedded word-sense table
(`src/DemaConsulting.WordSense/Resources/wordsense-table.bin`) was trained against.

| File | Purpose | Storage |
| ---- | ------- | ------- |
| `model_int8.onnx` | ONNX encoder, int8 dynamically quantized (output `last_hidden_state`, 384 dimensions) | Git LFS |
| `vocab.txt` | BERT uncased WordPiece vocabulary (30,522 entries) | Git |
| `tokenizer.json` | Reference tokenizer definition (used only to generate the tokenizer parity fixture) | Git |
| `SHA256SUMS` | Pinned SHA-256 digests of the three files above | Git |

## Provenance

- Base model: [`sentence-transformers/all-MiniLM-L6-v2`](https://huggingface.co/sentence-transformers/all-MiniLM-L6-v2)
  (BERT, 6 layers, hidden size 384, uncased vocabulary), licensed under the Apache License 2.0.
- `model_int8.onnx` was produced locally during the feasibility spike by exporting the base model to ONNX
  (`last_hidden_state` output) and applying ONNX Runtime dynamic int8 quantization. The exact quantization command
  was not recorded.
- It is **not** byte-identical to any upstream `onnx/model_qint8_*.onnx` file published in the Hugging Face repository
  (the upstream `model_qint8_avx512.onnx` is about 24.1 MB while this file is 22,972,370 bytes), so the Hugging Face
  source is never used for it. See `docs/build_notes/model-provenance.md`.
- `vocab.txt` is the standard BERT uncased vocabulary shipped with the base model.

## Integrity

Every runtime download is verified against the SHA-256 digests in `SHA256SUMS` (also pinned in source). A file that
fails verification is discarded and the next download source is tried.

# WordSense Model Files

These are the exact encoder files that the embedded weights table
(`src/DemaConsulting.WordSense.MiniLM/Resources/weights.bin`, embedded as the resource
`DemaConsulting.WordSense.MiniLM.weights.bin`)
was trained against.

| File | Purpose | Storage |
| ---- | ------- | ------- |
| `model_int8.onnx` | ONNX encoder, int8 dynamically quantized (output `last_hidden_state`, 384 dimensions) | Git LFS |
| `vocab.txt` | BERT uncased WordPiece vocabulary (30,522 entries) | Git LFS |
| `tokenizer.json` | Reference tokenizer definition (used only to generate the tokenizer parity fixture) | Git LFS |
| `SHA256SUMS` | Pinned SHA-256 digests of the three files above (of the real content, not the LFS pointers) | Git |
| `LICENSE` | The Apache License 2.0 that covers the model files above | Git |
| `README.md` | This file | Git |

## Provenance

- Base model: [`sentence-transformers/all-MiniLM-L6-v2`](https://huggingface.co/sentence-transformers/all-MiniLM-L6-v2)
  (BERT, 6 layers, hidden size 384, uncased vocabulary), licensed under the Apache License 2.0.
- `model_int8.onnx` was produced locally during the feasibility spike by exporting the base model to ONNX
  (`last_hidden_state` output) and applying ONNX Runtime dynamic int8 quantization. The exact quantization command
  was not recorded.
- It is **not** byte-identical to any upstream `onnx/model_qint8_*.onnx` file published in the Hugging Face repository
  (the upstream `model_qint8_avx512.onnx` is about 24.1 MB while this file is 22,972,370 bytes), so the Hugging Face
  source is never used for it.
- `vocab.txt` is the standard BERT uncased vocabulary shipped with the base model.

All downloadable assets (every file except this README, `LICENSE` and `SHA256SUMS`) are Git LFS objects, served from
`https://media.githubusercontent.com/media/demaconsulting/WordSense/{ref}/model/all-MiniLM-L6-v2-int8/{file}`.
The model ID `all-MiniLM-L6-v2-int8` names this folder everywhere: `<mirror>/<model-id>/` on a mirror,
`<store>/<model-id>/` in the per-user store and `<ref>/model/<model-id>/` on the LFS endpoint. A missing file is requested
from the mirror first (the `DownloadOptions` passed to `EnsureModelAsync` or the `WORDSENSE_MIRROR_URL` environment
variable), then from
the LFS endpoint; Hugging Face is not a download source. The weights embedded in the package are valid only for the exact
`model_int8.onnx` listed in `SHA256SUMS`.

## License

The model files `model_int8.onnx`, `vocab.txt` and `tokenizer.json` are licensed under the Apache License 2.0; the full
text is in [LICENSE](LICENSE). The copyright is held by the authors of the base model
`sentence-transformers/all-MiniLM-L6-v2` (the sentence-transformers project and the authors of the models it derives
from, see its model page above); this repository claims no copyright in them.

Changes: `model_int8.onnx` is not the unchanged upstream file. The base model was exported to ONNX (output
`last_hidden_state`) and quantized to int8 with ONNX Runtime dynamic quantization, so the weights are changed
(quantized). `vocab.txt` and `tokenizer.json` are the files shipped with the base model. The authors of the base model
do not endorse these files.

## Integrity

Every runtime download is verified against the SHA-256 digests in `SHA256SUMS` (also pinned in source). A file that
fails verification is discarded and the next download source is tried.

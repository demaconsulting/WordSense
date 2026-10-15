# Model Provenance

This note records where the WordSense encoder model and tokenizer vocabulary come from, how they are verified, and
the licence terms that apply to them.

## Files

| File | Size (bytes) | SHA-256 |
| ---- | ------------ | ------- |
| `model/model_int8.onnx` | 22,972,370 | `afdb6f1a0e45b715d0bb9b11772f032c399babd23bfc31fed1c170afc848bdb1` |
| `model/vocab.txt` | 231,508 | `07eced375cec144d27c900241f3e339478dec958f92fddbc551f295c992038a3` |
| `model/tokenizer.json` | 711,661 | `da0e79933b9ed51798a3ae27893d3c5fa4a201126cef75586296df9b4d2c62a0` |

## Origin

- Base model: `sentence-transformers/all-MiniLM-L6-v2` (BERT, six layers, 384 hidden units, uncased WordPiece
  vocabulary of 30,522 entries).
- `model_int8.onnx` was produced locally during the feasibility spike: the base model was exported to ONNX with a
  `last_hidden_state` output and quantized with ONNX Runtime dynamic int8 quantization. The exact command was not
  recorded.
- The Hugging Face repository publishes its own quantized files under `onnx/` (for example
  `model_qint8_avx512.onnx`, about 24.1 MB). Those differ in size from the committed model, so they cannot match its
  SHA-256 and are never used as a download source for `model_int8.onnx`.
- `huggingface.co` could not be reached from the build environment (TLS interception), so the upstream
  `vocab.txt` digest could not be compared directly. The committed `vocab.txt` digest equals the widely published
  digest of the BERT uncased vocabulary. The download pipeline pins the digest, so a mismatching upstream file is
  rejected and the next source is tried.

## Download Sources

Downloads are tried in this order, each verified against the pinned SHA-256 before it is accepted:

1. A user-configured mirror, when set.
2. Hugging Face (`vocab.txt` only).
3. GitHub Git LFS media endpoint for this repository.

## Licence

The base model is licensed under the Apache License 2.0. The quantized derivative and the vocabulary are
redistributed under the same licence, with attribution to the sentence-transformers project and the original
MiniLM authors.

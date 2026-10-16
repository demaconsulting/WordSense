# Model Provenance

This note records where each WordSense encoder model and its tokenizer vocabulary come from, how they are verified,
and the license terms that apply to them. There is one section per model. Every model has an identifier (the model
ID) that names its folder everywhere: `model/<model-id>/` in the repository, `<mirror>/<model-id>/` on a mirror,
`<store>/<model-id>/` in the per-user store and `<ref>/model/<model-id>/` on the GitHub LFS endpoint. The weights
table trained for a model is embedded in the package of that model as the resource
`weights.<model-id>.<onnx-sha256>.bin`, so the file name pairs the model ID with the SHA-256 of the ONNX file the
weights were trained against; a model file with another digest never matches those weights.

## all-MiniLM-L6-v2-int8

Package: `DemaConsulting.WordSense.MiniLM`. Weights table resource:
`weights.all-MiniLM-L6-v2-int8.afdb6f1a0e45b715d0bb9b11772f032c399babd23bfc31fed1c170afc848bdb1.bin` (in
`src/DemaConsulting.WordSense.MiniLM/Resources/`).

### Files

The digests below are the ones in `model/all-MiniLM-L6-v2-int8/SHA256SUMS`.

| File | Size (bytes) | SHA-256 |
| ---- | ------------ | ------- |
| `model_int8.onnx` | 22,972,370 | `afdb6f1a0e45b715d0bb9b11772f032c399babd23bfc31fed1c170afc848bdb1` |
| `vocab.txt` | 231,508 | `07eced375cec144d27c900241f3e339478dec958f92fddbc551f295c992038a3` |
| `tokenizer.json` | 711,661 | `da0e79933b9ed51798a3ae27893d3c5fa4a201126cef75586296df9b4d2c62a0` |

All files are in `model/all-MiniLM-L6-v2-int8/`.

### Origin

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

### Download Sources

Downloads are tried in this order, each verified against the pinned SHA-256 before it is accepted:

1. A user-configured mirror, when set: `{mirror}/all-MiniLM-L6-v2-int8/{file}`.
2. Hugging Face (`vocab.txt` only).
3. GitHub Git LFS media endpoint for this repository (`media.githubusercontent.com`).

All three files above are Git LFS objects, so one URL pattern
(`/media/<owner>/WordSense/{ref}/model/all-MiniLM-L6-v2-int8/{file}`) serves each of them. The SHA-256 values in the
table are of the real content, not of the LFS pointer files. The runtime downloader fetches only `model_int8.onnx`
and `vocab.txt` into `<store>/all-MiniLM-L6-v2-int8/`; `tokenizer.json` is needed only to regenerate the tokenizer
parity fixture and is fetched with `git lfs pull`.

### Adding a Model

A further model gets its own `model/<model-id>/` folder (files, `SHA256SUMS` and a README), its own section in this
note, a package that implements `IWordSenseModel`, and a weights table trained by the builder with
`build --model model --model-id <model-id> ... --weights <file>`.

## License

The base model is licensed under the Apache License 2.0. The quantized derivative and the vocabulary are
redistributed under the same license, with attribution to the sentence-transformers project and the original
MiniLM authors.

## Training Corpus Licences

The sentence corpus under `corpus/` is not shipped in the NuGet package; only the derived classifier vectors are.
Every real sentence has a row in `corpus/provenance/*.tsv` (hash id, source, licence, URL, contributor or work,
origin `real`, `authored` or `llm-corpus`). Accepted sources:

- Public domain: Project Gutenberg works of US public-domain titles (header and licence block stripped).
- CC0: the Tatoeba CC0 subset.
- Open licences with attribution recorded: Tatoeba sentences under CC BY 2.0 FR (contributor recorded),
  Wikipedia example sentences (CC BY-SA 4.0, article URL recorded) and Universal Dependencies English EWT
  sentences (CC BY-SA 4.0). These are isolated single sentences used as labeled examples.
- Single web excerpts (`web-excerpt`) are allowed only as a last resort; none were used.

Project-written sentences carry `MIT (project-authored)`. Bulk scraping of a copyrighted site, continuous text of
more than one sentence, and paywalled or terms-prohibited sources are not used. Raw downloads are kept in a
git-ignored cache and are never committed.

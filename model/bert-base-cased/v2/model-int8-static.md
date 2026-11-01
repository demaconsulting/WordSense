# model-int8-static.onnx

The int8 static encoder of `bert-base-cased` v2. `README.md` of the release describes the export, the upstream files and
the changes to the graph.

| | |
| --- | --- |
| Size | 108,950,399 bytes |
| SHA-256 | `137f128c6b7b300ef89a0c421b913270c700aa2297ee053f01b97b9fe901f18a` |
| Quantization | `int8-static` |

## Why this quantization

Dynamic int8 quantization (`int8-dynamic`, v1) measures the range of every activation on every run, and its integer
kernel can saturate on CPUs without VNNI, so the answers differ between x86 CPU types. In the static encoder the ranges
are fixed, and the weights have 7 bits, so a pair sum of the int8 kernel cannot saturate. The result is the same answers
on every x86 CPU at int8 speed and memory. The answers on ARM still differ slightly, because the float kernels of ARM
round differently and the activations are rounded to 8 bits after them. The measured figures are in the
[accuracy](https://github.com/demaconsulting/WordSense/blob/main/docs/user_guide/accuracy.md) document of the repository.

## How it was made

Script: [`tools/model-prep/make-bert-static-encoder.py`](https://github.com/demaconsulting/WordSense/blob/main/tools/model-prep/make-bert-static-encoder.py).
Python packages (pinned in `tools/model-prep/requirements.txt`; the bytes depend on them): onnx 1.22.0, onnxruntime
1.29.0, numpy 2.2.6, tokenizers 0.22.2, protobuf 7.35.1, ml_dtypes 0.5.4.

```text
python tools/model-prep/make-bert-static-encoder.py model-int8-static.onnx --upstream-revision <revision>
```

| Step | Result | Bytes | SHA-256 |
| ---- | ------ | ----: | ------- |
| 1. Download `onnx/model.onnx` (fp32) at the pinned revision | upstream file | placeholder | `TODO-PIN-UPSTREAM-SHA256` |
| 2. Cut at the last layer, expose `last_hidden_state`, drop the language-model head | cut graph | 431,231,249 | `638efba75102c18871c83414e9e89e023cd82b6ae9a5a57d691750af6e63dfa1` |
| 3. Convert to operator set 13 | fp32 input of the quantizer | 431,239,021 | `2d6bd1161d23bb4ea3d2ec9951dcc6fbe92d267cf60672fef10d7a57befe8f00` |
| 4. Quantize statically with the pinned ranges, 5. finalize | encoder without taps | 108,949,981 | `750ae69a6898f7edb7bf35e8f98fc67eb5152fb94b8cd591bff05fba90ad4d65` |
| 6. Add the taps of layers 9 to 11 | `model-int8-static.onnx` | 108,950,399 | `137f128c6b7b300ef89a0c421b913270c700aa2297ee053f01b97b9fe901f18a` |

The script checks the SHA-256 of every result and stops on a difference. `--fp32` starts from the result of step 3.

### Quantization

- ONNX Runtime `quantize_static`, QDQ format, MinMax calibration, per-channel symmetric int8 weights with
  `reduce_range` (7 bits), asymmetric uint8 activations.
- Only the 72 weight MatMuls and the three embedding tables (Gather) are quantized. The two attention MatMuls of each
  layer (scores and context), which multiply two activations, stay fp32.
- Finalizing removes the Q/DQ pairs on the MatMul outputs, gives the residual connections their float tensors back and
  turns the embedding Gathers into an int8 Gather with a DequantizeLinear, so that the graph has the structure of the
  v1 encoder.
- The script verifies that every quantized weight is within 64 in magnitude (255 × 64 × 2 = 32,640, below the int16
  limit) and that `last_hidden_state` equals the output of the encoder without taps bit for bit.

### Calibration

- `tools/model-prep/calib.tsv`: 2,000 sentences of 6 to 40 words, drawn with seed 20261031 by
  `tools/model-prep/make-calib.py` from the train split of the representative part of the open corpus
  (`corpus/open`; public domain or CC0 sentences, CC0 annotations). No development, test, held-out or gold sentence is
  used. SHA-256 `9dc59cfbf6d2a683f5a774c614e3a20eadbd30b5d15b05d72e1c57cfe794da5e`.
- `tools/model-prep/calib-ranges.json`: the minimum and maximum of the 195 activation tensors over those sentences
  (no text). SHA-256 `5819dc922af6da1bc912099249e5f47b289137e7da44c9847d93dbac51d3568a`. The build reads the ranges
  and does not measure them: measured on another CPU, 17 of the 443 scales differ by one float ulp, which changes 20% of
  the vectors. A new ranges file is a reviewed change.

### Reproduction

The workflow `.github/workflows/model-prep.yaml` rebuilds the file from the upstream export on a hosted runner and
checks every SHA-256 above.

# model-int8-dynamic.onnx

The int8 dynamic encoder of `bert-base-cased` v1. `README.md` of the release describes the export, the upstream file and
the changes to the graph.

| | |
| --- | --- |
| Size | 108,477,896 bytes |
| SHA-256 | `dcc10a647f1520c00e07fd2ee2b3ad383db94657bc97cba3e1915d27e98a962d` |
| Quantization | `int8-dynamic` |

## How it was made

The file is the upstream int8 export (`Xenova/bert-base-cased`, `onnx/model_int8.onnx`, 109,200,172 bytes, SHA-256
`b4505ab8506e2e7dd53c43f26570191e5b09180353b9c5ce1b764bad736bc841`) cut at the output of the last encoder layer:

```text
python tools/model-prep/make-bert-encoder.py model_int8.onnx model-int8-dynamic.onnx
```

The script needs the Python package `onnx` and changes no weight. The quantization is the one of the upstream file:
ONNX Runtime dynamic quantization, with int8 weights and activations that are quantized on every run from their observed
range. Nothing is calibrated.

## Why this quantization

The upstream export was the source at hand, and the file is small (the weights are 8 bits) and needs no calibration data.

## Known variance between CPUs

The activation ranges and the integer kernel of a dynamic encoder depend on the input and on the CPU. On x86 CPUs without
VNNI the u8 × s8 kernel can saturate its int16 pair sums, so the answers differ between x86 CPU types; ARM CPUs differ
from x86 and from each other. The measured figures are in the
[accuracy](https://github.com/demaconsulting/WordSense/blob/main/docs/user_guide/accuracy.md) document of the repository.
`bert-base-cased` v2 (`int8-static`) removes the difference between x86 CPUs.

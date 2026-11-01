# model-int8-dynamic.onnx

The int8 dynamic encoder of `all-MiniLM-L6-v2` v1. `README.md` of the release describes the export, the base model and
the changes to the graph.

| | |
| --- | --- |
| Size | 22,972,370 bytes |
| SHA-256 | `afdb6f1a0e45b715d0bb9b11772f032c399babd23bfc31fed1c170afc848bdb1` |
| Quantization | `int8-dynamic` |

## How it was made

The file was produced locally during the feasibility work for the first release: the base model was exported to ONNX
with the output `last_hidden_state`, then quantized with ONNX Runtime dynamic quantization (int8 weights; activations
quantized on every run from their observed range; nothing is calibrated). The exact commands, the tool versions and the
SHA-256 of the full-precision export were not recorded, so the file cannot be rebuilt byte for byte. The pinned SHA-256
above is the only identity of the file, and the weights and the head of the package were trained on exactly it.

## Why this quantization

Dynamic quantization needs no calibration data and gives a file of a quarter of the size of the full-precision model.

## Known variance between CPUs

The activation ranges and the integer kernel of a dynamic encoder depend on the input and on the CPU. On x86 CPUs without
VNNI the u8 × s8 kernel can saturate its int16 pair sums, so the answers can differ between CPU types, and ARM CPUs
differ from x86. The measured figures are in the
[accuracy](https://github.com/demaconsulting/WordSense/blob/main/docs/user_guide/accuracy.md) document of the repository.

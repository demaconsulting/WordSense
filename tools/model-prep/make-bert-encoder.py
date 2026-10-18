"""Derives the encoder-only ONNX file for bert-base-cased-int8.

The upstream int8 file (Xenova/bert-base-cased, onnx/model_int8.onnx) is a masked-language-model export that
only exposes `logits`. WordSense needs the final hidden states, so this script cuts the graph at the output of the
last encoder layer (`/bert/encoder/layer.11/output/LayerNorm/Add_1_output_0`), exposes it as `last_hidden_state`
and drops the language-model head. No weights are changed.

Usage: python make-bert-encoder.py <upstream-model_int8.onnx> <encoder_int8.onnx>
Requires the `onnx` Python package.
"""

import sys

import onnx
import onnx.utils

HIDDEN_TENSOR = "/bert/encoder/layer.11/output/LayerNorm/Add_1_output_0"


def main(source: str, target: str) -> None:
    """Extracts the encoder sub-graph and renames its output."""
    extracted = onnx.utils.Extractor(onnx.load(source)).extract_model(
        ["input_ids", "attention_mask", "token_type_ids"], [HIDDEN_TENSOR])

    # Rename the single output to the conventional name used by the encoder wrapper
    for node in extracted.graph.node:
        node.input[:] = ["last_hidden_state" if i == HIDDEN_TENSOR else i for i in node.input]
        node.output[:] = ["last_hidden_state" if o == HIDDEN_TENSOR else o for o in node.output]
    extracted.graph.output[0].name = "last_hidden_state"
    onnx.checker.check_model(extracted)
    onnx.save(extracted, target)


if __name__ == "__main__":
    main(sys.argv[1], sys.argv[2])

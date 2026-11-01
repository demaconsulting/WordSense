"""Derives the encoder file of bert-base-cased v2 (release asset model-int8-static.onnx) from the upstream fp32 export.

The recipe, step by step (every step that has a pinned result checks it and stops with a non-zero exit code on a mismatch):

 1. Download the upstream fp32 graph (Xenova/bert-base-cased, onnx/model.onnx) at the pinned revision.      [UPSTREAM_SHA256]
 2. Cut it at the output of the last encoder layer, expose that tensor as `last_hidden_state` and drop the
    language-model head (the same cut as v1; no weight changes).                                           [CUT_SHA256]
 3. Convert the graph to opset 13 (per-channel DequantizeLinear needs it; the outputs are bit-identical). [OP13_SHA256]
 4. Quantize statically with ONNX Runtime `quantize_static`: QDQ format, MinMax calibration, per-channel int8 weights with
    `reduce_range` (7-bit), asymmetric uint8 activations, only the 72 weight MatMuls and the embedding Gather tables.
    The activation x activation MatMuls (QK^T and attention x V) stay fp32. The calibration ranges are read from
    the pinned `calib-ranges.json`, not measured: the measured ranges differ by one ulp between CPU types, which
    moves 20% of the vectors.
 5. Finalize: drop the Q/DQ pairs on the weight MatMul outputs, give the residual consumers their float
    tensors back, and turn the embedding gathers into an int8 Gather plus DequantizeLinear.               [STATIC_SHA256]
 6. Add the layer taps: an Identity of the output of the final LayerNorm of layers 9, 10 and 11, exposed as the
    graph outputs hidden_9, hidden_10 and hidden_11 (layer 12 stays `last_hidden_state`).                  [OUTPUT_SHA256]
 7. Verify the structure (the weights stay within the 7-bit limit, so the int16 pair sum of an int8 kernel cannot
    saturate; the taps; the output order; the operator set) and that `last_hidden_state` equals the one of the
    graph before the taps, bit for bit, on calibration sentences.

Usage:
  python make-bert-static-encoder.py OUTPUT.onnx [--fp32 CUT_OP13.onnx | --upstream-revision SHA] [--work DIR]
  python make-bert-static-encoder.py --mode verify OUTPUT.onnx
  python make-bert-static-encoder.py --mode measure-ranges RANGES.json --fp32 CUT_OP13.onnx

`--fp32` names the result of step 3 (the cut graph at opset 13) and skips steps 1 to 3.
`--mode measure-ranges` measures the calibration ranges again. The result is CPU dependent in the last bit, so a new
ranges file is a reviewed change and never a step of the build.

The pins below fix the inputs and the outputs. The output depends on the exact versions in requirements.txt.
"""

import argparse
import hashlib
import json
import os
import sys
import tempfile
import urllib.request

import numpy as np
import onnx
import onnx.shape_inference
import onnx.utils
import onnxruntime as ort
from onnx import TensorProto, helper, numpy_helper, version_converter

HERE = os.path.dirname(os.path.abspath(__file__))
PLACEHOLDER = "TODO-PIN"

# --- Pins -----------------------------------------------------------------------------------------------------------------
UPSTREAM_REPO = "Xenova/bert-base-cased"
UPSTREAM_FILE = "onnx/model.onnx"
# PLACEHOLDER: the Hugging Face revision (commit SHA) of the upstream repository and the SHA-256 of onnx/model.onnx at that revision.
# Hugging Face could not be reached when the recipe was written. The workflow .github/workflows/model-prep.yaml resolves and prints
# both; replace the two values with what it prints. Until then pass --upstream-revision and --accept-unpinned-upstream.
UPSTREAM_REVISION = "TODO-PIN-UPSTREAM-REVISION"
UPSTREAM_SHA256 = "TODO-PIN-UPSTREAM-SHA256"
CUT_SHA256 = "638efba75102c18871c83414e9e89e023cd82b6ae9a5a57d691750af6e63dfa1"
CUT_SIZE = 431231249
OP13_SHA256 = "2d6bd1161d23bb4ea3d2ec9951dcc6fbe92d267cf60672fef10d7a57befe8f00"
OP13_SIZE = 431239021
STATIC_SHA256 = "750ae69a6898f7edb7bf35e8f98fc67eb5152fb94b8cd591bff05fba90ad4d65"
STATIC_SIZE = 108949981
OUTPUT_SHA256 = "137f128c6b7b300ef89a0c421b913270c700aa2297ee053f01b97b9fe901f18a"
OUTPUT_SIZE = 108950399
TOKENIZER_SHA256 = "343989712a36cd8b253efeaf8baf6a08b9d2583f78e395e83832e8ee9f8d8ee1"
CALIB_SHA256 = "9dc59cfbf6d2a683f5a774c614e3a20eadbd30b5d15b05d72e1c57cfe794da5e"
RANGES_SHA256 = "5819dc922af6da1bc912099249e5f47b289137e7da44c9847d93dbac51d3568a"

TAP_LAYERS = (9, 10, 11)
LAST_LAYER = 12
HIDDEN = 768
WEIGHT_LIMIT = 64  # reduce_range: a weight has at most 7 bits, so 255 * 64 * 2 = 32,640 < 32,767 cannot saturate an int16 pair sum
PARITY_SENTENCES = 50


def sha256_file(path):
    digest = hashlib.sha256()
    with open(path, "rb") as stream:
        for block in iter(lambda: stream.read(1 << 20), b""):
            digest.update(block)
    return digest.hexdigest()


def is_placeholder(value):
    return value.startswith(PLACEHOLDER)


def fail(message):
    print("ERROR: " + message, file=sys.stderr)
    sys.exit(1)


def check(label, path, expected_sha, expected_size=None, allow_unpinned=False):
    """Stops when a file is not the pinned one; a placeholder pin only prints the value to pin."""
    actual = sha256_file(path)
    size = os.path.getsize(path)
    print(f"{label}: {size} bytes, sha256 {actual}")
    if is_placeholder(expected_sha):
        if allow_unpinned:
            print(f"{label}: the pin is a placeholder; pin this value")
            return actual
        fail(f"{label}: the SHA-256 is not pinned yet; pin {actual} in {os.path.basename(__file__)}")
    if actual != expected_sha:
        fail(f"{label}: expected sha256 {expected_sha}, got {actual}")
    if expected_size is not None and size != expected_size:
        fail(f"{label}: expected {expected_size} bytes, got {size}")
    return actual


# --- Step 1: download -----------------------------------------------------------------------------------------------------
def download_upstream(revision, destination):
    url = f"https://huggingface.co/{UPSTREAM_REPO}/resolve/{revision}/{UPSTREAM_FILE}"
    print("downloading", url)
    with urllib.request.urlopen(url) as response, open(destination, "wb") as stream:
        while True:
            block = response.read(1 << 20)
            if not block:
                break
            stream.write(block)


# --- Step 2: cut ----------------------------------------------------------------------------------------------------------
def cut_fp32(source, destination):
    """Cuts the upstream graph at the output of the final LayerNorm of the last layer and exposes it as last_hidden_state."""
    model = onnx.load(source)
    last = [
        n for n in model.graph.node
        if n.name.startswith(f"/bert/encoder/layer.{LAST_LAYER - 1}/output/LayerNorm/") and n.op_type in ("Add", "LayerNormalization")
    ]
    target = last[-1].output[0]
    model = onnx.shape_inference.infer_shapes(model)
    cut = onnx.utils.Extractor(model).extract_model(["input_ids", "attention_mask", "token_type_ids"], [target])
    for node in cut.graph.node:
        node.input[:] = ["last_hidden_state" if i == target else i for i in node.input]
        node.output[:] = ["last_hidden_state" if o == target else o for o in node.output]
    cut.graph.output.pop()
    cut.graph.output.append(helper.make_tensor_value_info("last_hidden_state", TensorProto.FLOAT, ["batch", "sequence", HIDDEN]))
    onnx.checker.check_model(cut)
    onnx.save(cut, destination)


# --- Step 3: opset 13 -----------------------------------------------------------------------------------------------------
def to_opset13(source, destination):
    converted = version_converter.convert_version(onnx.load(source), 13)
    onnx.checker.check_model(converted)
    onnx.save(converted, destination)


# --- Step 4: static quantization ------------------------------------------------------------------------------------------
def read_sentences(path):
    return [line.rstrip("\n").split("\t") for line in open(path, encoding="utf-8")]


def quantize(fp32_op13, tokenizer_json, calib_tsv, ranges_json, destination, work):
    from onnxruntime.quantization import CalibrationDataReader, CalibrationMethod, QuantFormat, QuantType, quantize_static
    from onnxruntime.quantization import calibrate as cal
    from tokenizers import Tokenizer

    tokenizer = Tokenizer.from_file(tokenizer_json)
    tokenizer.no_truncation()
    tokenizer.no_padding()
    sentences = read_sentences(calib_tsv)[:2000]

    class Reader(CalibrationDataReader):
        def __init__(self):
            self.it = iter(sentences)

        def get_next(self):
            sentence = next(self.it, None)
            if sentence is None:
                return None
            ids = np.array([tokenizer.encode(sentence[1].split(" "), is_pretokenized=True).ids], dtype=np.int64)
            return {"input_ids": ids, "attention_mask": np.ones_like(ids), "token_type_ids": np.zeros_like(ids)}

        def rewind(self):
            self.it = iter(sentences)

    if ranges_json is not None:
        pinned = json.load(open(ranges_json, encoding="utf-8"))
        print("using the pinned calibration ranges:", ranges_json, len(pinned), "tensors")

        def no_collect(self, data_reader):
            return None

        def pinned_compute(self):
            data = {k: (np.array([v[0]], dtype=np.float32), np.array([v[1]], dtype=np.float32)) for k, v in pinned.items()}
            return cal.TensorsData(CalibrationMethod.MinMax, data)

        cal.MinMaxCalibrater.collect_data = no_collect
        cal.MinMaxCalibrater.compute_data = pinned_compute

    model = onnx.load(fp32_op13)
    initializers = {i.name for i in model.graph.initializer}
    # keep the activation x activation MatMuls (QK^T, attention x V) in fp32: quantize only the MatMuls with an initializer weight
    excluded = [n.name for n in model.graph.node if n.op_type == "MatMul" and not any(i in initializers for i in n.input)]
    del model
    os.makedirs(work, exist_ok=True)
    os.chdir(work)  # the calibrator writes its augmented graph into the working directory
    quantize_static(
        fp32_op13, destination, Reader(),
        quant_format=QuantFormat.QDQ,
        op_types_to_quantize=["MatMul", "Gather"],
        per_channel=True,
        reduce_range=True,
        activation_type=QuantType.QUInt8,
        weight_type=QuantType.QInt8,
        nodes_to_exclude=excluded,
        calibrate_method=CalibrationMethod.MinMax,
        extra_options={"ActivationSymmetric": False, "WeightSymmetric": True, "DedicatedQDQPair": True},
    )


def measure_ranges(fp32_op13, tokenizer_json, calib_tsv, destination, work):
    """Measures the min and max of every quantized activation over the calibration sentences."""
    from onnxruntime.quantization import calibrate as cal

    original = cal.MinMaxCalibrater.compute_data

    def dumping(self):
        result = original(self)
        ranges = {k: [float(v.range_value[0].ravel()[0]), float(v.range_value[1].ravel()[0])] for k, v in sorted(result.data.items())}
        json.dump(ranges, open(destination, "w", newline="\n"), indent=0)
        return result

    cal.MinMaxCalibrater.compute_data = dumping
    scratch = os.path.join(work, "measure-raw.onnx")
    quantize(fp32_op13, tokenizer_json, calib_tsv, None, scratch, work)


# --- Step 5: finalize -----------------------------------------------------------------------------------------------------
def finalize(source, destination):
    """Finalizes an ORT static QDQ model into the shape of the shipped graph (activation quantization only on the weight-MatMul inputs):
     1. drops the Q/DQ pair on the output of every weight MatMul (the integer matmul returns float: ORT fuses MatMulIntegerToFloat)
     2. every non-MatMul consumer of a quantized activation (residual Add, ...) reads the original float tensor again
     3. embedding Gather: Gather(int8 table) -> DequantizeLinear, no Q/DQ on the Gather outputs
    """
    model = onnx.load(source)
    graph = model.graph
    initializers = {i.name: i for i in graph.initializer}

    def maps():
        producers = {o: n for n in graph.node for o in n.output}
        consumers = {}
        for n in graph.node:
            for i in n.input:
                consumers.setdefault(i, []).append(n)
        return producers, consumers

    # 3. embedding gathers first
    producers, consumers = maps()
    gathers = 0
    new_nodes = []
    for node in list(graph.node):
        if node.op_type != "Gather":
            continue
        dequantize = producers.get(node.input[0])
        if dequantize is None or dequantize.op_type != "DequantizeLinear" or dequantize.input[0] not in initializers:
            continue
        table, scale = dequantize.input[0], dequantize.input[1]
        zero_point = dequantize.input[2] if len(dequantize.input) > 2 else None
        out = node.output[0]
        temporary = out + "_q8"
        node.input[0] = table
        node.output[0] = temporary
        new_nodes.append(helper.make_node("DequantizeLinear", [temporary, scale] + ([zero_point] if zero_point else []), [out], name=out + "_dq8"))
        gathers += 1
    graph.node.extend(new_nodes)

    producers, consumers = maps()
    remove, rename = set(), {}
    # 1. weight MatMul outputs
    pairs = 0
    for node in graph.node:
        if node.op_type != "MatMul":
            continue
        weight = producers.get(node.input[1])
        if not (weight is not None and weight.op_type == "DequantizeLinear"):
            continue
        quantize_nodes = consumers.get(node.output[0], [])
        if len(quantize_nodes) != 1 or quantize_nodes[0].op_type != "QuantizeLinear":
            continue
        dequantize_nodes = consumers[quantize_nodes[0].output[0]]
        assert len(dequantize_nodes) == 1 and dequantize_nodes[0].op_type == "DequantizeLinear"
        rename[dequantize_nodes[0].output[0]] = node.output[0]
        remove |= {id(quantize_nodes[0]), id(dequantize_nodes[0])}
        pairs += 1

    # 2. residual and other non-matmul consumers of quantized activations
    restored = 0
    for node in graph.node:
        if node.op_type != "DequantizeLinear" or id(node) in remove:
            continue
        quantize_node = producers.get(node.input[0])
        if quantize_node is None or quantize_node.op_type != "QuantizeLinear":
            continue
        original = quantize_node.input[0]
        for consumer in consumers.get(node.output[0], []):
            weight_matmul = (
                consumer.op_type == "MatMul"
                and consumer.input[0] == node.output[0]
                and producers.get(consumer.input[1]) is not None
                and producers[consumer.input[1]].op_type == "DequantizeLinear"
            )
            if weight_matmul:
                continue
            for j, i in enumerate(consumer.input):
                if i == node.output[0]:
                    consumer.input[j] = original
                    restored += 1
    for node in graph.node:
        for j, i in enumerate(node.input):
            if i in rename:
                node.input[j] = rename[i]
    keep = [n for n in graph.node if id(n) not in remove]

    # prune dead nodes
    live, needed = set(), [o.name for o in graph.output]
    by_output = {o: n for n in keep for o in n.output}
    while needed:
        tensor = needed.pop()
        node = by_output.get(tensor)
        if node is None or id(node) in live:
            continue
        live.add(id(node))
        needed += list(node.input)
    keep = [n for n in keep if id(n) in live]

    # topological order
    order, available, pending = [], {i.name for i in graph.initializer} | {i.name for i in graph.input}, list(keep)
    while pending:
        rest = []
        for node in pending:
            if all(i == "" or i in available for i in node.input):
                order.append(node)
                available.update(node.output)
            else:
                rest.append(node)
        assert len(rest) < len(pending), "cycle"
        pending = rest
    del graph.node[:]
    graph.node.extend(order)
    used = {i for n in graph.node for i in n.input}
    for initializer in [i for i in graph.initializer if i.name not in used]:
        graph.initializer.remove(initializer)
    onnx.checker.check_model(model)
    onnx.save(model, destination)
    print(f"finalize: {pairs} matmul output pairs dropped, {restored} non-matmul consumers restored to float, {gathers} embedding gathers")


# --- Step 6: taps ---------------------------------------------------------------------------------------------------------
def tap_source(layer):
    """The tensor that layer n (1-based) exposes: the output of the final LayerNorm of the encoder layer with the index n - 1."""
    return f"/bert/encoder/layer.{layer - 1}/output/LayerNorm/Add_1_output_0"


def add_taps(source, destination):
    """Adds an Identity of the output of each tapped layer and exposes it as hidden_<n>; the other nodes are not touched."""
    model = onnx.load(source)
    produced = {o for n in model.graph.node for o in n.output}
    for layer in TAP_LAYERS:
        if tap_source(layer) not in produced:
            fail(f"the graph has no tensor {tap_source(layer)} for the tap of layer {layer}")
        name = f"hidden_{layer}"
        model.graph.node.append(helper.make_node("Identity", [tap_source(layer)], [name], name=f"tap_{name}"))
        model.graph.output.append(helper.make_tensor_value_info(name, TensorProto.FLOAT, ["batch", "sequence", HIDDEN]))
    onnx.checker.check_model(model)
    onnx.save(model, destination)


# --- Step 7: verification -------------------------------------------------------------------------------------------------
def verify_structure(path):
    model = onnx.load(path)
    graph = model.graph
    errors = []
    opsets = {o.domain: o.version for o in model.opset_import}
    if opsets.get("", opsets.get("ai.onnx")) != 13:
        errors.append(f"the default operator set is {opsets.get('')}, not 13")
    expected_outputs = ["last_hidden_state"] + [f"hidden_{n}" for n in TAP_LAYERS]
    if [o.name for o in graph.output] != expected_outputs:
        errors.append(f"the outputs are {[o.name for o in graph.output]}, not {expected_outputs}")
    by_output = {o: n for n in graph.node for o in n.output}
    for layer in TAP_LAYERS:
        node = by_output.get(f"hidden_{layer}")
        if node is None or node.op_type != "Identity" or list(node.input) != [tap_source(layer)]:
            errors.append(f"hidden_{layer} is not an Identity of {tap_source(layer)}")
    # the 7-bit limit of the weights of the MatMuls: no int16 pair sum of an int8 kernel can saturate
    initializers = {i.name: i for i in graph.initializer}
    consumers = {}
    for n in graph.node:
        for i in n.input:
            consumers.setdefault(i, []).append(n)
    checked = 0
    for n in graph.node:
        if n.op_type != "DequantizeLinear" or n.input[0] not in initializers:
            continue
        if not any(c.op_type == "MatMul" for c in consumers.get(n.output[0], [])):
            continue
        weights = numpy_helper.to_array(initializers[n.input[0]])
        checked += 1
        if weights.dtype != np.int8 or int(np.abs(weights.astype(np.int16)).max()) > WEIGHT_LIMIT:
            errors.append(f"the weights {n.input[0]} exceed the 7-bit limit")
    if checked != 72:
        errors.append(f"{checked} quantized weight MatMuls, expected 72")
    if errors:
        fail("verification failed:\n  " + "\n  ".join(errors))
    print(f"structure: operator set 13, outputs {expected_outputs}, taps of {TAP_LAYERS}, {checked} weight tensors within +-{WEIGHT_LIMIT}")


def run_last_layer(path, sentences, tokenizer_json, threads):
    from tokenizers import Tokenizer

    tokenizer = Tokenizer.from_file(tokenizer_json)
    tokenizer.no_truncation()
    tokenizer.no_padding()
    options = ort.SessionOptions()
    options.intra_op_num_threads = threads
    options.inter_op_num_threads = 1
    options.log_severity_level = 3
    session = ort.InferenceSession(path, options, providers=["CPUExecutionProvider"])
    results = []
    for sentence in sentences:
        ids = np.array([tokenizer.encode(sentence[1].split(" "), is_pretokenized=True).ids], dtype=np.int64)
        feeds = {"input_ids": ids, "attention_mask": np.ones_like(ids), "token_type_ids": np.zeros_like(ids)}
        results.append(session.run(["last_hidden_state"], feeds)[0])
    return results


def verify_parity(untapped, tapped, tokenizer_json, calib_tsv, threads):
    sentences = read_sentences(calib_tsv)[:PARITY_SENTENCES]
    before = run_last_layer(untapped, sentences, tokenizer_json, threads)
    after = run_last_layer(tapped, sentences, tokenizer_json, threads)
    different = sum(1 for a, b in zip(before, after) if not np.array_equal(a, b))
    if different:
        fail(f"last_hidden_state differs from the untapped graph on {different} of {len(sentences)} sentences")
    print(f"parity: last_hidden_state is bit-identical to the graph without taps on {len(sentences)} sentences")


# --- Main -----------------------------------------------------------------------------------------------------------------
def main():
    parser = argparse.ArgumentParser(description=__doc__.split("\n")[0], formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("output", help="the file to write (build), the ranges file (measure-ranges) or the file to check (verify)")
    parser.add_argument("--mode", choices=["build", "measure-ranges", "verify"], default="build")
    parser.add_argument("--fp32", help="the cut graph at opset 13 (the result of step 3); skips the download, the cut and the conversion")
    parser.add_argument("--upstream-revision", default=UPSTREAM_REVISION, help="the Hugging Face revision of the upstream repository")
    parser.add_argument("--accept-unpinned-upstream", action="store_true", help="continue when the upstream SHA-256 is still a placeholder (it is printed)")
    parser.add_argument("--tokenizer", default=os.path.join(HERE, "..", "..", "model", "bert-base-cased", "v1", "tokenizer.json"))
    parser.add_argument("--calib", default=os.path.join(HERE, "calib.tsv"))
    parser.add_argument("--ranges", default=os.path.join(HERE, "calib-ranges.json"))
    parser.add_argument("--work", help="the working directory for the intermediate files (default: a temporary directory)")
    parser.add_argument("--threads", type=int, default=min(8, os.cpu_count() or 1), help="intra-op threads of the sessions of the verification")
    args = parser.parse_args()

    print("versions: onnx", onnx.__version__, "onnxruntime", ort.__version__, "numpy", np.__version__)
    if args.mode == "verify":
        verify_structure(args.output)
        if not is_placeholder(OUTPUT_SHA256):
            check("output", args.output, OUTPUT_SHA256, OUTPUT_SIZE)
        return

    check("tokenizer.json", args.tokenizer, TOKENIZER_SHA256)
    check("calib.tsv", args.calib, CALIB_SHA256)
    check("calib-ranges.json", args.ranges, RANGES_SHA256)
    work = os.path.abspath(args.work) if args.work else tempfile.mkdtemp(prefix="model-prep-")
    os.makedirs(work, exist_ok=True)
    output = os.path.abspath(args.output)
    tokenizer, calib, ranges = (os.path.abspath(p) for p in (args.tokenizer, args.calib, args.ranges))

    if args.fp32:
        op13 = os.path.abspath(args.fp32)
    else:
        if is_placeholder(args.upstream_revision):
            fail("the upstream revision is not pinned; pass --upstream-revision (the workflow model-prep resolves it)")
        upstream = os.path.join(work, "upstream-fp32.onnx")
        download_upstream(args.upstream_revision, upstream)
        check("upstream", upstream, UPSTREAM_SHA256, allow_unpinned=args.accept_unpinned_upstream)
        cut = os.path.join(work, "bert-fp32.onnx")
        cut_fp32(upstream, cut)
        check("cut graph", cut, CUT_SHA256, CUT_SIZE)
        op13 = os.path.join(work, "bert-fp32-op13.onnx")
        to_opset13(cut, op13)
    check("fp32 graph, opset 13", op13, OP13_SHA256, OP13_SIZE, allow_unpinned=args.accept_unpinned_upstream and not args.fp32)

    if args.mode == "measure-ranges":
        measure_ranges(op13, tokenizer, calib, output, work)
        print("wrote the measured ranges to", output, "(CPU dependent in the last bit; a new ranges file is a reviewed change)")
        return

    raw = os.path.join(work, "s3-raw.onnx")
    untapped = os.path.join(work, "s3.onnx")
    quantize(op13, tokenizer, calib, ranges, raw, work)
    finalize(raw, untapped)
    check("static int8 graph without taps", untapped, STATIC_SHA256, STATIC_SIZE)
    add_taps(untapped, output)
    verify_structure(output)
    verify_parity(untapped, output, tokenizer, calib, args.threads)
    check("output", output, OUTPUT_SHA256, OUTPUT_SIZE)


if __name__ == "__main__":
    main()

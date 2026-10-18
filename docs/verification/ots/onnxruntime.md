## ONNX Runtime Verification

This document provides the verification evidence for the ONNX Runtime OTS software item. ONNX Runtime is a dependency
of the Onnx package, `DemaConsulting.WordSense.Onnx`, and reaches the MiniLM and BertBaseCased packages through it; the
core and Basic packages do not use it. Requirements for this OTS item are defined in the ONNX Runtime OTS Software
Requirements document.

### Required Functionality

ONNX Runtime must load the pinned quantized models and return deterministic hidden states of the expected shape on every
supported platform.

### Verification Approach

ONNX Runtime is verified by running the real models in the test suite, on every platform and target framework of the CI
matrix: the encoder tests in `test/DemaConsulting.WordSense.Onnx.Tests` and the end-to-end tests in
`test/DemaConsulting.WordSense.MiniLM.Tests` and `test/DemaConsulting.WordSense.BertBaseCased.Tests`. The tests are
skipped locally only when the Git LFS model files have not been fetched; the CI checkout fetches them.

### Test Scenarios

#### OnnxEncoder_EncodeAsync_ValidTargets_ReturnsDeterministicVectors

**Scenario**: The MiniLM model is loaded and target words of a sentence are encoded twice.

**Expected**: Each target gets a vector of 384 values and the vectors are identical for both runs.

**Requirement coverage**: `WordSense-OTS-OnnxRuntime-Inference`.

#### WordSense_Classifier_EndToEnd_ClassifiesRecord

**Scenario**: A classifier built on the real MiniLM model, and one built on the real BertBaseCased model, each classify
a sentence containing two senses of "record".

**Expected**: The verb sense is assigned to the first occurrence (after "will") and the noun sense to the second
(after "the").

**Requirement coverage**: `WordSense-OTS-OnnxRuntime-Inference`.

### Requirements Coverage

- **`WordSense-OTS-OnnxRuntime-Inference`**: OnnxEncoder_EncodeAsync_ValidTargets_ReturnsDeterministicVectors,
  WordSense_Classifier_EndToEnd_ClassifiesRecord

## ONNX Runtime Verification

This document provides the verification evidence for the ONNX Runtime OTS software item.
Requirements for this OTS item are defined in the ONNX Runtime OTS Software Requirements document.

### Required Functionality

ONNX Runtime must load the pinned quantized model and return deterministic hidden states of the
expected shape on every supported platform.

### Verification Approach

ONNX Runtime is verified by running the real model in the test suite, on every platform and target
framework of the CI matrix. The tests are skipped locally only when the Git LFS model file has not
been fetched; the CI checkout fetches it.

### Test Scenarios

#### OnnxWordSenseEncoder_EncodeAsync_ValidIds_ReturnsHiddenStates

**Scenario**: The model is loaded and three token ids are encoded twice.

**Expected**: The result has `3 × 384` values, is identical for both runs, and the encoder reports the
pinned model digest.

**Requirement coverage**: `WordSense-OTS-OnnxRuntime-Inference`.

#### WordSense_Classifier_EndToEnd_ClassifiesRecord

**Scenario**: A classifier built on the real model classifies a sentence containing two senses of
"record".

**Expected**: The noun sense is assigned to the first and the verb sense to the second occurrence.

**Requirement coverage**: `WordSense-OTS-OnnxRuntime-Inference`.

### Requirements Coverage

- **`WordSense-OTS-OnnxRuntime-Inference`**: OnnxWordSenseEncoder_EncodeAsync_ValidIds_ReturnsHiddenStates,
  WordSense_Classifier_EndToEnd_ClassifiesRecord

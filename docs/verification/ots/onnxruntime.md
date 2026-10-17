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

#### MiniLMWordSenseModel_EncodeAsync_ValidTargets_ReturnsDeterministicVectors

**Scenario**: The model is loaded and target words of a sentence are encoded twice.

**Expected**: Each target gets a vector of 384 values, the vectors are identical for both runs, and the
model reports the pinned model digest.

**Requirement coverage**: `WordSense-OTS-OnnxRuntime-Inference`.

#### WordSense_Classifier_EndToEnd_ClassifiesRecord

**Scenario**: A classifier built on the real model classifies a sentence containing two senses of
"record".

**Expected**: The noun sense is assigned to the first and the verb sense to the second occurrence.

**Requirement coverage**: `WordSense-OTS-OnnxRuntime-Inference`.

### Requirements Coverage

- **`WordSense-OTS-OnnxRuntime-Inference`**: MiniLMWordSenseModel_EncodeAsync_ValidTargets_ReturnsDeterministicVectors,
  WordSense_Classifier_EndToEnd_ClassifiesRecord

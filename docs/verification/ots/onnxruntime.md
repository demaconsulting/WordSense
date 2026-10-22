## ONNX Runtime Verification

This document provides the verification evidence for the ONNX Runtime OTS software item. ONNX Runtime is a dependency
of the Onnx package, `DemaConsulting.WordSense.Onnx`, and reaches the MiniLM and BertBaseCased packages through it; the
core package, which holds the Basic model, does not use it. Requirements for this OTS item are defined in the ONNX
Runtime OTS Software
Requirements document.

### Required Functionality

ONNX Runtime must load the pinned quantized models (all-MiniLM-L6-v2-int8 and bert-base-cased-int8) and return
deterministic hidden states of the expected shape on every supported platform. The int8 arithmetic is deterministic on
one machine but may differ between CPUs and operating systems (probabilities by up to about 0.02, and a word whose
margin lies close to its threshold can be answered one level more or less precisely), so the end-to-end scenarios assert
the answered part of speech and not exact probabilities.

### Verification Approach

ONNX Runtime is verified by running the real models in the test suite, on every platform and target framework of the CI
matrix: the encoder tests in `test/DemaConsulting.WordSense.Onnx.Tests` and the end-to-end tests in
`test/DemaConsulting.WordSense.MiniLM.Tests` and `test/DemaConsulting.WordSense.BertBaseCased.Tests`. The tests are
skipped locally only when the Git LFS model files have not been fetched; the CI checkout fetches them, verifies that
they are real content and runs the tests with `WORDSENSE_REQUIRE_MODELS=true`, so that a missing model fails the run.

### Test Scenarios

#### OnnxEncoder_EncodeAsync_ValidTargets_ReturnsDeterministicVectors

**Scenario**: The MiniLM model is loaded and target words of a sentence are encoded twice.

**Expected**: Each target gets a vector of 384 values and the vectors are identical for both runs.

**Requirement coverage**: `WordSense-OTS-OnnxRuntime-Inference`.

#### WordSense_Classifier_EndToEnd_ClassifiesRecord

**Scenario**: A model built on the real MiniLM model files classifies a sentence containing two senses of "record".

**Expected**: The verb analysis is assigned to the first occurrence (after "will") and the noun analysis to the second
(after "the"), each at a level that the result states.

**Requirement coverage**: `WordSense-OTS-OnnxRuntime-Inference`.

#### BertBaseCased_Classifier_EndToEnd_ClassifiesRecord

**Scenario**: A model built on the real BertBaseCased model files classifies the same sentence.

**Expected**: The verb analysis is assigned to the first occurrence of "record" and the noun analysis to the second, the
period is unknown, every confidence is between 0 and 1 and no answer is at the group level.

**Requirement coverage**: `WordSense-OTS-OnnxRuntime-Inference`.

#### MiniLMWordSenseModel_Classify_TrainedLemma_AnswersAtASenseOrGroupLevel, BertBaseCasedWordSenseModel_Classify_TrainedLemma_AnswersAtASenseOrGroupLevel

**Scenario**: The model of each package classifies "I will go back to the house.", where `back` is a lemma the encoder
was trained for, so the ONNX Runtime session of that model runs for the word.

**Expected**: `back` is answered at the sense or the group level with ranked senses that sum to one in the selected
analysis.

**Requirement coverage**: `WordSense-OTS-OnnxRuntime-Inference`.

### Requirements Coverage

- **`WordSense-OTS-OnnxRuntime-Inference`**: OnnxEncoder_EncodeAsync_ValidTargets_ReturnsDeterministicVectors,
  WordSense_Classifier_EndToEnd_ClassifiesRecord, BertBaseCased_Classifier_EndToEnd_ClassifiesRecord,
  MiniLMWordSenseModel_Classify_TrainedLemma_AnswersAtASenseOrGroupLevel,
  BertBaseCasedWordSenseModel_Classify_TrainedLemma_AnswersAtASenseOrGroupLevel

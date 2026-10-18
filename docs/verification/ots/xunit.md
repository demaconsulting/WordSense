## xUnit Verification

This document provides the verification evidence for the xUnit OTS software item. Requirements
for this OTS item are defined in the xUnit OTS Software Requirements document.

### Required Functionality

xUnit v3 (xunit.v3 and xunit.runner.visualstudio) is the unit-testing framework used by the
project. It discovers and runs all test methods and writes TRX result files that feed into coverage
reporting and requirements traceability. Passing tests confirm the framework is functioning
correctly.

### Verification Approach

xUnit is verified by self-validation evidence from the CI pipeline. Each scenario names a specific
test method that xUnit must discover, execute, and record in a TRX result file. A passing pipeline
run for all scenarios constitutes evidence that both requirements are satisfied.

### Test Scenarios

#### WordsBlob_Read_UnknownMagic_Throws

**Scenario**: xUnit discovers and runs this test (core test project); the test verifies that a lexicon blob with an
unknown magic number is refused.

**Expected**: xUnit executes the test, the test passes, and the result appears in the TRX output.

**Requirement coverage**: `WordSense-OTS-xUnit-Execute`, `WordSense-OTS-xUnit-Report`.

#### WordPieceTokenizer_Tokenize_EmptyText_ReturnsNoTokens

**Scenario**: xUnit discovers and runs this test (Onnx test project); the test verifies that empty text yields no
tokens.

**Expected**: xUnit executes the test, the test passes, and the result appears in the TRX output.

**Requirement coverage**: `WordSense-OTS-xUnit-Execute`, `WordSense-OTS-xUnit-Report`.

#### WeightsBlob_Read_UnknownMagic_Throws

**Scenario**: xUnit discovers and runs this test (Onnx test project); the test verifies that a weights blob with an
unknown magic number is refused.

**Expected**: xUnit executes the test, the test passes, and the result appears in the TRX output.

**Requirement coverage**: `WordSense-OTS-xUnit-Execute`, `WordSense-OTS-xUnit-Report`.

### Requirements Coverage

- **`WordSense-OTS-xUnit-Execute`**: WordsBlob_Read_UnknownMagic_Throws,
  WordPieceTokenizer_Tokenize_EmptyText_ReturnsNoTokens, WeightsBlob_Read_UnknownMagic_Throws
- **`WordSense-OTS-xUnit-Report`**: WordsBlob_Read_UnknownMagic_Throws,
  WordPieceTokenizer_Tokenize_EmptyText_ReturnsNoTokens, WeightsBlob_Read_UnknownMagic_Throws

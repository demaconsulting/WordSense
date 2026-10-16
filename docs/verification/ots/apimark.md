## ApiMark Verification

This document provides the verification evidence for the ApiMark OTS software item. Requirements
for this OTS item are defined in the ApiMark OTS Software Requirements document.

### Required Functionality

DemaConsulting.ApiMark generates the API reference for the WordSense package from its XML doc
comments, organized for gradual disclosure. A documented example must render as a fenced code
block and the terminology remarks must survive into the generated pages. ApiMark is also relied
upon to enforce documentation coverage: `ApiMarkEnforceDocs=Public` with severity `Error` fails
the build when a public member lacks an XML summary.

### Verification Approach

ApiMark is verified by asserting the **real generated output of the real build**. The MSBuild
integration runs during the ordinary `dotnet build`, and the CI workflow then runs FileAssert
against the generated files in the same job, before the test step. ApiMark's `--validate`
self-test is not used because it does not self-test generation.

Two assertions check content rather than existence, because a complete-looking file tree could
still hide examples rendered as prose or dropped remarks.

### Test Scenarios

#### ApiMark_ApiIndex

**Scenario**: ApiMark generates the API reference during the build.

**Expected**: `src/DemaConsulting.WordSense/generated/api/api.md` exists and names the
`DemaConsulting.WordSense` namespace.

**Requirement coverage**: `WordSense-OTS-ApiMark-Generate`.

#### ApiMark_ExampleCodeRendered

**Scenario**: A type documented with an `<example>` containing a `<code>` block is generated.

**Expected**: The `WordSenseClassifier` type page contains a fenced `csharp` code block.

**Requirement coverage**: `WordSense-OTS-ApiMark-RenderExamples`.

#### ApiMark_TerminologyDocumented

**Scenario**: The shared terminology remarks on `IWordSenseClassifier` are generated.

**Expected**: The `IWordSenseClassifier` type page contains the term "abstain".

**Requirement coverage**: `WordSense-OTS-ApiMark-RenderExamples`.

#### ApiMark_ClassifierDocumented

**Scenario**: Documentation-coverage enforcement is active and the classifier factory is
documented and generated.

**Expected**: The `WordSenseClassifier.CreateAsync` member page exists and contains a fenced
`csharp` example.

**Rationale**: Enforcement is verified by the build itself: with severity `Error`, a green build
cannot coexist with an undocumented public member. Enforcement was also demonstrated by observed
green-to-red-to-green transitions when an undocumented public property was temporarily added.

**Requirement coverage**: `WordSense-OTS-ApiMark-EnforceDocs`.

### Requirements Coverage

- **`WordSense-OTS-ApiMark-Generate`**: ApiMark_ApiIndex
- **`WordSense-OTS-ApiMark-RenderExamples`**: ApiMark_ExampleCodeRendered,
  ApiMark_TerminologyDocumented
- **`WordSense-OTS-ApiMark-EnforceDocs`**: ApiMark_ClassifierDocumented

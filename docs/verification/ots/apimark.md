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

Each assertion checks content rather than existence, because a complete-looking file tree could
still hide examples rendered as prose or dropped remarks.

### Test Scenarios

#### ApiMark_ApiIndex

**Scenario**: ApiMark generates the API reference during the build.

**Expected**: `src/DemaConsulting.WordSense/generated/api/api.md` exists and names the
`DemaConsulting.WordSense` namespace.

**Requirement coverage**: `WordSense-OTS-ApiMark-Generate`.

#### ApiMark_ExampleCodeRendered

**Scenario**: A type documented with an `<example>` containing a `<code>` block is generated.

**Expected**: The `WordLexicon` type page contains a fenced `csharp` code block.

**Requirement coverage**: `WordSense-OTS-ApiMark-RenderExamples`.

#### ApiMark_TerminologyDocumented

**Scenario**: The shared terminology remarks on `WordSense` are generated.

**Expected**: The `WordSense` type page contains the term "confidence".

**Requirement coverage**: `WordSense-OTS-ApiMark-RenderExamples`.

#### ApiMark_ClassifierDocumented

**Scenario**: The model contract type is documented and its page is generated.

**Expected**: The `IWordSenseModel` type page exists and contains a fenced
`csharp` example.

**What this proves**: only that the member page was generated from its documentation. It does not
by itself prove enforcement. Enforcement is proven by the build failing when a public member lacks
an XML summary: with severity `Error`, a green build cannot coexist with an undocumented public
member, and this was observed manually as green-to-red-to-green transitions when an undocumented
public property was temporarily added. That observation is not an automated test.

**Requirement coverage**: `WordSense-OTS-ApiMark-EnforceDocs`.

#### WordSense_XmlDocExamples_Compile

**Scenario**: Every `<example><code>` block in the shipped XML documentation file is compiled with
Roslyn against the real compiled API (xUnit test in the main test project).

**Expected**: All documented examples compile without errors.

**Requirement coverage**: supporting evidence for `WordSense-OTS-ApiMark-RenderExamples`, because
the rendered examples are only useful to a reader if they are correct.

### Requirements Coverage

- **`WordSense-OTS-ApiMark-Generate`**: ApiMark_ApiIndex
- **`WordSense-OTS-ApiMark-RenderExamples`**: ApiMark_ExampleCodeRendered,
  ApiMark_TerminologyDocumented, WordSense_XmlDocExamples_Compile
- **`WordSense-OTS-ApiMark-EnforceDocs`**: ApiMark_ClassifierDocumented

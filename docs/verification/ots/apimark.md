## ApiMark Verification

This document provides the verification evidence for the ApiMark OTS software item. Requirements
for this OTS item are defined in the ApiMark OTS Software Requirements document.

### Required Functionality

DemaConsulting.ApiMark generates the API reference for the four WordSense packages (core, Onnx, MiniLM and
BertBaseCased) from their XML doc
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

**Scenario**: The shared terminology remarks on `ClassifyResult` are generated.

**Expected**: The `ClassifyResult` type page contains the term "confidence".

**Requirement coverage**: `WordSense-OTS-ApiMark-RenderExamples`.

#### ApiMark_ModelContractDocumented

**Scenario**: The model contract type is documented and its page is generated.

**Expected**: The `IWordSenseModel` type page exists and contains a fenced
`csharp` example.

#### ApiMark_BasicModelDocumented, ApiMark_OnnxModelDocumented, ApiMark_MiniLMModelDocumented, ApiMark_BertBaseCasedModelDocumented

**Scenario**: The model class of each of the other packages (the Basic model of the core package, Onnx, MiniLM and
BertBaseCased) is documented and its page is generated.

**Expected**: The `BasicWordSenseModel`, `OnnxWordSenseModel`, `MiniLMWordSenseModel` and
`BertBaseCasedWordSenseModel` type pages exist and each contains a fenced `csharp` example.

**What these prove**: only that the member pages were generated from their documentation. They do not
by themselves prove enforcement. Enforcement is proven by the build failing when a public member lacks
an XML summary: with severity `Error`, a green build cannot coexist with an undocumented public
member, and this was observed manually as green-to-red-to-green transitions when an undocumented
public property was temporarily added. That observation is not an automated test.

**Requirement coverage**: `WordSense-OTS-ApiMark-EnforceDocs`.

#### WordSense_XmlDocExamples_Compile

**Scenario**: Every `<example><code>` block in the shipped XML documentation file of a package is compiled with
Roslyn against the real compiled API (an xUnit test in the test project of each package:
`WordSense_XmlDocExamples_Compile`, `OnnxWordSense_XmlDocExamples_Compile`, `MiniLMWordSense_XmlDocExamples_Compile`
and `BertBaseCasedWordSense_XmlDocExamples_Compile`).

**Expected**: All documented examples compile without errors.

**Requirement coverage**: supporting evidence for `WordSense-OTS-ApiMark-RenderExamples`, because
the rendered examples are only useful to a reader if they are correct.

### Manual Check: API Documentation Quiz

The automated scenarios prove that pages are generated; they do not prove that the generated documentation is enough to
use the library. That is checked by a repeatable manual quiz (not part of the automated test run):

1. Delete the generated API documents of all packages (`src/*/generated/api`, untracked build output).
2. Build the solution once in the Release configuration, so that ApiMark rewrites them.
3. Copy the generated API documents of the four packages (`DemaConsulting.WordSense`, `.Onnx`, `.MiniLM` and
   `.BertBaseCased`) to a fresh temporary folder, and nothing else: no source, README, user guide or build notes.
4. Start an isolated reader that can read only that folder. It answers each question below from the documents alone,
   quotes the supporting document and marks the answer PASS (complete, specific and consistent) or GAP (missing, vague or
   contradictory, with what is missing).
5. Verify every GAP against the code (never document a guess), fix it in the XML documentation, the namespace
   documentation, the README or the user guide, and repeat from step 1 until every question is PASS.

The questions: (1) classify end to end with each of the three models, including the packages, `EnsureModelAsync` with the
default download, a mirror and an offline model directory, `ClassifyAsync` and how to read `Details` and `Confidence`;
(2) list the candidates of a word such as `read` with `WordLexicon`, including the keys, a missing word and the entries;
(3) choose a model, with the measured accuracy, download size, latency and memory, and when Basic is enough; (4) threshold
the confidence, including its meaning per model, the special values 0 and 1 and whether a threshold can be reused across
models; (5) unknown words and unresolved ambiguity, with an example of each; (6) thread safety, disposal and the cost of
first use; (7) offline use and failures, including the store location, the environment variables, the files verified
against SHA-256 and the exceptions of `EnsureModelAsync`; (8) tokenization, including punctuation, contractions, hyphens,
curly apostrophes, sentences per call and the maximum input length; (9) disposal and ownership, including disposal during
a download and after disposal; (10) laziness of the model files and of the ensure, including concurrent callers and retry
after a failure; (11) the public contract of `IWordSenseModel` and what a third-party implementation must honor.

Five rounds were run. Round 1 found gaps in every question, round 2 left minor gaps and one gap in tokenization, and the
later rounds, with questions 9 to 11 added, found consistency gaps in disposal, cancellation, thresholds, environment
variable rules and the contract for implementers; every real gap was verified against the code and fixed in the
documentation. The last round was not repeated after its fixes, so the quiz should be run again after any change to the
public API documentation and before a release that changes it.

### Requirements Coverage

- **`WordSense-OTS-ApiMark-Generate`**: ApiMark_ApiIndex
- **`WordSense-OTS-ApiMark-RenderExamples`**: ApiMark_ExampleCodeRendered,
  ApiMark_TerminologyDocumented, WordSense_XmlDocExamples_Compile, OnnxWordSense_XmlDocExamples_Compile,
  MiniLMWordSense_XmlDocExamples_Compile, BertBaseCasedWordSense_XmlDocExamples_Compile
- **`WordSense-OTS-ApiMark-EnforceDocs`**: ApiMark_ModelContractDocumented, ApiMark_BasicModelDocumented,
  ApiMark_OnnxModelDocumented, ApiMark_MiniLMModelDocumented, ApiMark_BertBaseCasedModelDocumented

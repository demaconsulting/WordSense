## ApiMark

### Purpose

DemaConsulting.ApiMark generates the API reference for the WordSense package from its XML doc
comments. It was chosen because its output is organized for *gradual disclosure* (an index page,
then a page per namespace, type, and member), which matches the stated audience: a coding agent
learning how to use the library, which reads a small targeted page far more effectively than one
large document. Generating the reference from the doc comments keeps it from drifting away from
the code it describes.

### Features Used

- MSBuild integration (`DemaConsulting.ApiMark.MSBuild`), so the reference is produced by the
  ordinary build rather than by a separate command
- Output redirection (`ApiMarkOutputDir`) into `src/DemaConsulting.WordSense/generated/api`
- Package inclusion (`ApiMarkPackDocs`), which places the generated `api/` tree in the NuGet package
- Documentation-coverage enforcement (`ApiMarkEnforceDocs`, `ApiMarkEnforceDocsSeverity`)
- Rendering of `<example>`/`<code>` blocks as fenced code blocks and of `<remarks>` prose
- Namespace description from a `NamespaceDoc` carrier class (`NamespaceDoc.cs`, an `internal static class NamespaceDoc`
  in the namespace, which is ApiMark's documented convention; ApiMark recognizes it by that shape and omits it
  from the type list and from enforcement; its XML summary, remarks and example become the namespace page; without
  it the namespace shows "No description provided"). The carrier's XML documentation is written to cover the
  quickstart, package and target frameworks, offline and mirror setup, the `WordAnalysis`, `WordDetails`,
  `PartOfSpeech` and `Pronunciation` result model, a glossary, confidence (margin and runner-up), tokenization
  and context window, thread safety and examples. The carrier and the example-compile test are in the
  OTS-ApiMark review set because they are ApiMark's inputs and evidence. FileAssert does not assert that
  content of the namespace page (it asserts only the index, the classifier example block, the terminology on
  `IWordSenseClassifier` and the
  `WordLexicon` page); the carrier's examples are compiled by `WordSense_XmlDocExamples_Compile`. References to
  repository documents in
  XML docs use absolute GitHub URLs, because the packaged API reference has no access to repository files.

### Integration Pattern

ApiMark is referenced from the library project as a build-time-only `PackageReference` with
`PrivateAssets="All"`, so it is never a transitive dependency of consumers and is never shipped as
runtime code. The test project does not reference it and therefore produces no API documentation.
Versions are pinned inline in the project file; there is no central package management.

The library project sets these properties:

- **`ApiMarkOutputDir`**: `$(MSBuildProjectDirectory)\generated\api`. The `generated/` folder is
  already excluded by `.gitignore`, `.markdownlint-cli2.yaml`, and `.cspell.yaml`, and covered by
  the rule that agents never read, lint, or modify generated content. The reference is therefore
  generated at build time and not committed, so no staleness check is needed.
- **`ApiMarkVisibility`**: `Public`.
- **`ApiMarkPackDocs`**: `true`, shipping the reference inside the `.nupkg`.
- **`ApiMarkEnforceDocs`**: `Public`, matching the visibility so enforcement covers exactly the
  generated surface.
- **`ApiMarkEnforceDocsSeverity`**: `Error`, because the build treats warnings as errors and
  a warning nobody reads is no enforcement.

The targets run once per build, for the first target framework only, which is correct because the
public surface is identical across `net8.0`, `net9.0`, and `net10.0`. Regeneration requires a real
rebuild (`--no-incremental`), since an up-to-date incremental build skips the task.

### Documentation Enforcement

Enforcement was confirmed active by observation: adding a public property without an XML summary
made the build fail with `[Undocumented] Property` and `ApiMark.Tool exited with code 1`; removing
it returned the build to green. This observation is manual evidence and is not repeated by an
automated test. Whether ApiMark flags a missing summary on an implicit (compiler-generated)
constructor was not observed, so enforcement is not claimed for that case.

### Example Correctness

Examples are written for agents, so a wrong example is worse than none. The test
`WordSense_XmlDocExamples_Compile` extracts every `<example><code>` block from the shipped XML
documentation file and compiles it with Roslyn against the real compiled API.

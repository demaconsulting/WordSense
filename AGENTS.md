# Project Overview

- **project-name**: WordSense
- **organization**: DEMA Consulting
- **project-tagline**: Offline word-sense disambiguation for .NET
- **description**: WordSense is a .NET library that identifies the meaning of an English word in context, offline: its
  lemma, its part of speech and features and, where the senses allow, its sense (an Open English WordNet fine sense with
  a readable id such as `bank#financial-institution`, linked to its WordNet sense key) or the group of closely related
  senses it belongs to. Every answer states its level (sense, group or part of speech), carries a confidence for that
  level and lists the candidate analyses with their probabilities and, where scored, the senses inside each. It ships as
  four layered NuGet packages: `DemaConsulting.WordSense`,
  the core (public contracts, embedded lexicon and sense catalog with build-time inflections, and the built-in
  statistical part-of-speech tagger and context-cue model `BasicWordSenseModel`; no ONNX dependency, no model files);
  `DemaConsulting.WordSense.Onnx` (shared WordPiece tokenizer, ONNX encoder, verified model downloader with mirror
  support and lemma-weights machinery); and the two concrete models `DemaConsulting.WordSense.MiniLM` and
  `DemaConsulting.WordSense.BertBaseCased`, each pinning its model files and embedding its own weights (fine-sense
  classifiers whose scores are corrected to the usage of the senses). Onnx
  depends only on core; MiniLM and BertBaseCased depend on core and Onnx. No CC
  BY-SA material is shipped; the training corpus is project-authored CC0 text and US public domain sources.
- **languages**: C#, Python (one-off tokenizer fixture generator, the quota-fill scripts `tools/quota-fill`, the model
  preparation scripts `tools/model-prep` and the open-corpus scripts `tools/open-corpus`)
- **technologies**: .NET, ONNX Runtime, Git LFS

# Project Structure

```text
├── corpus/                     (training anchors, holdout sentences, form maps; corpus/open is the open
│                                part-of-speech corpus and the tag counts of the Basic tagger; corpus/suite is the
│                                reviewed suite with the expectation of each model; corpus/shapes is the held-out shape
│                                probe set; corpus/allwords is the independent
│                                all-words gold set of the usage prior and corpus/allwords/v2 the second,
│                                document-disjoint all-words gold set for part of speech, tense, lemma and sense)
├── data/                       (word data TSV tables from Open English WordNet, schema.yaml)
├── model/                      (one folder per model: model/<model-id>/ with ONNX encoder, vocabulary, tokenizer,
│                                SHA256SUMS and README; stored in Git LFS)
├── tools/
│   ├── WordSense.Builder/      (non-shipped builder: compile-image, weights, train-pos-head, evaluate, evaluate-senses,
│   │                            evaluate-allwords, allwords-sample, allwords-merge, gold2-power, gold2-sample,
│   │                            gold2-batches, gold2-merge, evaluate-gold2, compare-gold2,
│   │                            evaluate-running, compare-outcomes, evaluate-suite, evaluate-shapes, check-leakage,
│   │                            tiers, open-counts, train-basic,
│   │                            tune-basic, evaluate-tagger, appendix)
│   ├── open-corpus/            (Python scripts that built the open part-of-speech corpus corpus/open)
│   ├── quota-fill/             (Python scripts that fill the per-sense quotas of the corpus, and the usage-weighted
│   │                            evaluation diagnostic)
│   ├── model-prep/             (prepares the pinned encoder model files)
│   └── tokenizer-fixture/      (one-off Python tokenizer parity fixture generator)
├── docs/
│   ├── build_notes/
│   ├── code_quality/
│   ├── code_review_plan/
│   ├── code_review_report/
│   ├── design/
│   ├── requirements_doc/
│   ├── requirements_report/
│   ├── reqstream/
│   ├── sysml2/
│   ├── user_guide/
│   └── verification/
├── src/
│   ├── DemaConsulting.WordSense/                (core: contracts, WordLexicon, embedded wordsense.img, the Basic
│   │                                             statistical model and its embedded basic.bin; no files, no network)
│   ├── DemaConsulting.WordSense.Onnx/           (tokenizer, encoder, downloader, lemma weights, POS head, abstract model)
│   ├── DemaConsulting.WordSense.MiniLM/         (MiniLM model: pinned files, embedded weights and head)
│   └── DemaConsulting.WordSense.BertBaseCased/  (BERT base cased model: pinned files, embedded weights and head)
└── test/
    ├── DemaConsulting.WordSense.Tests/
    ├── DemaConsulting.WordSense.Onnx.Tests/
    ├── DemaConsulting.WordSense.MiniLM.Tests/
    ├── DemaConsulting.WordSense.BertBaseCased.Tests/
    └── WordSense.Builder.Tests/
```

# Language and Spelling (ALL Agents)

Always use **US English** spelling in all output (code, comments, documentation,
commit messages, and reports).

# Documentation (ALL Agents)

- Docs describe the current state only. Git holds the history: no "before and after", "previously" or change logs.
- State each measured figure in one place and link to it from elsewhere.
- Write concisely, in short sentences. Cut qualifiers and repetition.
- The README stays near 1,200 words and the user guide near 6,000 words.
- Explain a moved suite expectation in the commit message and the PR description, never in a README.

# Reference Template

This repository follows a reference template for structure and file conventions.

- **template-url**: `https://github.com/demaconsulting/Agents/raw/refs/heads/template`
- **Repository map**: `{template-url}/repository-map.md`
- **Template files**: `{template-url}/{file-path}` for files described in the map

# Codebase Navigation (ALL Agents)

When working with source code, design, or requirements artifacts, query the SysML2
architecture model under `docs/sysml2/` first (see the `sysml2tools-query` skill) to
understand software structure, purpose, and relationships. Fall back to
`docs/design/introduction.md` for the human-facing narrative, folder layout, and
companion artifact locations, and use it as the primary map when the model doesn't
yet cover something.

# Key Configuration Files

- **`.config/dotnet-tools.json`** - Local tool manifest for Continuous Compliance tools
- **`.editorconfig`** - Code formatting rules
- **`.clang-format`** - C/C++ formatting (if applicable)
- **`.cspell.yaml`** - Spell-check configuration and technical term dictionary
- **`.markdownlint-cli2.yaml`** - Markdown formatting rules
- **`.yamllint.yaml`** - YAML formatting configuration
- **`.reviewmark.yaml`** - File review definitions and tracking
- **`nuget.config`** - NuGet package sources (if .NET)
- **`package.json`** - Node.js dependencies for formatting tools
- **`requirements.yaml`** - Root requirements file with includes
- **`pip-requirements.txt`** - Python dependencies for yamllint and yamlfix
- **`docs/sysml2/`** - SysML2 architecture model; authoritative source for software structure
- **`fix.ps1`** - Applies all auto-fixers silently (dotnet format, markdown, YAML). Always exits 0.
- **`build.ps1`** - Builds the solution and runs all tests.

# Standards Application (ALL Agents Must Follow)

Before performing any work, agents must read and apply the relevant standards
from `.github/standards/`. Use this matrix to determine which to load:

- **Any code**: `coding-principles.md`
- **C# code**: `coding-principles.md`, `csharp-language.md`
- **Any tests**: `testing-principles.md`
- **C# tests**: `testing-principles.md`, `csharp-testing.md`
- **Requirements**: `requirements-principles.md`, `software-items.md`, `reqstream-usage.md`
- **Design docs**: `software-items.md`, `design-documentation.md`, `technical-documentation.md`
- **Verification docs**: `software-items.md`, `verification-documentation.md`, `technical-documentation.md`
- **Review configuration**: `software-items.md`, `reviewmark-usage.md`
- **Software structure**: `sysml2-modeling.md`
- **Any documentation**: `technical-documentation.md`

Load only the standards relevant to your specific task scope.

# Agent Delegation Guidelines

The default agent should handle simple, straightforward tasks directly.
Delegate to specialized agents only for specific scenarios:

- **Pre-PR lint cleanup** (fix all lint issues before pull request) → Call the lint-fix agent
- **Scoped fixes with no new user-visible behavior** (PR review comments, doc
  corrections, known bug fixes with defined root cause) → Call the developer agent
- **Light quality checking** (basic validation) → Call the quality agent
- **Any change introducing new user-visible behavior** (features, enhancements,
  new commands or options) → Call the implementation agent
- **Formal bug resolution** (complex debugging, unknown root cause) → Call the implementation agent
- **Formal reviews** (compliance verification, detailed analysis) → Call the formal-review agent
- **Structural audit**: (repository layout vs. template) → Call the template-sync agent
- **Implementation planning only** (review a plan before committing to implementation) → Call the planning agent

# Agent Reporting (Specialized Agents Must Follow)

Specialized agents MUST generate a completion report:

1. Save to `.agent-logs/{agent-name}-{subject}-{unique-id}.md`
   where `{subject}` is a kebab-case task summary (max 5 words) and
   `{unique-id}` is a short unique suffix (e.g., 8-char hex or timestamp)
2. Start with `**Result**: (SUCCEEDED|FAILED|INCOMPLETE)` as the first metadata field
3. Include the agent-specific report sections defined in each agent's prompt
4. Return the summary to the caller

Result semantics for orchestrator decision-making:

- **SUCCEEDED**: Work completed and all quality gates applicable to that agent's scope met
- **FAILED**: Work could not be completed or quality gates not met
- **INCOMPLETE**: Work cannot proceed without information only the user can
  provide (implementation, planning, and template-sync agents)

# Formatting (After Making Changes)

After making changes, run the auto-fix pass. This applies all available fixers
silently and **always exits 0** - agents do not need to respond to its output.

```pwsh
pwsh ./fix.ps1
```

This automatically handles: `dotnet format`, markdown formatting, and YAML
formatting. Full lint compliance is a **pre-PR responsibility**, not an agent
responsibility - invoke the lint-fix agent once before submitting a pull request.

## CI Quality Tools

CI runs `lint.ps1` which checks: markdownlint-cli2, cspell, yamllint, dotnet format,
reqstream, versionmark, reviewmark, and sysml2tools.

# Scope Discipline (ALL Agents Must Follow)

- **No generated file access**: Files inside any `generated/` folder are build
  outputs - do not read, lint, or modify them
- **Minimum necessary changes**: Only modify files directly required by the task
- **No speculative refactoring**: Do not refactor code adjacent to the change
  unless the task explicitly requests it
- **No drive-by fixes**: If you discover pre-existing issues in files you are
  reading but not modifying, document them in the report but do not fix them
- **Declare scope upfront**: Before making changes, determine which files will be
  modified. Any file outside this scope requires explicit justification.

# Protected Configuration Files

These files contain carefully designed configuration with documented intent
in header comments. Agents MUST NOT modify them unless the task explicitly
requires it and the modification preserves the documented design intent:

- `.reviewmark.yaml`, `.cspell.yaml`, `.editorconfig`
- `.markdownlint-cli2.yaml`, `.yamllint.yaml`
- `requirements.yaml`, `fix.ps1`, `lint.ps1`

# Continuous Compliance Overview

This repository follows the [Continuous Compliance](https://github.com/demaconsulting/ContinuousCompliance)
approach. Tools: **ReqStream** (requirements traceability), **ReviewMark** (file review enforcement),
**BuildMark** (tool versions), **VersionMark** (version tracking).

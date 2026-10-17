# System Design

This document provides the system-level design for the core WordSense package, `DemaConsulting.WordSense`.

![WordSense Structure](WordSenseView.svg)

## Architecture

WordSense is a .NET library that decides which part of speech and, for words pronounced differently
by meaning, which pronunciation an English word carries in a given text. The core package holds everything that does
not depend on a particular model: the embedded lexicon, the per-model weights table format, the model abstraction and
the classifier. Words with one part of speech are answered from the embedded lexicon without any model. Ambiguous
words (for example "record" as noun or verb) are answered in context when the classifier has a model and the weights
trained for it, which the companion package `DemaConsulting.WordSense.MiniLM` supplies; without a model the classifier
still answers every lexicon word and reports the candidates of the ambiguous ones. The package has no runtime
dependency. The system consists of these units:

- **WordSenseModel**: the model abstraction `IWordSenseModel` (identity, embedding size, embedding of target words
  and the check that a weights table belongs to the model) and the mismatch exception
- **WordSenseTable**: the embedded lexicon table (format `WSNS`, context lemmas with their senses, word forms, the
  front-coded dictionary and the one resolver from surface word to lemma entry) and the per-model weights table
  (format `WSNW`: one set of classifier weights and abstention margins per context lemma, trained for one model and
  one lexicon)
- **SenseRecord**: immutable answer values (part of speech, pronunciation, word details, analysis with its answer
  basis and candidates), sense definition validation, the abstention rule and text segmentation
- **WordSenseClassifier**: public entry point that combines the units above in one flow
- **WordSenseBuilder**: non-shipped tool (`tools/WordSense.Builder`) that trains one weights table per model, imports
  the public-domain Moby part-of-speech list into the derived lexicon and builds the lexicon table

There are no subsystems. The classifier collaborates with the model abstraction and the table; it never sees a
tokenizer, a model file or a download. The builder reuses the table, the weights table and the feature extraction so
that training and inference share one code path, and opens the MiniLM model through the MiniLM package.

A shared package for ONNX-based models (a `WordSense.Onnx` package) is intentionally not created yet. There is one
model, so the ONNX session handling stays in the MiniLM package; the shared package is deferred until a second model
exists. The model abstraction in this package is the seam that makes the later split possible without changing the
classifier.

### Uniform data flow

There is one data model and one flow, with no separate "dictionary mode":

```text
surface word -> lemma (form map / gated morphology; one resolver) -> lemma entry
  one sense                         -> dictionary answer, no model
  several senses, model + weights   -> embed and choose: context answer
  several senses, no model          -> candidates only, no answer
  not resolved                      -> unknown
```

A lemma entry is a list of senses (part of speech, optional pronunciation). A dictionary word is an entry with one
sense. A context lemma has at least two senses, and the weights table of a model has one set of weights for it. The
entry alone decides the action, and the model runs only when at least one word of the call resolves to a context
lemma and the classifier has a model. The dictionary comes from the Moby Part-of-Speech II list (Grady Ward, public
domain), filtered and committed as `corpus/lexicon/lexicon.tsv`, and is embedded in the lexicon table. The weights
are not part of the lexicon table, so adding a model never changes the lexicon.

## External Interfaces

| Interface                          | Direction | Format                         | Constraints                       |
|------------------------------------|-----------|--------------------------------|-----------------------------------|
| `WordSenseClassifier.Create()`     | In        | Factory                        | Lexicon only; needs no model      |
| `WordSenseClassifier` constructor  | In        | Table, model, weights, options | Model and weights both or neither |
| `ClassifyAsync(words)`             | In/Out    | Words to `WordAnalysis[]`      | Parallel to input, never null     |
| `ClassifyAsync(text)`              | In/Out    | `string` to list of tokens     | Tokens tile the input             |
| `ContextLemmas`, `GetCandidates()` | Out       | Lemmas, `WordDetails` list     | Unknown lemma: empty list         |
| `IWordSenseModel`                  | In        | Interface                      | Test substitutable                |
| `WordSenseWeightsTable.Read`       | In        | Weights table bytes            | Checksum and bounds verified      |
| `WordSenseTable.LoadEmbedded`      | In        | Embedded lexicon resource      | Checksum and bounds verified      |
| `AbstainMarginScale`               | In        | Classifier option              | Not NaN, not negative             |
| `UseLexicon`                       | In        | Classifier option              | Default true                      |

### Documentation verification

The README and user-guide code samples carry markers, and the test
`WordSense_Readme_SampleMarkers_AreAllChecked` fails if any marked sample is not checked. The compile-checked
XML documentation examples are verified by `WordSense_XmlDocExamples_Compile` (linked from the quality
requirements). The appendix document
(`WordSense-Lib-Documentation`) is generated by the build from the same sources. Reproducibility of the tables
themselves (`WordSense-Lib-Reproducible`) is a property of the table builder, described in its unit design.

## Error Handling

- `WordSenseModelMismatchException`: creating a classifier throws it when the weights table was trained for another
  model (model id, model SHA-256 or embedding dimension) or for another lexicon (lexicon SHA-256, the context
  lemmas or their sense counts), so a weights table is never used with a model or lexicon it was not trained for.
- `WordSenseTableFormatException`: loading the embedded lexicon, or reading a lexicon or weights table, throws it
  when the data is missing, truncated, corrupted (checksum mismatch) or otherwise malformed.
- Invalid arguments (for example a null word list, a model without weights or weights without a model) throw
  `ArgumentException` or its derived types. Words that are not known, ambiguous words that the classifier has no
  model for and words below the abstention margin are not errors; they yield an analysis without details, and
  `Basis`, `Abstained`, `Candidates` and a zero margin tell the cases apart.

## Dependencies

The core package has no runtime dependency; it does not use ONNX Runtime. The following OTS items are used for
building and verifying this system (not consumed at runtime):

- **ApiMark**, **BuildMark**, **FileAssert**, **Pandoc**, **ReqStream**, **ReviewMark**, **SarifMark**,
  **SonarMark**, **SysML2Tools**, **VersionMark**, **WeasyPrint** and **xUnit**; see each item's
  design document.

## Risk Control Measures

N/A - WordSense is a text-analysis library with no safety-critical functionality requiring risk
control measures (IEC 62304 §5.3.3). Table integrity is protected by each table's trailing digest, and a weights
table is bound to its model and lexicon by digests.

## Data Flow

**Creation path:**

1. `WordSenseClassifier.Create()` loads the embedded lexicon table and builds a classifier without a model. The
   constructor with a model also takes the weights table.
2. When a model is given, the model checks that the weights belong to it (model id, model SHA-256, dimension) and the
   weights table checks that it belongs to the lexicon (lexicon SHA-256, context lemmas, sense counts); either check
   failing throws `WordSenseModelMismatchException`.

**Classification path:**

1. The text is split into tiles (word, whitespace run or single character).
2. Every word is resolved to a lemma entry by the table's one resolver (form map, exact lemma, possessive of a
   context lemma, gated morphology). With `UseLexicon` set to false, dictionary entries are invisible, so every
   word that is not a context lemma or one of its forms is unknown (basis `None`) and nothing is embedded unless a
   context word is present. Dictionary entries are subject to the case policy: lower case, the first
   word, a word after `. ! ? :` or an opening quote or bracket, or exactly `I`; a capitalized word elsewhere is
   treated as unknown.
3. A word whose entry has one sense is answered from the dictionary (basis `Dictionary`). A context lemma without a
   model is a word with several parts of speech that nothing can decide: it gets its candidates and no answer (basis
   `Dictionary`, `Abstained` true). An unresolved word is unknown (basis `None`). Only context words, and only when
   the classifier has a model, become targets; if there are none, the model is not called.
4. The model embeds the target words in their context and returns one vector per target (null when a target has no
   embedding).
5. For each target the vector is centered with the weights table mean and normalized; the lemma's linear model scores
   the senses. The best sense gives the part of speech and, for a heteronym, the pronunciation; the margin, the
   runner-up and its margin are reported with it (basis `Context`). A margin below the lemma's abstention threshold
   yields an analysis without details and with `Abstained` set. A context word without a vector, and a word whose
   feature vector has zero norm, yield the unknown analysis: no details, a zero margin and `Abstained` unset.

## Design Constraints

- **Determinism**: same text, model and weights give the same result; the tables are built reproducibly from the
  committed corpus and lexicon.
- **Licensing of data**: the dictionary derives from the public-domain Moby Part-of-Speech II list; the Universal
  Dependencies treebank used to evaluate it is not shipped, and no ASD-STE100 data is used or embedded.
- **Concurrency**: the classifier is thread-safe when its model is; the tables are immutable.
- **Integrity**: a weights table is used only with the model and lexicon whose digests it names.
- **Independence**: the package has no runtime dependency and works without any model file.
- **Portability**: the assembly targets net8.0, net9.0 and net10.0; it contains no native code.
- **Accuracy**: moving from one package to two does not change behavior; the holdout results stay at 96.91 % best
  sense, 97.20 % meaning and 98.73 % accuracy at 90.74 % coverage.
- **Quality**: zero warnings, requirements traceable to tests.

### Platform Support

| Target Framework | Runtime / Environment |
|------------------|-----------------------|
| `net8.0`         | .NET 8 LTS            |
| `net9.0`         | .NET 9                |
| `net10.0`        | .NET 10               |

The library is supported on Windows, Linux and macOS.

### Integration Patterns

- **NuGet Packaging**: the package `DemaConsulting.WordSense`; the model packages build on it.
- **CI/CD Integration**: automated build, test and quality validation.
- **Review Management**: file review with ReviewMark review-sets.

## WordSenseBuilder Unit Design

![WordSense Structure](WordSenseView.svg)

The builder is a unit directly under the WordSense system. It is a command-line tool in `tools/WordSense.Builder` that
is used at build time only and is never shipped in a package.

### Purpose

The builder creates the data that the shipped packages embed and measures the shipped models:

- the lexicon blob `words.bin` embedded in the core package;
- the weights blob `weights.bin` embedded in each encoder package (MiniLM, BertBaseCased);
- accuracy reports of the models and a precision report of the rules of the Basic model;
- the senses appendix of the user guide.

It reads the repository's `corpus/` folder: the sense inventory (`senses/`: every trained lemma with its senses, parts
of speech, meanings and respellings), the training sentences (`anchors/`), the holdout sentences (`holdout/`, never
used for training), the form lists (`forms/`: inflected forms of trained lemmas), the dictionary of single-sense
words and the morphology rule switches (`lexicon/`), and the real-text evaluation sentences (`realtext/`). It
references all five packages and uses internal members of them through `InternalsVisibleTo`, so that training and
inference share one implementation of the feature computation.

### Data Model

- `Program`: command-line entry point; parses `--name value` options and dispatches the commands.
- `ModelCatalog`: the models the builder knows (`minilm`, `bert-base-cased`, `basic`) with their package and model
  folder; sets the ONNX intra-op threads to 2.
- `Corpus`, `CorpusRow`: read and validate the sentence and form files; check that anchors and holdout are disjoint.
- `SenseInventory`: the authority on which senses exist (part of speech, meaning, respelling per lemma and sense).
- `SenseDefinition`, `LemmaEntry`: the validated definition of one sense and of one lemma (meanings all-or-none; a
  heteronym has at least two distinct meanings).
- `LexiconSource`: reads the dictionary word list (`word<TAB>letters`, strictly ascending, parts of speech as the
  letters `NRVJBPCI`).
- `MorphologyExpander`: generates inflected forms of dictionary words from the enabled morphology rules.
- `WordsBuilder` and `WordsBlobWriter`: combine the word sources and serialize the lexicon blob (`WSW1`).
- `WeightsTrainer`, `LogisticTrainer`, `ModelTrainer` and `WeightsBlobWriter`: train the per-lemma classifiers from
  pooled encoder vectors and serialize the weights blob (`WSX1`).
- `Evaluator`, `SetResult`, `EvaluationResult`: measure a shipped model on the holdout and the real-text sentences.
- `BasicPrecision`, `RulePrecision`: measure the precision of each Basic rule on the training anchors.
- `AppendixGenerator`: generates the senses appendix of the user guide.
- `TextSplitter`: splits raw text into words and punctuation.

Invariants: the order of the senses of a lemma is the ordinal order of the sense labels and is the order of the
weight rows of every model; equal input always gives equal output bytes (keys and lemmas are written in ordinal
order and the trainer is deterministic).

### Key Methods

#### Commands

Options are `--name value` pairs; every path defaults to the repository layout below the current directory. For
`weights` and `evaluate` the model name may be given as the second argument instead of `--model`.

- `words [--corpus DIR] [--output FILE]`: builds the lexicon and writes `words.bin` (default: the core package
  resources).
- `weights <modelId> [--corpus DIR] [--repository DIR]`: trains the weights of an encoder model (`minilm`,
  `bert-base-cased`) and writes its `weights.bin`.
- `evaluate <modelId> [--corpus DIR] [--repository DIR] [--output FILE]`: measures a model (`minilm`,
  `bert-base-cased`, `basic`) and prints a Markdown table.
- `basic-precision [--corpus DIR] [--output FILE]`: measures the precision of each Basic rule on the training anchors.
- `appendix [--corpus DIR] [--output FILE]`: writes the senses appendix of the user guide.

#### Lexicon build (`WordsBuilder.Build`)

The lexicon entries come from four sources, in decreasing priority; a source never replaces a word an earlier one
supplied:

1. The sense inventory: every trained lemma answers with all of its senses, in label order.
2. The form lists: each form of a trained lemma answers with the lemma's senses.
3. The dictionary word list: each word answers with one sense per part of speech and no meaning.
4. Generated inflections: `MorphologyExpander` adds forms of the dictionary words from the enabled rules, each
   answering with one sense of its base; the first base in ordinal order wins when two bases give the same form.

The inflections are materialized at build time, so no morphology runs at run time. `WordsBlobWriter` writes the
Brotli-compressed blob with a string table, a table of distinct `WordDetails` and the key-to-senses table.

#### Weights training (`ModelTrainer.TrainAsync`)

1. Read and validate the inventory, anchors, holdout and forms; the anchors and the holdout shall be disjoint.
2. Open the model on the verified files of `model/<model-id>/` and encode every anchor sentence once, pooling the
   hidden states of the target word (mean over its word pieces).
3. Compute the global mean of the pooled vectors, then center every vector on it and scale it to unit length with
   the same feature computation the classifier uses.
4. Per lemma, fit a binary logistic regression (two senses) or one-versus-rest (more) with a deterministic Newton
   solver. When a lemma has enough samples, choose its abstain margin by cross-validation to reach the target
   accuracy while keeping a minimum share answered; other lemmas get the median margin.
5. Write the weights blob to the package's `Resources/weights.bin`.

#### Evaluation (`Evaluator.RunAsync`)

Creates the model on the verified files, classifies the holdout and the real-text sentences, and reports per set
the rows, the share of best-sense, part-of-speech and pronunciation matches, the answered share (confidence at least
0.5) and its accuracy, plus the weights size, the latency per sentence and the first-use time and memory of the
lexicon. `basic` is evaluated the same way without weights.

#### Rule precision (`BasicPrecision.Measure`)

Runs the Basic rules over the training anchors of every ambiguous word and reports, per rule, how often it fired and
how often the part of speech it selected is correct, next to the confidence the rule currently has. The holdout is
never read, so the confidences calibrated from this report stay an honest estimate on the holdout.

#### Appendix (`AppendixGenerator.Write`)

Writes a deterministic Markdown appendix: the explanation of parts of speech, pronunciations and forms, the summary
of the dictionary words, and an alphabetical list of the trained lemmas with their senses, one example sentence per
sense and their recognized forms. Lines are wrapped to the repository's markdown line limit.

### Error Handling

The `Main` method returns 0 on success and 1 for a usage or data error. An unknown command, a missing or malformed
option, an unknown model name, an invalid or inconsistent corpus (`InvalidDataException`), a missing file or an I/O
failure is reported as a one-line message on the error stream. The corpus validation is strict: anchors and holdout
shall not overlap, every sense with sentences needs an inventory entry and every inventory entry needs a sentence,
and a lemma shall have anchor sentences for at least two senses. Nothing is written when validation fails before the
output step.

### Dependencies

- Core: `WordLexicon`, `WordDetails`, `PartOfSpeech`, `IWordSenseModel`.
- Basic: `BasicClassifier` and `Rules` (for the rule precision).
- Onnx: `OnnxWordSenseModel`, `OnnxEncoder`, the weights data types and `FeatureExtractor`.
- MiniLM and BertBaseCased: the two model classes.
- The corpus files and the model files under `model/`.

### Callers

A developer or the CI pipeline runs the builder from the command line. The `lint` script calls `appendix` to check
that the committed appendix is current. No shipped package references the builder.

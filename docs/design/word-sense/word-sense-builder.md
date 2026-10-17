## WordSenseBuilder Unit Design

![WordSense Structure](WordSenseView.svg)

The builder is a unit directly under the WordSense system. It is a command-line tool in `tools/WordSense.Builder` that
is used at build time only and is never shipped in a package.

### Purpose

The builder creates the data that the shipped packages embed and measures the shipped models:

- the lexicon blob `words.bin` embedded in the core package;
- the weights blob `weights.bin` embedded in each encoder package (MiniLM, BertBaseCased);
- the statistics table `basic.bin` embedded in the Basic package, and the treebank count files of `corpus/ud` it is
  trained from;
- accuracy reports of the models, including the calibration of the Basic model;
- the senses appendix of the user guide.

It reads the repository's `corpus/` folder: the sense inventory (`senses/`: every trained lemma with its senses, parts
of speech, meanings and respellings), the training sentences (`anchors/`), the holdout sentences (`holdout/`, never
used for training), the form lists (`forms/`: inflected forms of trained lemmas), the dictionary of all words with
their parts of speech and the morphology rule switches (`lexicon/`), the real-text evaluation sentences (`realtext/`)
and the vendored
Universal Dependencies counts (`ud/`: tag n-gram and per-word tag counts of the English EWT treebank training file). It
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
- `LexiconSource`: reads the dictionary word list (`word<TAB>letters`, ascending by word, parts of speech as the
  letters `NRVJBPCID`, the first letter being the first sense).
- `LexiconImporter`: derives the word list from the pinned Moby list (checksum, source order of the parts of speech,
  corrections, participle and gerund rule) and writes `lexicon.tsv`.
- `MorphologyExpander`: generates inflected forms of dictionary words from the enabled morphology rules.
- `WordsBuilder` and `WordsBlobWriter`: combine the word sources and serialize the lexicon blob (`WSW1`).
- `WeightsTrainer`, `LogisticTrainer`, `ModelTrainer` and `WeightsBlobWriter`: train the per-lemma classifiers from
  pooled encoder vectors and serialize the weights blob (`WSX1`).
- `Evaluator`, `SetResult`, `EvaluationResult`: measure a shipped model on the holdout and the real-text sentences.
- `UdSource`, `UdCounts`, `UdSentence`: the pinned treebank source (commit and SHA-256 checksums; download and
  verification) and the tag statistics: a CoNLL-U reader that keeps a multi-word token as one token tagged like its first
  word and skips empty nodes, the mapping of the universal tags onto the 13 tags of the tagger, the n-gram and per-word
  counts, and the deterministic count files `tag-ngrams.tsv` and `word-tags.tsv`.
- `BasicHyperparameters`: every tunable number of the Basic model in one place (the runtime values that travel in the
  table header, the training choices, and the grid and sample sizes of the tuner).
- `BasicTrainer`, `BasicModelData`: select the tabled words (closed-class words and the most frequent
  dictionary-ambiguous words), the suffixes and the closed-class masks from the counts, and train the cue tables from
  the anchors.
- `BasicTablesWriter`: serializes the model data into the `WSB1` blob, deterministically.
- `BasicTuner`: scores the hyper-parameter grid.
- `BasicModelBuilder`: the commands `ud-counts` and `train-basic`.
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
- `import-moby --input FILE [--corpus DIR]`: derives `corpus/lexicon/lexicon.tsv` from the pinned Moby Part-of-Speech
  list after verifying its SHA-256, keeping every single lower-case word with all its parts of speech in source order
  and applying `corrections.tsv` and `classes.tsv`; it prints the counts.
- `weights <modelId> [--corpus DIR] [--repository DIR]`: trains the weights of an encoder model (`minilm`,
  `bert-base-cased`) and writes its `weights.bin`.
- `evaluate <modelId> [--corpus DIR] [--repository DIR] [--output FILE]`: measures a model (`minilm`,
  `bert-base-cased`, `basic`) and prints a Markdown table.
- `ud-counts [--ud DIR] [--corpus DIR] [--output DIR]`: downloads (when missing) and verifies the pinned treebank files
  and writes the count files of `corpus/ud`.
- `train-basic [--corpus DIR] [--repository DIR] [--ud DIR] [--refresh-ud true] [--output FILE]`: retrains the whole
  Basic model with the default hyper-parameters and writes `Resources/basic.bin` of the Basic package. It regenerates
  the count files first when they are missing or `--refresh-ud true` is given.
- `tune-basic [--corpus DIR] [--repository DIR] [--ud DIR]`: scores the hyper-parameter grid and prints a table; it
  writes nothing.
- `appendix [--corpus DIR] [--output FILE]`: writes the senses appendix of the user guide.

#### Lexicon build (`WordsBuilder.Build`)

The lexicon entries come from four sources, in decreasing priority for the trained senses; the dictionary never
removes a sense:

1. The sense inventory: every trained lemma answers with all of its senses, in label order.
2. The form lists: each form of a trained lemma answers with the lemma's senses.
3. The dictionary word list: each word answers with one sense per part of speech and no meaning. When a trained
   lemma or form spells the same word, its trained senses stay first and the dictionary's other parts of speech are
   appended, so no part of speech of the dictionary is lost. Every sense of an entry has the lemma of its first sense.
4. Generated inflections: `MorphologyExpander` adds forms of the dictionary words from the enabled rules, each
   answering with one sense of its base; the first base in ordinal order wins when two bases give the same form.

Every dictionary word is kept, whatever the accuracy of any model for it: the accuracy of a model governs only which
senses it chooses between, never whether a word exists.

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
lexicon. `basic` is evaluated the same way without weights and additionally reports the calibration of its
confidence: the number and accuracy of the answers in the confidence buckets (below 0.4, 0.4 to 0.6, 0.6 to 0.7, 0.7 to
0.8, 0.8 to 0.9 and at least 0.9), the coverage and precision at the thresholds 0.6, 0.8 and 0.9, the size of the
embedded table, the cold load time, the latency per word and per sentence, and a fixed comparison table against the
reference accuracies of the other models.

#### Basic training (`BasicModelBuilder.TrainAsync`)

1. Regenerate the count files of `corpus/ud` from the verified treebank when they are missing or `--refresh-ud true` is
   given (`ud-counts` does the same on its own).
2. Read and validate the inventory, anchors, holdout and forms as for the weights; the holdout is not used further.
3. `BasicTrainer.Train` selects from the counts the closed-class words (enough occurrences and a dominant closed-class
   share), the most frequent dictionary-ambiguous words and the suffixes of one to three characters of words the lexicon
   lacks, and trains one naive Bayes cue table per lemma with at least two senses from the anchors, with the same
   feature extraction and 16-bit hash as the runtime; hash collisions keep the more frequent feature, and weights and
   priors are quantized to 1/8 nat. All numbers come from `BasicHyperparameters`.
4. `BasicTablesWriter.Write` serializes the table, `BasicTables.Load` checks that it reads back, and the file is
   written with its size and SHA-256 reported.

#### Basic tuning (`BasicTuner.RunAsync`)

Trains and scores each variant of the hyper-parameter grid (one parameter at a time around the default) on the
treebank development file and on a deterministic sample of the anchors split into a train and a dev part. It reports
the accuracy on the ambiguous treebank tokens, the best-sense accuracy on the anchor dev part and the table size. It
never reads the holdout or the real-text sentences and writes no file, so the reported evaluation stays honest.

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
- Basic: `BasicTables`, `BasicClassifier`, `CueModel` and `PosTag` (the format, the tag set, the feature extraction and
  hash, and the segmentation shared with training).
- Onnx: `OnnxWordSenseModel`, `OnnxEncoder`, the weights data types and `FeatureExtractor`.
- MiniLM and BertBaseCased: the two model classes.
- The corpus files and the model files under `model/`.

### Callers

A developer or the CI pipeline runs the builder from the command line. The `lint` script calls `appendix` to check
that the committed appendix is current. No shipped package references the builder.

## WordSenseBuilder Unit Design

![WordSense Structure](WordSenseView.svg)

The builder is a unit directly under the WordSense system. It is a command-line tool in `tools/WordSense.Builder` that
is used at build time only and is never shipped in a package.

### Purpose

The builder creates the data that the shipped packages embed and measures the shipped models:

- the flat word image `wordsense.img` embedded in the core package, compiled from the authoritative tables of `data/`
  (the words, their analyses and the sense catalog);
- the weights blob `weights.bin` embedded in each encoder package (MiniLM, BertBaseCased): the per-lemma fine-sense
  classifiers with their thresholds;
- the statistics table `basic.bin` embedded in the Basic package, and the treebank count files of `corpus/ud` it is
  trained from;
- accuracy reports of the models at each answer level, measured on the held-out sentences, on SemCor 3.0
  (senses) and on the test file of the English EWT treebank (part of speech);
- the quota report of the training sentences (tiers) and the senses appendix of the user guide.

It reads the sense catalog (the senses, their labels, groups, respellings, OEWN keys and WordNet 3.0 tag counts) from
the word image, which is compiled from `data/`, and the repository's `corpus/` folder: the training sentences
(`anchors/`, rows of a fine sense id), the holdout sentences (`holdout/`, never used for training or for thresholds),
their provenance (`provenance/`), the dictionary of all words with their parts of speech and the auxiliary list
(`lexicon/`), the reviewed suite (`suite/`, with the independent evaluation sets of a later increment) and the
vendored Universal Dependencies counts (`ud/`: tag n-gram and per-word tag counts of the English EWT treebank
training file). The former class-labeled sentences, inventory and form lists wait in `legacy/` for their relabeling and
only the Basic model commands read them. It references all five packages
and uses internal members of them through `InternalsVisibleTo`, so that training and inference share one
implementation of the feature computation.

### Data Model

- `Program`: command-line entry point; parses `--name value` options and dispatches the commands.
- `ModelCatalog`: the models the builder knows (`minilm`, `bert-base-cased`, `basic`) with their package and model
  folder; sets the ONNX intra-op threads to 2.
- `Corpus`, `CorpusRow`: read and validate the sentence and form files. A sentence row is `lemma`, `sense_id`,
  `part_of_speech` and `sentence`. The reader checks that the part of speech of a row is the one of its sense, and
  that the training sentences are disjoint from every sentence they must not resemble (see Corpus validation).
- `SenseInventory`: the authority on which senses exist, read from the sense catalog of the word image (the data of
  `data/` as compiled): for each lemma the senses in inventory order with part of speech, group, OEWN key (none for a
  project sense) and respelling; it checks the rows of the corpus against it. `LegacyCorpus` and `LegacySenseInventory`
  read the class-labeled data of `corpus/legacy` that the Basic model commands still train from.
- `SenseDefinition`, `LemmaEntry`: the validated definition of one sense and of one lemma.
- `LexiconSource`: reads the dictionary word list (`word<TAB>letters`, ascending by word, parts of speech as the
  letters `NRVJBPCID`, the first letter being the first analysis) and the auxiliary list.
- `LexiconImporter`: derives the word list from the pinned Moby list (checksum, source order of the parts of speech,
  corrections, participle and gerund rule) and writes `lexicon.tsv`.
- `IrregularForms`: reads the irregular form lists and derives the treebank list (`ud-irregular`).
- `CoverageReport`: the `coverage` command; measures how many running words of a directory of text files the word
  table knows, how many of the known words have a lemma with trained fine senses, and lists the unknown words as names
  and others.
- `ImageCompiler`: reads the tables of `data/` (entry, form, sense, sense label, sense group, group sense, respelling
  and frequency) and the lexicon lists, and writes the flat word image (`WSI1` version 3, a 112-byte header: magic,
  version, section count, a reserved word, the SHA-256 of the data tables, the SHA-256 of all bytes from offset 112 and
  the 32-byte inventory hash; then a directory of 20-byte entries and the sections). The analysis sections are the
  UTF-8-byte-sorted spelling index, the features, the entries with their lemma and part of speech, one analysis row
  (entry index and feature index) per `form` row, the lemma-to-forms index and the fold index. The
  catalog sections are the sense ids sorted by UTF-8 bytes (a sense index is its position), the OEWN key suffix of each
  sense (the part after `lemma%`, empty for a project sense), the sense-to-group index (group index plus one, 0 for a
  sense in no group), the sense-to-respelling index (respelling index plus one, 0 for none) with the distinct
  respelling strings, the group ids (UTF-8 sorted) and group parts of speech, the group members (CSR in inventory
  order), the lemma and part-of-speech blocks (lemma bytes and part of speech sorted by lemma bytes then part of
  speech, each with a CSR of sense indexes in inventory order: WordNet 3.0 tag count descending, project senses last,
  then sense id bytes) and a key index (sense indexes sorted by full key bytes). Each section is Brotli-compressed. The
  inventory hash covers, as raw decompressed bytes with each section fed as id, length and bytes, the spelling,
  analysis, feature, entry lemma and part-of-speech, sense id, sense-group, group id, group part-of-speech and
  lemma-block sections, and is what the weights and `basic.bin` must match. The catalog sections are decoded lazily by
  the reader. Equal data always gives equal decompressed sections.
- `WeightsTrainer`, `LogisticTrainer`, `ModelTrainer` and `WeightsBlobWriter`: train the per-lemma classifiers from
  pooled encoder vectors and serialize the weights blob (`WSX3`: float16 weights, the sense ids of each lemma, the
  thresholds, and the optional form chooser).
- `Thresholds`: chooses the fine threshold of a lemma, and the group threshold of a lemma that has groups, by
  cross-validation on its anchors.
- `Tiers`: the per-sense quota of anchor and held-out sentences and the report of the senses that fall short.
- `EmbeddingCache` and `CachingEncoder`: the persistent cache of pooled vectors (one file per model, keyed by the
  words and the target index, valid for one digest of the model files, per-entry checksums, atomic save) and the
  wrapper that asks the cache before the encoder; `FitCache` stores the fitted classifier of each lemma, keyed by its
  exact features and its training and threshold settings; `TrainSettings` carries the cache folder and the mean choice.
- `Evaluator`, `SetResult`, `EvaluationResult`: measure a shipped model on the holdout sentences, at
  each answer level.
- `SenseEval`: the SemCor 3.0 evaluation (download to a cache with a pinned checksum, key mapping, per-level figures and
  the most-frequent-sense baseline).
- `RunningText`: the part-of-speech evaluation on the test file of the English EWT treebank, with the convention map.
- `Suite`: reads the reviewed suite rows and the expectation of each model for each row.
- `UdSource`, `UdCounts`, `UdSentence`: the pinned treebank source (commit and SHA-256 checksums; download and
  verification) and the tag statistics: a CoNLL-U reader that keeps a multi-word token as one token tagged like its
  first
  word and skips empty nodes, the mapping of the universal tags onto the 13 tags of the tagger, the n-gram and per-word
  counts, and the deterministic count files `tag-ngrams.tsv` and `word-tags.tsv`.
- `BasicHyperparameters`: every tunable number of the Basic model in one place (the runtime values that travel in the
  table header, the training choices, and the grid and sample sizes of the tuner).
- `BasicTrainer`, `BasicModelData`: select the tabled words (closed-class words and the most frequent
  dictionary-ambiguous words), the suffixes and the closed-class masks from the counts, and train the cue tables from
  the anchors, with the analysis label of each anchor rolled up from its fine sense.
- `BasicTablesWriter`: serializes the model data into the `WSB3` blob, deterministically.
- `BasicTuner`: scores the hyper-parameter grid.
- `BasicModelBuilder`: the commands `ud-counts` and `train-basic`.
- `AppendixGenerator`: generates the senses appendix of the user guide.
- `TextSplitter`: splits raw text into words and punctuation.

Invariants: the order of the senses of a lemma is the inventory order (part of speech, tag count descending, sense id
bytes) and the weight rows of every model follow it; equal input always gives equal output bytes (keys and lemmas are
written in ordinal order and the trainer is deterministic).

### Key Methods

#### Commands

Options are `--name value` pairs; every path defaults to the repository layout below the current directory. For
`weights`, `evaluate`, `evaluate-senses` and `evaluate-running` the model name may be given as the second argument
instead of `--model`.

- `compile-image [--data DIR] [--corpus DIR] [--output FILE]`: compiles the tables of `data/` into the flat word image
  and writes `wordsense.img` (default: the core package resources); it prints the counts and the size.
- `import-moby --input FILE [--corpus DIR]`: derives `corpus/lexicon/lexicon.tsv` from the pinned Moby Part-of-Speech
  list after verifying its SHA-256, keeping every single lower-case word with all its parts of speech in source order
  and applying the committed corrections; it prints the counts.
- `weights <modelId> [--corpus DIR] [--repository DIR] [--cache DIR] [--no-cache] [--recompute-mean]`: trains the
  weights of an encoder model (`minilm`, `bert-base-cased`) and writes its `weights.bin`. `--cache DIR` places the
  embedding cache (default `<repository>/.cache/embeddings`; the cache of a model is the subfolder named by its model
  id); `--no-cache` bypasses it (and cannot be combined with `--cache`); `--recompute-mean` computes the global mean
  from the anchors instead of reusing the one of the existing weights. `--no-cache` and `--recompute-mean` are
  switches: they take no value (an explicit `true` or `false` is accepted).
- `evaluate <modelId> [--corpus DIR] [--repository DIR] [--output FILE]`: measures a model (`minilm`,
  `bert-base-cased`, `basic`) on the holdout sentences and prints a Markdown table.
- `evaluate-senses <modelId> --cache DIR [--corpus DIR] [--repository DIR] [--output FILE]`: measures a model on
  SemCor 3.0, downloaded to the cache when it is missing.
- `evaluate-running <modelId> --cache DIR [--corpus DIR] [--repository DIR] [--output FILE]`: measures the
  part-of-speech accuracy of a model on the test file of the English EWT treebank, downloaded to the cache when it is
  missing.
- `ud-counts [--ud DIR] [--corpus DIR] [--output DIR]`: downloads (when missing) and verifies the pinned treebank files
  and writes the count files of `corpus/ud`.
- `ud-irregular`: derives the treebank list of irregular forms from the committed irregular lists.
- `coverage --text DIR [--output FILE]`: reports the known, trained and unknown share of the running words of a
  directory of plain-text files.
- `train-basic [--corpus DIR] [--repository DIR] [--ud DIR] [--refresh-ud true] [--output FILE]`: retrains the whole
  Basic model with the default hyper-parameters and writes `Resources/basic.bin` of the Basic package. It regenerates
  the count files first when they are missing or `--refresh-ud true` is given.
- `tune-basic [--corpus DIR] [--repository DIR] [--ud DIR]`: scores the hyper-parameter grid and prints a table; it
  writes nothing.
- `label-senses [--data DIR]`: gives every sense without a label a generated label (the SenseLabelGenerator unit of the
  WordData subsystem).
- `import-wn30-counts --input FILE [--data DIR] [--sha256 HEX|none]`: imports the pinned WordNet 3.0 sense frequency
  counts (the OewnImporter unit of the WordData subsystem).
- `tiers [--corpus DIR] [--data DIR] [--output FILE]`: writes the quota report of the training sentences.
- `appendix [--corpus DIR] [--data DIR] [--output FILE]`: writes the senses appendix of the user guide.
- `import-oewn`, `validate-data`, `rebuild-forms`, `audit-forms` and `generate-facts`: the commands of the WordData
  subsystem (see its design).

#### Image compilation (`ImageCompiler.Build`)

1. Read the tables of `data/` and the dictionary lexicon. Every word of the dictionary is kept with all its parts of
   speech, whatever the accuracy of any model for it: the accuracy of a model governs only which senses it chooses
   between, never whether a word exists.
2. Write one analysis (lemma, part of speech, features) for every row of the `form` table. A word of the committed
   auxiliary list gains an auxiliary analysis. The forms of every dictionary word come from the `form` table, so no
   morphology runs at run time.
3. Build the sense catalog: every single-token OEWN sense and every project sense, with its readable id (`lemma#label`),
   its group (none when it is in no group), its OEWN key (none for a project sense) and its respelling. The senses of
   each lemma and part of speech
   are ranked by WordNet 3.0 tag count, then by the sense id bytes.
4. Check the invariants before writing: a sense without a label, a sense in two groups, a group of fewer than two senses
   or of all the senses of its lemma and part of speech, a group that
   mixes lemmas or parts of speech, and a group id that lacks the group marker (`lemma#group:label`) or equals a sense
   id fail the build with an error that lists every violation.
5. Write the sections, compress them, compute the hashes and write the file. The committed image is the compilation of
   the committed data (`Words_Committed_MatchesData`).

#### Corpus validation (`Corpus`, `SenseInventory`)

Every row of the anchors and the holdout sentences must have four fields, a sense id that exists in the
inventory, the lemma of that sense, and the part of speech of that sense. The training sentences must be disjoint from
the holdout, real-text and suite sentences, from the SemCor and treebank test sentences (when their cached files are
present) and from the example sentences of Open English WordNet, under two keys: the normalized sentence and every
8-gram of its words. A sentence that the writers saw as a gloss example therefore cannot train a classifier.
A lemma needs anchors for at least two of its senses to get weights; a lemma with fewer is not an error and is listed by
the tiers report.

#### Weights training (`ModelTrainer.TrainAsync`)

1. Read the anchors and the holdout and check them against the sense catalog of the word image: every row has a sense
   that exists, belongs to the lemma of the row and has the part of speech of the row (an error), and its braced target
   word is analyzed by the lexicon as that lemma and part of speech (a warning that is reported and still trains); the
   anchors and the holdout shall be disjoint (by normalized sentence). The legacy class-labeled folders are never read.
2. Open the model on the verified files of `model/<model-id>/`, and open the embedding cache (see below) unless
   `--no-cache` is given. Take the global mean from the existing weights blob of the model when it exists, reads, names
   the same model id and is not overridden by `--recompute-mean` (the *frozen mean*); the reason is printed whenever the
   mean is computed instead.
3. Get the pooled vector of every anchor sentence (the hidden states of the target word, mean over its word pieces):
   from the cache when present, otherwise from the encoder. Progress lines report the cache hits, the sentences
   encoded, the elapsed time and, at the end, the time spent in the encoder.
4. Without a frozen mean, compute the global mean of the pooled vectors. Center every vector on the mean and scale it
   to unit length with the same feature computation the classifier uses.
5. Per lemma that has two or more senses with anchors, fit a one-versus-rest L2-regularized logistic regression
   (C = 100) over its fine senses with a deterministic Newton solver. A sense with a single anchor still takes part in
   the training; a lemma with fewer than two anchored senses gets no weights. The columns are the senses in inventory
   order. The group score of a lemma that has groups is derived from the fine scores (the sum of the normalized scores of
   the senses of the group), so no group weights are stored. The solver works in the span of the anchors (the
   *kernel* form, see below) whenever the training part has fewer anchors than the encoder has dimensions, and in the
   feature space otherwise.
6. Choose the thresholds of the lemma (see Thresholds below).
7. The lemmas are fitted concurrently (`TrainingOptions.DefaultParallelism`, the processors minus two, 2 to 16
   threads), and inside a lemma the senses of a fit and the folds of the cross-validation are fitted concurrently too;
   each result is written to its own slot, so the weights do not depend on the thread count. A lemma whose
   fit is in the fit cache (see below) is not fitted again.
8. Form choosers (for a form whose analyses span lemmas, such as `left`, `bases`, `rows`) are not trained yet: the
   corpus has no rows for them. The blob format and its writer support them, and the weights carry none.
9. Check the half-precision storage: encode the held-out rows of the trained lemmas through the same cache, score them
   with the full-precision weights and with the weights read back from the written blob, print both fine top-1
   accuracies, and fail without writing when they differ by more than 0.2 percentage point. Then write the weights blob
   (`WSX3`, float16 weights) to the package's `Resources/weights.bin`. The summary of the run counts the lemmas that
   have a finite fine threshold and a finite group threshold and names the lemmas that got no weights.

**Kernel solver (`KernelBasis`, `LemmaFitter`, `LogisticTrainer`).** The weight vector of an L2-regularized logistic
regression lies in the span of its training samples, so with fewer samples n than features (a lemma has 50 to 500
anchors, the encoders 384 or 768 dimensions) the Newton iteration can run in the n coordinates of that span instead of in
the feature space, which cuts one iteration from the cube of the dimension to the cube of n. `KernelBasis` computes the
Gram matrix of the anchors once per lemma, takes the Cholesky factor of the Gram matrix of the training part (a
duplicate sentence adds no coordinate), and uses its rows as the coordinates: they have the same inner products as the
features, which is all the loss and the penalty see, so the Newton iterates are those of the feature space and the
optimum is the same; `Expand` turns the solution back into the weight vector over the features. All sums run in a
fixed scalar order (only element-wise updates are vectorized), so the result is deterministic and does not depend on
the processor or the thread count. The same Gram matrix serves the five folds and the final fit, and the senses of a
fit and the folds run concurrently. `--solver primal` selects the feature-space Newton method for every fit, which is the
reference: it reproduced the committed blobs byte for byte, and the kernel solver agrees with it to about 1e-6 in a
weight with identical answers on the held-out sentences (see `docs/build_notes/fit-speed.md`). The solver is part of the
key of the fit cache.

**Thresholds (`Thresholds.Choose`).** For every trained lemma the anchors are split into k = 5 folds by a seeded,
deterministic shuffle; each fold is scored by a fit on the other folds, which gives out-of-fold margins for every
anchor. The fine threshold `t_fine` is the lowest margin such that the answers at or above it (equal margins together)
reach the precision target on the out-of-fold anchors (95 percent by default) with at least three answers; `t_group`
is chosen in the same way on the group answers, only for a lemma that has groups and over the anchors whose best sense
is in a group and whose fine margin is below `t_fine` (the rows that fall through to the group level; every anchor
when `t_fine` is "never"), with the group margin defined as at run time (the group probability minus the largest
probability of any other group or ungrouped sense). A
level for which no threshold reaches the target gets no threshold and is never answered
for that lemma. Only the anchors are read: the holdout, suite and independent evaluation sentences never
reach the choice, so the accuracy figures of the evaluations are independent of the thresholds. The thresholds decide
the level of an answer; the confidence of an answer is the probability of the answered level.

**Tiers (`Tiers`).** The quota of a sense follows its WordNet 3.0 tag count: five or more, 20 anchor and 5 held-out
sentences; one to four, 10 and 3; none, 5 and 2. The report lists every sense of every trained lemma with its counts
and quota, the senses that fall short and the senses that are excluded (a sense that cannot be trained), each with the
reason.

**Frozen mean rule.** Features are centered on a global mean, and a recomputed mean moves whenever any anchor changes,
which would change the weights of every lemma. The builder therefore reuses the stored mean of the committed blob, so
with unchanged anchors a retraining reproduces the committed blob byte for byte (provided the encoder outputs are the
same: the committed blob must have been trained with the ONNX Runtime version now referenced, because another version
changes the low digits of the int8 encoder output), and editing the anchors of one lemma
changes only the weights of that lemma. Use `--recompute-mean` after an encoder change (a new model file) and whenever
the anchors have changed so much that the old mean no longer centers them; the whole blob then changes.

**Embedding cache (`EmbeddingCache`, `CachingEncoder`).** The encoder is deterministic for a given set of model files,
so its output is cached per model in `<cache>/<model-id>/embeddings.bin` (default cache folder
`<repository>/.cache/embeddings`, ignored by git). An entry is keyed by the SHA-256 of the exact word sequence and the
target word index, and the file is valid only for the digest of the model's ONNX file and vocabulary (their SHA-256 as
pinned in the model specification and verified when the model is opened), the casing and the version of ONNX Runtime
(a new runtime version changes the low digits of the int8 encoder output, and with them the weights): a changed model
file or runtime drops the whole cache. The vectors are stored as the exact 64-bit numbers the encoder returned, so a
cached vector is bit-identical to a fresh encode. Every entry carries a checksum; an entry that fails it, a cut-off
file,
a damaged header or an unreadable file is ignored and the sentences are encoded again, never failing the run. The file
is rewritten
atomically (a temporary file in the same folder, then renamed) at the end of the run and after every 4000 newly
encoded sentences, so an interrupted run keeps its work. The cache does not know the encoder code: after a change of
the pooling or tokenization that keeps the model files, delete the cache folder or run once with `--no-cache`.

**Fit cache (`FitCache`).** Fitting the classifiers is the longest step once the encoding is cached (seconds with the
kernel solver for the 20 trial lemmas, where the feature-space solver needed 77 seconds for MiniLM and 11.5 minutes for
the 768-dimensional BertBaseCased), and the fit of a lemma depends on that lemma alone. `<cache>/<model-id>/fits.bin`
therefore stores the fitted weights, biases and thresholds of each lemma, keyed by the SHA-256 of the training settings
(the threshold settings, the precision target, the solver and the seed included), the lemma, its sense ids, the label of
each
sentence and the exact feature vectors (which already contain the global mean). A lemma whose anchors, mean and settings
are unchanged reuses its fit bit-identically; any change of a feature number, such as a recomputed mean, another
encoder or another runtime version, or of a threshold setting, finds no entry and the lemma is fitted. With
the frozen mean, editing the anchors of one lemma therefore costs the encoding of its new sentences and the fit of that
lemma. The file holds only the fits the last run used, entries carry a checksum, the first damaged entry ends the
reading (later entries are fitted again), and the file is replaced atomically; `--no-cache` bypasses it as well.

The `evaluate` command does not use the cache: it classifies through the public `ClassifyAsync` of the shipped model,
which owns its encoder, and that guarantees that the score comes from the shipped scoring code path; caching it would
require a second scoring path.

#### Evaluation (`Evaluator.RunAsync`)

Creates the model on the verified files, classifies the sentences of `corpus/holdout` (the real-text sentences carry
the labels of the retired classes and are no longer measured), and reports per set the rows, the share of answers at
each level (sense, group, part of speech, and none for an unknown word) and the accuracy at each level (the
share of the sense-level answers with the right sense, of the group-level answers with the group of the sense of the row,
and of the part-of-speech answers with the right part of speech), the share of rows that are right at the level they
were answered, the top-1 fine accuracy (the sense with the largest product of the probability of its analysis and its
own probability; over all rows and over the rows that rank senses), the part-of-speech accuracy of the selected
analysis, plus the weights size, the latency per
sentence and the first-use time and memory of the lexicon. `basic` is evaluated the same way without weights and
additionally reports the calibration of its confidence: the number and accuracy of the answers in the confidence
buckets (below 0.4, 0.4 to 0.6, 0.6 to 0.7, 0.7 to 0.8, 0.8 to 0.9 and at least 0.9), the coverage and precision at
the thresholds 0.6, 0.8 and 0.9, the size of the embedded table, the cold load time, the latency per word and per
sentence, and a fixed comparison table against the reference accuracies of the other models.

#### Independent sense evaluation (`SenseEval.RunAsync`)

1. Download SemCor 3.0 into the cache when it is missing and verify its pinned SHA-256; a file with another checksum is
   rejected. SemCor is never committed.
2. Map the WordNet 3.0 sense keys of the annotated tokens to OEWN senses through the `sense_key` and `id_map` tables.
   A token whose key has no OEWN sense is skipped and counted.
3. Classify the sentences through the shipped model and report, for the tokens of the trained lemmas, the share
   answered at each level, the precision at each level, the part-of-speech accuracy and the accuracy of the
   most-frequent-sense baseline (the first sense of the lemma and part of speech in inventory order).

The tiers come from WordNet 3.0 tag counts that include SemCor; this gives a frequency prior and no sentence leakage,
and the evaluation report says so.

#### Running-text evaluation (`RunningText.RunAsync`)

Downloads the English EWT treebank into the cache when it is missing, verifies the pinned checksum and classifies the
sentences of the test file; the development file is the one used to select hyper-parameters and is never used here.
The universal tags are mapped to the parts of speech of the library by a published convention: a possessive pronoun
(`PRON` with `Poss=Yes`) is a determiner, and `NUM`, `PART`, `SYM` and `X` are excluded; the report lists the excluded
tokens and their number.

#### Suite (`Suite.Read`)

`corpus/suite/suite.tsv` holds the reviewed sentences, each with its expected sense, group or part of speech, and
`corpus/suite/expected.tsv` holds the expectation of each model for each row (`pass` or `known-miss`). The reader
rejects
a malformed row, a row without an expectation for a model and an expectation that names a sense or group that is not
in the inventory. The xUnit `SuiteTests` of the Basic, MiniLM and BertBaseCased test projects read the suite through
the builder's reader and fail when an expected pass regresses; the encoder tests skip when the model files are not
present (Git LFS).

#### Basic training (`BasicModelBuilder.TrainAsync`)

1. Regenerate the count files of `corpus/ud` from the verified treebank when they are missing or `--refresh-ud true` is
   given (`ud-counts` does the same on its own).
2. Read and validate the legacy inventory, anchors, holdout and forms of `corpus/legacy` as for the weights of the earlier
   release (the shipped table was trained from this class-labeled data); the holdout is not used further. The command
   moves to the fine-sense corpus when the tables are retrained for it.
3. `BasicTrainer.Train` selects from the counts the closed-class words (enough occurrences and a dominant closed-class
   share), the most frequent dictionary-ambiguous words and the suffixes of one to three characters of words the lexicon
   lacks, and trains the naive Bayes cue tables from the anchors, keyed by training lemma (a form that spans lemmas, such
   as `left` or `bases`, gets a table of its own), whose columns are analyses (lemma, part of speech and features): a
   column with empty features stands for every analysis of that lemma and part of speech, and a column with features
   (for example `read`'s `Tense=Past+VerbForm=Part` and `Tense=Pres`) matches that analysis exactly. The features are
   extracted
   with the same feature extraction and 16-bit hash as the runtime; hash collisions keep the more frequent feature, and
   weights and
   priors are quantized to 1/8 nat. The analysis label of each anchor is rolled up from its fine sense (the lemma and
   part of speech of the sense), so the anchors are labeled only once. All numbers come from `BasicHyperparameters`.
4. `BasicTablesWriter.Write` serializes the table, `BasicTables.Load` checks that it reads back, and the file is
   written with its size and SHA-256 reported. The table carries the inventory hash of the word image that it was
   trained against and is rebuilt whenever the inventory changes.

#### Basic tuning (`BasicTuner.RunAsync`)

Trains and scores each variant of the hyper-parameter grid (one parameter at a time around the default) on the
treebank development file and on a deterministic sample of the anchors split into a train and a dev part. It reports
the accuracy on the ambiguous treebank tokens, the accuracy of the analysis on the anchor dev part and the table size.
It never reads the holdout or the real-text sentences and writes no file, so the reported evaluation stays honest.

#### Appendix (`AppendixGenerator.Write`)

Writes a deterministic Markdown appendix: the explanation of parts of speech, pronunciations, groups and forms, the
summary of the dictionary words, an alphabetical list of the trained lemmas, each with its senses (id, group, OEWN sense
key, gloss and respelling), the tier and anchor counts of each sense and their recognized forms. Lines are wrapped to
the repository's
markdown line limit.

### Error Handling

The `Main` method returns 0 on success and 1 for a usage or data error. An unknown or removed command, a missing or
malformed option, an unknown model name, an invalid or inconsistent corpus or inventory (`InvalidDataException`), a
missing file, a download whose checksum differs from the pin or an I/O failure is reported as a one-line message on the
error stream. The corpus validation is strict: a malformed row, a sense id that does not exist, a part of speech that
disagrees with the sense and a sentence that is not disjoint from the sets it must not resemble are errors. The image
compilation fails on a sense without a label, a sense in two groups, a group of fewer than two senses or of all the
senses of its lemma and part of speech, a group that mixes lemmas or
parts of speech, and a group id without the group marker or equal to a sense id. Nothing is written when validation
fails before the output step.

### Dependencies

- Core: `WordLexicon`, `WordDetails`, `PartOfSpeech`, `SenseLevel`, `ClassifyResult`, `IWordSenseModel`.
- Basic: `BasicTables`, `BasicClassifier`, `CueModel` and `PosTag` (the format, the tag set, the feature extraction and
  hash, and the segmentation shared with training).
- Onnx: `OnnxWordSenseModel`, `OnnxEncoder`, the weights data types (read for the frozen mean) and `FeatureExtractor`.
- MiniLM and BertBaseCased: the two model classes.
- The WordData subsystem (`DataSchema`, `DataLayout`, `DataTables` and the validated tables of `data/`).
- The corpus files and the model files under `model/`; SemCor 3.0 and the English EWT treebank files in their caches.

### Callers

A developer or the CI pipeline runs the builder from the command line. The `lint` script calls `appendix` to check
that the committed appendix is current. No shipped package references the builder.

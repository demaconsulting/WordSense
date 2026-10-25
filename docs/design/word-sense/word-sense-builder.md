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
- the statistics table `basic.bin` embedded in the core package (for `BasicWordSenseModel`), and the tag count files of
  `corpus/open/counts` it is
  trained from;
- accuracy reports of the models at each answer level, measured on the held-out sentences, on SemCor 3.0
  (senses) and on the part-of-speech corpora (the test split of the open English corpus, with the convention-mapped
  development and test files of the English EWT treebank, which is downloaded for evaluation only);
- the quota report of the training sentences (tiers) and the senses appendix of the user guide.

It reads the sense catalog (the senses, their labels, groups and OEWN keys, in the order of Open English
WordNet) from the word image, which is compiled from `data/` (the WordNet 3.0 tag counts, for the usage prior and the
tiers, come from the `sense_frequency` table of `data/`), and the repository's `corpus/` folder: the training sentences
(`anchors/`, rows of a fine sense id), the holdout sentences (`holdout/`, never used for training or for thresholds),
the chooser rows (`choosers/anchors` and `choosers/holdout`, the same sentences labeled with the lemma or tense of
their form), their provenance (`provenance/`), the dictionary of all words with their parts of speech and the auxiliary
list
(`lexicon/`), the reviewed suite (`suite/`) and the open
English part-of-speech corpus (`open/`: the representative part, a random sample that is the only input of usage
statistics and of the real-text part-of-speech evaluation, the targeted part, whose test split is a challenge set,
and the tag n-gram and per-word tag counts of the representative training split in `open/counts`). Every sentence of
the training corpus is CC0 1.0, public domain in the United States or project-authored (and dedicated to CC0 1.0);
no treebank data and no retired class-labeled corpus is in the repository. It references all four packages
and uses internal members of them through `InternalsVisibleTo`, so that training and inference share one
implementation of the feature computation.

### Data Model

- `Program`: command-line entry point; parses `--name value` options and dispatches the commands.
- `ModelCatalog`: the models the builder knows (`minilm`, `bert-base-cased`, `basic`) with their package and model
  folder; sets the ONNX intra-op threads to 2. `EnsureAsync(model, cancellationToken)` awaits
  `EnsureModelAsync(null, cancellationToken)` only when the model is an `IDownloadableWordSenseModel` (the Basic model
  is skipped, because it needs no files).
- `Corpus`, `CorpusRow`: read and validate the sentence and form files. A sentence row is `lemma`, `sense_id`,
  `part_of_speech` and `sentence`. The reader checks that the part of speech of a row is the one of its sense, and
  that the training sentences are disjoint from every sentence they must not resemble (see Corpus validation).
- `SenseInventory`: the authority on which senses exist, read from the sense catalog of the word image (the data of
  `data/` as compiled): for each lemma the senses in inventory order with part of speech, group, OEWN key (none for a
  project sense); it checks the rows of the corpus against it.
- `AnalysisKey`: an analysis that a training row stands for (a lemma, a part of speech and the features that must be
  present); `Resolve` finds the spelling of the lemma that the word image uses.
- `LexiconSource`: reads the dictionary word list (`word<TAB>letters`, ascending by word, parts of speech as the
  letters `NRVJBPCIDA` (noun, pronoun, verb, adjective, adverb, preposition, conjunction, interjection, determiner and
  auxiliary: the letter `A`), the first letter being the first analysis) and the auxiliary list.
- `LexiconImporter`: derives the word list from the pinned Moby list (checksum, source order of the parts of speech,
  corrections, participle and gerund rule) and writes `lexicon.tsv`.
- `IrregularForms`: reads the irregular form lists.
- `CoverageReport`: the `coverage` command; measures how many running words of a directory of text files the word
  table knows, how many of the known words have a lemma with trained fine senses, and lists the unknown words as names
  and others.
- `ImageCompiler`: reads the tables of `data/` (entry, form, sense, sense label, sense group and group sense) and the
  lexicon lists, and writes the flat word image (`WSI1` version 4, a 112-byte header: magic,
  version, section count, a reserved word, the SHA-256 of the data tables, the SHA-256 of all bytes from offset 112 and
  the 32-byte inventory hash; then a directory of 20-byte entries and the sections). The analysis sections are the
  UTF-8-byte-sorted spelling index, the features, the entries with their lemma and part of speech, one analysis row
  (entry index and feature index) per `form` row, the lemma-to-forms index and the fold index. The
  catalog sections are the sense ids sorted by UTF-8 bytes (a sense index is its position), the OEWN key suffix of each
  sense (the part after `lemma%`, empty for a project sense), the sense-to-group index (group index plus one, 0 for a
  sense in no group), the group ids (UTF-8 sorted) and group parts of speech, the group members (CSR in inventory
  order), the lemma and part-of-speech blocks (lemma bytes and part of speech sorted by lemma bytes then part of
  speech, each with a CSR of sense indexes in inventory order: the order of Open English WordNet, that is the position of
  the sense in its entry, then the homograph number of the entry, with project senses last, then sense id bytes; the
  WordNet 3.0 tag counts play no part) and a key index (sense indexes sorted by full key bytes). Each section is
  Brotli-compressed. The
  inventory hash covers, as raw decompressed bytes with each section fed as id, length and bytes, the spelling,
  analysis, feature, entry lemma and part-of-speech, sense id, sense-group, group id, group part-of-speech and
  lemma-block sections, and is what the weights and `basic.bin` must match. The catalog sections are decoded lazily by
  the reader. Equal data always gives equal decompressed sections.
- `WeightsTrainer`, `LogisticTrainer`, `ModelTrainer` and `WeightsBlobWriter`: train the per-lemma classifiers and the
  form choosers from pooled encoder vectors and serialize the weights blob (`WSX3`: float16 weights, the sense ids of
  each lemma, the thresholds, and the form choosers).
- `ChooserCorpus`: reads and validates the chooser rows (`corpus/choosers/anchors` and `holdout`): rows of the form,
  the label (the lemma of the analyses it stands for, or `lemma/features` for one analysis of a lemma whose analyses of
  the form differ in tense) and the sentence with the form in braces. A label must stand for an analysis of the form
  and a sentence must occur once across both sets; it also finds the label of an analysis by the rule of the weights
  table (the exact `lemma/features` label, else the lemma).
- `Thresholds`: chooses the first fine threshold of a lemma, and the first group threshold of a lemma that has groups,
  by cross-validation on its anchors, on the scores with the usage prior applied.
- `SensePrior`, `PriorSettings`: the usage prior of the per-sense scores: reads the WordNet 3.0 tag counts from the
  `sense_frequency` table and computes the additive offset of each sense of a lemma (see "Usage prior" below).
- `RealSplit`, `RealCalibrator`: split the real-text held-out sentences by document into a calibration and a test
  split (from `corpus/provenance`, the columns `id`, `split`, `source` and `source_key`) and move the thresholds of the
  trained lemmas to the precision target measured on the real calibration sentences (see "Real-text calibration"
  below). `RealSplit` reads the provenance strictly: it refuses a file with another header, a row without its four
  non-empty columns, a split other than `anchors`, `holdout` or `realtext`, a source that is not allowed or a repeated
  id, with the file and the line. `ProvenanceSources` is the one list of the allowed sources of a sentence (real text
  and sentences written for the project): the licence gate that `CorpusLicenseTests` applies to the provenance, and
  the decision whether a source is real text.
- `Tiers`, `TierRow`: the per-sense quota of anchor and held-out sentences, the status of each sense and the report of
  the senses that fall short.
- `EmbeddingCache` and `CachingEncoder`: the persistent cache of pooled vectors (one file per model, keyed by the
  words and the target index, valid for one digest of the model files, per-entry checksums, atomic save) and the
  wrapper that asks the cache before the encoder; `FitCache` stores the fitted classifier of each lemma, keyed by its
  exact features and its training and threshold settings; `TrainSettings` carries the cache folder and the mean choice.
- `Evaluator`, `SetResult`, `EvaluationResult`: measure a shipped model on the holdout sentences, at
  each answer level.
- `ModelTable`: the model comparison table file (one row per model, one column per measurement, each written by the
  command that measures it).
- `SemCor`, `SemCorSentence`, `SemCorTarget`, `SemCorCounts`, `SenseEvaluator`, `SenseEvalResult`: the SemCor 3.0
  evaluation (reader of the tag files, download to a cache with a pinned checksum, key mapping, per-level figures and
  the first-sense baseline in Open English WordNet order, which the report calls partly circular).
- `RunningTextEvaluator`: the part-of-speech evaluation of every model on the test split of the open corpus, the
  convention-mapped treebank test file and the challenge set.
- `SuiteFile`, `SuiteRow`, `SuiteData`, `SuiteGrade`, `SuiteEvaluator`: read the reviewed suite rows and the expectation
  of each model for each row, grade an answer, and report the pass rates (the first four are compiled into the test
  projects of the models).
- `LeakageCheck`, `LeakageSet`, `LeakageReport`: the check that the parts of the corpus share no sentence.
- `OpenCorpus`, `OpenSentence`, `OpenToken`: read the open English part-of-speech corpus (`corpus/open`: header
  comments, the sentence comment lines and one seven-column token line per token) and turn a sentence into the tokens
  and tags that the tagger works with: a contraction that the corpus splits (`do` and `n't`, `it` and `'s`) is joined
  again when the joined form is in the sentence text (the possessive `'s` stays a token of its own), so that the
  counts and the evaluation present the classifier the tokens an application passes it.
- `TaggerCounts`, `TaggedSentence`: the tag statistics: the mapping of the universal tags onto the 13 tags of the
  tagger, the n-gram and per-word counts, and the deterministic count files `tag-ngrams.tsv` and `word-tags.tsv` of
  `corpus/open/counts`. They are usage statistics, so they are counted from the training split of the representative
  part only.
- `TaggerEvaluator`, `TaggerScore`: the part-of-speech accuracy of the basic classifier on gold sentences, and the
  reader of an external gold file.
- `BasicHyperparameters`: every tunable number of the Basic model in one place (the runtime values that travel in the
  table header, the training choices, and the grid and sample sizes of the tuner).
- `BasicTrainer`, `BasicModelData`: select the tabled words (closed-class words and the most frequent
  dictionary-ambiguous words), the suffixes and the closed-class masks from the counts, and train the cue tables from
  the anchors and the form-chooser anchors, with the analysis label of each sentence rolled up from its fine sense or
  chooser label.
- `BasicTablesWriter`: serializes the model data into the `WSB3` blob, deterministically.
- `BasicTuner`: scores the hyper-parameter grid.
- `BasicModelBuilder`: the commands `open-counts` and `train-basic`.
- `AppendixGenerator`: generates the senses appendix of the user guide.
- `TextSplitter`: splits raw text into words and punctuation.

Invariants: the order of the senses of a lemma is the inventory order (part of speech, Open English WordNet position,
homograph number, project senses last, sense id
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
- `weights <modelId> [--corpus DIR] [--repository DIR] [--cache DIR] [--no-cache] [--recompute-mean] [--no-prior]
  [--prior-alpha A] [--prior-smoothing K]`: trains the
  weights of an encoder model (`minilm`, `bert-base-cased`) and writes its `weights.bin`. `--cache DIR` places the
  embedding cache (default `<repository>/.cache/embeddings`; the cache of a model is the subfolder named by its model
  id); `--no-cache` bypasses it (and cannot be combined with `--cache`); `--recompute-mean` computes the global mean
  from the anchors instead of reusing the one of the existing weights. `--no-prior` trains without the usage prior;
  `--prior-alpha` (default 0.5, not negative) and `--prior-smoothing` (default 0.5, positive) set its exponent and its
  add-k constant (see Usage prior below). `--no-cache`, `--recompute-mean` and `--no-prior` are
  switches: they take no value (an explicit `true` or `false` is accepted).
- `evaluate <modelId> [--corpus DIR] [--repository DIR] [--output FILE] [--table FILE] [--outcomes FILE]`: measures a
  model (`minilm`, `bert-base-cased`, `basic`) on the holdout sentences and prints a Markdown table; `--table` writes
  the real-text sense cell and the cost cells of the model into the model comparison table file.
- `evaluate-senses [--model all|NAME] [--cache DIR] [--download] [--sample N] [--output FILE] [--table FILE]`: measures
  the models on SemCor 3.0 (read from the cache, downloaded with `--download` after verifying its pinned SHA-256).
- `evaluate-running [--model all|NAME] [--split test] [--gold FILE] [--output FILE] [--table FILE]`: measures the
  part-of-speech accuracy of the models on the representative test split of the open corpus, the convention-mapped
  treebank test file and the challenge set.
- `evaluate-suite [--output FILE] [--write-expectations FILE] [--overwrite true]`: runs the reviewed suite against the
  three models and reports the pass rates and the regressions against the expectation file.
- `check-leakage [--data DIR] [--gold FILE] [--cache DIR] [--output FILE]`: checks that no sentence is shared by a
  training part and an evaluation part, by two evaluation parts, or by the corpus and an example of Open English
  WordNet; it exits with 1 when one is.
- `open-counts [--corpus DIR] [--output DIR]`: counts the tag statistics of the training split of the representative
  open corpus (every token, whatever its consensus status) and writes the count files of `corpus/open/counts`.
- `evaluate-tagger [--input FILE] [--corpus DIR] [--repository DIR] [--split test] [--gold FILE] [--output FILE]`:
  measures the part-of-speech accuracy of the basic classifier with the table `FILE` (default the shipped `basic.bin`)
  on a split of the representative open corpus (the headline), on the test split of the targeted challenge set (its own
  line) and on an optional external gold file, and prints a Markdown table.
- `coverage --text DIR [--output FILE]`: reports the known, trained and unknown share of the running words of a
  directory of plain-text files.
- `train-basic [--corpus DIR] [--repository DIR] [--output FILE]`: retrains the whole Basic model with the default
  hyper-parameters from the committed counts of `corpus/open/counts`, the anchors and the chooser anchors, and writes
  `Resources/basic.bin` of the core package; it names `open-counts` when the counts are missing.
- `tune-basic [--corpus DIR] [--repository DIR]`: scores the hyper-parameter grid and prints a table; it writes nothing.
- `label-senses [--data DIR]`: gives every sense without a label a generated label (the SenseLabelGenerator unit of the
  WordData subsystem).
- `import-wn30-counts --input FILE [--data DIR] [--sha256 HEX|none]`: imports the pinned WordNet 3.0 sense frequency
  counts (the OewnImporter unit of the WordData subsystem).
- `tiers [--data DIR] [--output FILE]`: reports the quota of every sense of the trained lemmas (the tier of its tag
  count, its status and the sentences that are missing) and, with `--output`, writes one line for every sense.
- `appendix [--corpus DIR] [--data DIR] [--output FILE]`: writes the senses appendix of the user guide.
- `import-oewn`, `validate-data`, `rebuild-forms`, `audit-forms` and `generate-facts`: the commands of the WordData
  subsystem (see its design).

#### Image compilation (`ImageCompiler.Build`)

1. Read the tables of `data/` and the dictionary lexicon. Every word of the dictionary is kept with all its parts of
   speech, whatever the accuracy of any model for it: the accuracy of a model governs only which senses it chooses
   between, never whether a word exists.
2. Write one analysis (lemma, part of speech, features) for every row of the `form` table whose spelling has a letter.
   A spelling without a letter (a number written with digits such as `0`, `24/7` or `.22`: 134 spellings, with 176
   analyses, 176 entries and 178 senses in the data) is left out of the image, with the entries and senses of its
   lemmas, because the models never look such a token up: the filter is the rule of the runtime (`CueModel.HasLetter`)
   and is applied here, at compile time, so the committed data tables stay a faithful copy of Open English WordNet.
   A word of the committed auxiliary list gains an auxiliary analysis. The forms of every dictionary word come from the
   `form` table, so no morphology runs at run time.
3. Build the sense catalog: every single-token OEWN sense and every project sense whose lemma has a letter, with its
   readable id (`lemma#label`), its group (none when it is in no group) and its OEWN key (none for a project sense).
   The senses of each lemma and part of speech
   are ordered as Open English WordNet orders them: by the position of the sense in its entry, then the homograph number
   of the entry, the project senses last, then by the sense id bytes (the WordNet 3.0 tag counts do not order the senses;
   they only feed the usage prior of the encoder training and the tiers). The order was moved from the tag counts to
   the OEWN order because a spike that compiled the image in that order and retrained and re-evaluated all three
   models gave every figure identical to the last digit (the weights differed only in the order of their columns; the
   SemCor first-sense baseline moved from 60.68% to 60.69%); of the 1,245 blocks of two or more senses of the 826
   trained lemmas, 659 changed their order and 175 their first sense, in 126 of them because the old first sense had
   the count 0 (an alphabetical tie) and in 48 because two counts were equal. The WordNet order is made from tag counts
   that include SemCor, so the first-sense baseline of the independent evaluation stays optimistic. The counts still
   number the slugs of senses that share a label (`SenseLabelGenerator`; sense ids are frozen, so that numbering must
   not change).
4. Check the invariants before writing: a sense without a label, a sense in two groups, a group of fewer than two senses
   or of all the senses of its lemma and part of speech, a group that
   mixes lemmas or parts of speech, and a group id that lacks the group marker (`lemma#group:label`) or equals a sense
   id fail the build with an error that lists every violation.
5. Write the sections, compress them, compute the hashes and write the file. The committed image is the compilation of
   the committed data (`Words_Committed_MatchesData`).

#### Corpus validation (`Corpus`, `SenseInventory`)

Every row of the anchors and the holdout sentences must have four fields, a sense id that exists in the inventory, the
lemma of that sense, and the part of speech of that sense. The leakage check (`LeakageCheck`, the `check-leakage`
command) compares the normalized sentences (`Corpus.Normalize`: case, spacing and punctuation do not make a sentence
new) of every part of the corpus: the anchors, the chooser anchors and the training split of the open corpus
(training); the holdout, chooser holdout and real-text sentences, the reviewed suite, the development and test splits
and the challenge set of the open corpus (evaluation); and the example sentences of Open English WordNet (reference
text of another license). The SemCor sentences and the treebank test file (external evaluation sets, read from their
caches when present) are compared too, and the sense evaluation leaves out every SemCor sentence that is in the corpus
or is an example. A sentence shared by a training part and an evaluation part, by two evaluation parts (except a
chooser part and the sense part it is a view of), or by the corpus and an example sentence of five words or more fails
the check; two training parts may share a sentence (a duplicate, nothing is evaluated on it). The sentences that share
a run of eight words are counted and reported but do not fail the check, because a stock phrase can be shared by
chance. A sentence that the writers saw as a gloss example therefore cannot train a classifier. A lemma needs anchors
for at least two of its senses to get weights; a lemma with fewer is not an error and is listed by the tiers report.

#### Weights training (`ModelTrainer.TrainAsync`)

1. Read the anchors and the holdout and check them against the sense catalog of the word image: every row has a sense
   that exists, belongs to the lemma of the row and has the part of speech of the row (an error), and its braced target
   word is analyzed by the lexicon as that lemma and part of speech (a warning that is reported and still trains); the
   anchors and the holdout shall be disjoint (by normalized sentence).
2. Open the model on the verified files of `model/<model-id>/`, and open the embedding cache (see below) unless
   `--no-cache` is given. Take the global mean from the existing weights blob of the model when it exists, reads, names
   the same model id and is not overridden by `--recompute-mean` (the *frozen mean*); the reason is printed whenever the
   mean is computed instead.
3. Get the pooled vector of every anchor sentence (the hidden states of the target word, mean over its word pieces):
   from the cache when present, otherwise from the encoder. Progress lines report the cache hits, the sentences
   encoded, the elapsed time and, at the end, the time spent in the encoder.
4. Without a frozen mean, compute the global mean of the pooled vectors. Center every vector on the mean and scale it
   to unit length with the same feature computation the classifier uses.
5. Per lemma that has two or more senses with anchors, fit a one-versus-rest L2-regularized logistic regression (C =
   100) over its fine senses with a deterministic Newton solver. A sense with a single anchor still takes part in the
   training; a lemma with fewer than two anchored senses gets no weights. The columns are the senses in inventory
   order. The group score of a lemma that has groups is derived from the fine scores (the sum of the normalized scores
   of the senses of the group), so no group weights are stored. The solver works in the span of the anchors (the
   *kernel* form, see below) whenever the training part has fewer anchors than the encoder has dimensions, and in the
   feature space otherwise.
   Add the usage prior (see Usage prior below) to the fitted biases.
6. Choose the first thresholds of the lemma (see Thresholds below), on the scores with the prior; after all lemmas are
   fitted, move them to the real-text precision target (see Real-text calibration below).
7. The lemmas are fitted concurrently (`TrainingOptions.DefaultParallelism`, the processors minus two, 2 to 16
   threads), and inside a lemma the senses of a fit and the folds of the cross-validation are fitted concurrently too;
   each result is written to its own slot, so the weights do not depend on the thread count. A lemma whose
   fit is in the fit cache (see below) is not fitted again.
8. Form choosers (for a form whose analyses span lemmas, such as `left`, `bases`, `rows`, or whose verb analyses
   differ in tense, such as `read`) are trained from the chooser rows of `corpus/choosers` (see `ChooserCorpus`
   below), after the lemmas: the rows of a form are encoded through the same cache, centered on the same mean, and
   fitted by the same one-versus-rest logistic regression over the labels of the rows (a chooser has no groups, only a
   fine threshold, chosen by the same cross-validation on its rows). A form gets no chooser, and is answered from the
   lexicon alone, when its rows name fewer than two labels, when an analysis of the form has no label among them (the
   table refuses such a chooser when the weights load) or when no lemma of the form has weights; the summary lists
   those forms with the reason. A chooser is stored with the first lemma of the form, in ordinal order, that has
   weights. The held-out chooser rows are only scored: the summary prints the top-1 accuracy of the choosers, the
   share of the sentences answered at the thresholds and the precision of those answers.
9. Check the half-precision storage: encode the held-out rows of the trained lemmas through the same cache, score them
   with the full-precision weights and with the weights read back from the written blob, print both fine top-1
   accuracies, and fail without writing when they differ by more than 0.2 percentage point. Then write the weights blob
   (`WSX3`, float16 weights) to the package's `Resources/weights.bin`. The summary of the run counts the lemmas that
   have a finite fine threshold and a finite group threshold and names the lemmas that got no weights.

**Kernel solver (`KernelBasis`, `LemmaFitter`, `LogisticTrainer`).** The weight vector of an L2-regularized logistic
regression lies in the span of its training samples, so with fewer samples n than features (a lemma has 50 to 500
anchors, the encoders 384 or 768 dimensions) the Newton iteration can run in the n coordinates of that span instead of
in the feature space, which cuts one iteration from the cube of the dimension to the cube of n. `KernelBasis` computes
the Gram matrix of the anchors once per lemma, takes the Cholesky factor of the Gram matrix of the training part (a
duplicate sentence adds no coordinate), and uses its rows as the coordinates: they have the same inner products as the
features, which is all the loss and the penalty see, so the Newton iterates are those of the feature space and the
optimum is the same; `Expand` turns the solution back into the weight vector over the features. All sums run in a
fixed scalar order (only element-wise updates are vectorized), so the result is deterministic and does not depend on
the processor or the thread count. The same Gram matrix serves the five folds and the final fit, and the senses of a
fit and the folds run concurrently. `--solver primal` selects the feature-space Newton method for every fit, which is
the reference: it reproduced the committed blobs byte for byte, and the kernel solver agrees with it to about 1e-6 in
a weight with identical answers on the held-out sentences (on the first batch of 20 lemmas the fit took 11.5 minutes
in the feature space and about 9 seconds with the kernel solver and the parallel folds for BertBaseCased, with a
difference of 0.00 points on every measured column, answers identical on all 984 held-out rows and a half-precision gate
difference of 0.00; `KernelFitTests` check the basis, the weights, the biases, the thresholds and the bit-for-bit
repetition). The solver is part of
the key of the fit cache.

**Usage prior (`SensePrior`).** The classifiers are fitted on anchors that are balanced by sense quota, so the
probabilities they give carry the proportions of the training sentences, not those of real use. The correction is
`p'(s|x) ~ p(s|x) / q(s) * pi(s)^alpha`, normalized over the senses of the lemma: `q(s)` is the share of the sense
among the anchors of its lemma, and `pi(s)` is the usage share of the sense, its WordNet 3.0 tag count (the
`sense_frequency` table, joined on the sense key) plus the smoothing constant `k`, divided by the sum over the senses
of the lemma that have a column. A project sense has no sense key, so its count is 0 and it gets the constant alone. The
defaults are `alpha = 0.5` (a tempered prior: the counts are small and from a different genre) and `k = 0.5`
(`--prior-alpha`, `--prior-smoothing`; `--no-prior` trains without the correction). The scores of a lemma are
one-versus-rest log-odds whose softmax is the probability, so the correction is the additive offset
`-ln q(s) + alpha ln pi(s)` on the score of each sense, and it is stored by adding it to the bias of the sense: the
weights keep their size, the run time keeps its code and the probabilities, the margins and the thresholds are all
read from the corrected scores. The same offset is added to the out-of-fold scores of the cross-validation before the
margins are taken, so the first thresholds are chosen on the corrected scores, and the real-text calibration scores
with the stored biases, so it calibrates the corrected model. The offset is a function of the anchors and the tag
counts only, so it is deterministic and part of the fit-cache key (the offsets themselves are hashed). The blob format
has no slot for settings, so the settings are not stored in it; they are the defaults of the builder, printed in the
summary of the run and documented in the user guide chapter "Accuracy and Model Choice", which also records how the
settings were chosen: the variants without the usage term (only the training share divided out), with an independent
prior from the randomly harvested candidate sentences, and with larger exponents or smoothing were measured; the larger
exponents raise the fine top-1 on SemCor further but lower the share of sense-level answers and the part of speech on
the balanced real test sentences, so `alpha = 0.5` with `k = 0.5` is the setting that gains on SemCor and costs least.
A run takes about two minutes when the embedding cache is warm, and `--no-prior` reproduces the uncorrected weights.

**Thresholds (`Thresholds.Choose`).** For every trained lemma the anchors are split into k = 5 folds by a seeded,
deterministic shuffle; each fold is scored by a fit on the other folds, which gives out-of-fold margins for every
anchor. The fine threshold `t_fine` is the lowest margin such that the answers at or above it (equal margins together)
reach the precision target on the out-of-fold anchors (95 percent by default) with at least three answers; `t_group`
is chosen in the same way on the group answers, only for a lemma that has groups and over the anchors whose best sense
is in a group and whose fine margin is below `t_fine` (the rows that fall through to the group level; every anchor
when `t_fine` is "never"), with the group margin defined as at run time (the group probability minus the largest
probability of any other group or ungrouped sense). A level for which no threshold reaches the target gets no
threshold and is never answered for that lemma. Only the anchors are read by this step. These are the first
thresholds: the anchors are balanced and partly written, so the answers above them fall short of the target on real
text, and the real-text calibration below moves them. The thresholds decide the level of an answer; the confidence of
an answer is the probability of the answered level.

**Real-text calibration (`RealSplit`, `RealCalibrator`).** The real held-out sentences (provenance rows whose source is
real text, `ProvenanceSources.IsReal`, joined to a corpus row by the first 12 hexadecimal digits of the SHA-256 of
`lemma|sentence`) are split by *document*:
the document of a row is its source and the part of the source key before `#` (a Gutenberg book, a VOA article, a
government page; a Tatoeba sentence is its own document), and the first bit of the SHA-256 of `document|<id>` (0
calibration, 1 test) gives its side, so every sentence of a document is on one side and the split is fixed by the
data, not by a seed. The written held-out sentences and the rows without provenance are never in either split. The
calibration rows of the trained lemmas are encoded through the same cache and scored by the final full-precision
weights. For the fine level, a lemma with at least 20 calibration rows gets a threshold of its own (the lowest margin
that reaches the precision target with at least five answers); every lemma that has a finite first threshold and no
threshold of its own contributes to one pooled shift `s >= 0`. The usable answers are the calibration rows of those
lemmas whose distance `d = margin - t_cv` (the margin of the best sense minus the first threshold of its lemma) is finite
and not negative; there are `n` of them, and the answers with `d >= s` are the answers that the shift `s` keeps (answers
of equal distance are kept or dropped together). The shift is the lowest `s` whose kept answers reach the precision target
while at least `max(30, ceil(0.30 n))` answers are kept (the ceiling is taken after a tolerance of 1e-9, so an exact
product does not round up; below 100 usable answers the floor of 30 decides). With fewer than 30 usable answers the shift
is 0 and the target is reported as not reached. When no shift reaches the target on that many answers, the shift is the
largest one that keeps them (an order statistic of the distances, so it does not depend on which answers are right) and
the report says that the target was not reached. The share is what makes the shift robust: the precision of the far
tail of the distances rests on a few dozen rows, and in the measured corpus six wrong rows there moved the shift from 8.1
to 4.9, while with a share of 30 percent they move it by about 0.1 at most (bootstrap and drop-six tests on the
calibration rows only; see the user guide chapter "Accuracy and Model Choice"). The lemmas with a threshold of their own
(at least 20 rows) hold about 30 percent of the calibration rows; their threshold only raises the final one, and the
bootstrap spread of the answered share of the calibration rows is about one point with them, so they keep their simple
rule. The final fine threshold is `max(t_cv + s, t_own)`, so calibration only raises a threshold and a level that was
never answered stays so. The group threshold follows the same rule on its own answers: they are the calibration rows
that fall through the final fine threshold and whose best sense is in a group, with `d` the group margin minus the first
group threshold (a distance that is infinite, as for a lemma with no other group, or negative is no usable answer and
is not counted); it moves only when at least 30 usable group answers exist to verify it (otherwise it is kept and the
report says it is unverified), by the lowest shift that reaches the target on at least `max(30, ceil(0.30 n))` group
answers or, when none does, by the largest shift that keeps them, and the report says "target not reached" for the group
level too (after "verified"). A calibration row whose sense has no anchor counts as a wrong answer. The test split and the
written held-out sentences never reach the choice. The report of the run prints the rows used, the fine shift (with
"reaches the target" or "target not reached"), the number of lemmas with a threshold of their own, the sense answers
before and after (counted over all observed calibration rows, the lemmas with a threshold of their own included, not
only the pooled rows, so they are not the pooled answers that decided the shift) and the group shift with its state. The
calibration target is
0.97 (`--calibration-target`), two points above the 95% aimed at on the whole pipeline (and used for the cross-validation
of the first thresholds), because the precision of the
pipeline (the analysis and the form chooser) on the same sentences is about two points below that of the lemma-level
classifier (measured: 95.1% at the lemma level against 92.6% for the pipeline at the target 0.95 for MiniLM). Sweeping
the target on the real test split showed that MiniLM saturates near 94% precision, because about 7% of the gold labels
are wrong according to a blind judge, which caps the precision that can be measured. `evaluate` reports the
real test split as the headline set.

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

**Fit cache (`FitCache`).** Fitting the classifiers is the longest step once the encoding is cached (about 1.5 minutes
for the 775 lemmas of MiniLM and 2 minutes for BertBaseCased with the kernel solver; the feature-space solver needed 77
seconds for MiniLM and 11.5 minutes for the 768-dimensional BertBaseCased on a measurement of only 20 lemmas), and the
fit of a lemma depends on that lemma alone. `<cache>/<model-id>/fits.bin`
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

Creates the model on the verified files, classifies the sentences of `corpus/holdout` (the held-out sentences of the
fine senses: real text and written sentences, each with its sense id), and reports per set the rows, the share of
answers at
each level (sense, group, part of speech, and none for an unknown word) and the accuracy at each level (the share of
the sense-level answers with the right sense, of the group-level answers with the group of the sense of the row, and
of the part-of-speech answers with the right part of speech), the share of rows that are right at the level they were
answered, the top-1 fine accuracy (the sense with the largest product of the probability of its analysis and its own
probability; over all rows and over the rows that rank senses), the part-of-speech accuracy of the selected analysis,
plus the weights size, the latency per sentence and the first-use time and memory of the lexicon. When
`corpus/provenance` names the origin of the held-out sentences (the first 12 hexadecimal digits of the SHA-256 of
`lemma|sentence` join a provenance row to a row), the same classification is scored for these sets, each reported on
its own, in this order: **real text, test split** (the headline set: real held-out sentences of the test documents,
see Real-text calibration), the same sentences restricted to the documents that supply no anchor, **real text,
calibration split** (diagnostic: the thresholds were chosen on it), **LLM-written** held-out sentences (secondary:
about 17 points easier than real text) and the whole held-out set. No set is representative real text (the held-out
sentences are balanced and selected or written per sense), and the report says so; the representative measurements are
the independent evaluations.

The protocol keeps the choices apart from the measurement. The *calibration split* (the real held-out sentences of the
documents whose hash bit is 0) is used only by `RealCalibrator` when the weights are trained, to move the thresholds to
the precision target. The *test split* (the documents whose hash bit is 1) never reaches a threshold, a weight or a
choice and is the headline set that `evaluate` reports. The written held-out sentences and the rows without provenance
belong to neither split and are reported as secondary sets. The result is *published* through the model table
(`evaluate --table`): the real-text test split gives the sense share at its precision and the cost cells of the
model's row of `docs/user_guide/model-table.md`, and the README table must equal that file (a doc-test checks it); the
independent evaluations (SemCor, running text, the reviewed suite) fill their own cells. `basic` is evaluated the same
way without weights and additionally reports the
calibration of its confidence: the number and accuracy of the answers in the confidence buckets (below 0.4, 0.4 to
0.6, 0.6 to 0.7, 0.7 to 0.8, 0.8 to 0.9 and at least 0.9), the coverage and precision at the thresholds 0.6, 0.8 and
0.9, the size of the embedded table, the cold load time, the latency per word and per sentence, and a fixed comparison
table against the reference accuracies of the other models.

#### Independent sense evaluation (`SemCor`, `SenseEvaluator`)

1. Read SemCor 3.0 (`semcor3.0.tar.gz`) from the cache (`tools/open-corpus/cache`, ignored by git); with `--download`
   download it first. `SemCor.Verify` rejects an archive whose SHA-256 differs from the pin and `DownloadAsync`
   deletes a download with another hash. SemCor is never committed. Its license is the Princeton WordNet license (use,
   copy, modify and distribute for any purpose without fee, with the copyright notice and disclaimer kept on every
   copy); the notice is in `data/PROVENANCE.md`.
2. `SemCor.Parse` reads the tag files: every `<wf>` word and `<punc>` mark is a word of the sentence (a contraction
   clitic is joined to the word before it unless that word is annotated, a multiword expression is split at its
   underscores and left out as a target), and an annotated single word becomes a target whose WordNet 3.0 sense keys
   are `lemma%lexsn`. Words whose sense the mapping from WordNet 1.6 dropped (`wnsn=0`) and multiword expressions are
   counted and left out.
3. `SenseEvaluator.Select` maps a key to the sense of the catalog that has it (`WordLexicon.TryGetSenseByKey`; the
   catalog keeps the Open English WordNet keys, which are the WordNet 3.0 keys of every sense the 2025 release did not
   change), leaves out a word with several keys, keeps the words whose lemma is trained and leaves out the sentences
   that are in the corpus or are examples of Open English WordNet. The mapping coverage and the most frequent unmapped
   keys are reported.
4. `SenseEvaluator.MeasureAsync` classifies each remaining sentence once through the public interface of the model and
   judges every target like a held-out row (`Evaluator.Judge`): the answer is right at its own level; the report has the
   share answered at each level, the precision at each level, the fine top-1 (the most probable ranked sense), the group
   top-1 (the most probable ranked sense is in the group of the gold sense, for words whose gold sense is in a group),
   the part-of-speech accuracy and the first-sense baseline (the first sense of the lemma and part of speech of
   the gold sense in inventory order, the order of Open English WordNet; the baseline knows the gold lemma and part of speech).

The tiers come from WordNet 3.0 tag counts that include the counts of SemCor itself, and the inventory order (the order
of Open English WordNet) follows Princeton WordNet's frequency order: it matches the WordNet 3.0 top-count sense in 10,047
of 10,062 blocks with a unique maximum, and those counts include the tags of SemCor. The baseline is therefore partly
circular and has an advantage (it has seen the answers); the evaluation report says so. No SemCor sentence is in any
training corpus.

#### Running-text evaluation (`RunningTextEvaluator`)

`RunningTextEvaluator.Load` reads the sets: the `test` split of the representative open corpus (the headline), the gold
file of the treebank test file and the test split of the targeted part (the challenge set, on its own line). The gold
file `tools/open-corpus/work/tagger-eval/ud_test.tsv` is written by `tools/open-corpus/scripts/prepare_tagger_eval.py`,
which verifies the pinned SHA-256 of the downloaded treebank files, applies the published convention map (a possessive
pronoun is a determiner, the copula `be` is a verb, a phrasal-verb particle is an adverb, a preposition before a gerund
or noun phrase is a preposition) and writes the tag letter of every token; the development file is the one used to
select settings and is never the default here. A missing gold file is reported as a note and the set is left out.
`RunningTextEvaluator.RunAsync` scores every model through its public interface with the scoring rule of
`TaggerEvaluator` (the part of speech of the selected analysis against the gold tag; tokens that the corpus or the
convention tags as punctuation or other are excluded and counted; a token the model does not answer is counted), and
reports the accuracy on all scored tokens and on the ambiguous tokens, whose word has at least two parts of speech in
the lexicon.

#### Model table (`ModelTable`)

`docs/user_guide/model-table.md` holds the table of the README and is a chapter of the user guide: one row per model,
one column per measurement. Each
evaluation command writes its own cells (`evaluate`: the real-text sense share at its precision and the cost cells;
`evaluate-running`: the open-corpus, treebank and challenge-set cells; `evaluate-senses`: the SemCor cell) with
`--table`, and keeps the others; the README table must equal it (a doc-test checks it).

#### Suite (`SuiteFile`)

`corpus/suite/suite.tsv` holds the reviewed sentences, each with its expected analysis (lemma, part of speech and
features) and its acceptable fine senses, and `corpus/suite/expectations.tsv` holds the strongest grade each model is
expected to keep for each row (`fine`, `answer` or `miss`; see `corpus/suite/README.md`). The reader rejects a malformed
row, a row without an expectation for a model and a sense that is not in the catalog or does not belong to the expected
lemma and part of speech. `SuiteFile.Grade` judges the answer of a model at the level it answered; the grade is `fine`
when, besides, the most probable ranked sense is acceptable. `SuiteFile` has no other dependency of the builder, so the
test projects of the three models compile it (`Suite.cs` is linked into them): `BasicSuite_ExpectedPasses_StillPass`,
`MiniLMSuite_ExpectedPasses_StillPass` and `BertBaseCasedSuite_ExpectedPasses_StillPass` fail when a row falls below its
expectation; the encoder tests skip when the model files are not present (Git LFS). The expectations are raised in a
reviewed commit and never lowered to hide a regression. `evaluate-suite` reports the pass rates and writes the
expectations of a new suite or model. The challenge set has a floor test of the same kind
(`ChallengeSetFloorTests`, `corpus/suite/ste-floors.tsv`).

#### Quota report (`Tiers`)

Reads the tag counts of the senses from the `sense_frequency` table of `data/`, the anchors and the held-out
sentences, and for every sense of every lemma that has anchors computes the tier (`C`: five or more tags, 20 anchors
and 5 held-out sentences; `U`: one to four tags or a project sense, 10 and 3; `R`: none, 5 and 2), the counts and the
status (`met`, `short`, `empty`); the summary lists the totals per tier, the sentences that are missing and the senses
that fall short, most frequent first.

#### Basic training (`BasicModelBuilder.TrainAsync`)

1. Read the tag counts of `corpus/open/counts` (made by `open-counts` from the training split of the representative
   open corpus; the command stops with the name of `open-counts` when they are missing), the fine-sense anchors of
   `corpus/anchors` and the chooser anchors of `corpus/choosers/anchors`. The holdout, the test splits, the targeted
   part and the treebank are not read.
2. `BasicTrainer.Train` selects from the counts the closed-class words (enough occurrences and a dominant closed-class
   share), the most frequent dictionary-ambiguous words and the suffixes of one to three characters of words the
   lexicon lacks, and trains the naive Bayes cue tables from the anchors. A table is keyed by a lemma (the fine-sense
   anchors) or by a form that a chooser decides (the chooser anchors: `left` as a form of `leave` or of the adjective
   `left`, `read` as a past or a present verb; the classifier finds the table of the form before the table of the
   lemma). The columns are analyses (lemma, part of speech and features): a sense sentence stands for its lemma and
   part of speech with no required features, and a chooser sentence for the one analysis its label names
   (`lemma/features` names the analysis with exactly those features, a bare lemma its only part of speech; a label
   that stands for several parts of speech names no single analysis and the sentence is left out and counted). When a
   key has both kinds of sentences, a sense sentence goes to the one chooser column of its lemma and part of speech,
   is left out when the chooser columns split that slot by features, and keeps its own column when no chooser column
   has the slot. The features are extracted with the same feature extraction and 16-bit hash as the runtime; hash
   collisions keep the more frequent feature, and weights are quantized to 1/8 nat. The anchors are a balanced
   classifier corpus whose counts say nothing about how often an analysis occurs, so every column of a table gets the
   same prior and the frequencies come from the tagger counts alone. All numbers come from `BasicHyperparameters`.
3. `BasicTablesWriter.Write` serializes the table, `BasicTables.Load` checks that it reads back, and the file is
   written with its size and SHA-256 reported. The table carries the inventory hash of the word image that it was
   trained against and is rebuilt whenever the inventory changes.

#### Basic tuning (`BasicTuner.RunAsync`)

Trains and scores each variant of the hyper-parameter grid (one parameter at a time around the default, and two combined
variants) on the development split of the representative open corpus and on a deterministic sample of the anchors split
into a train and a dev part (the chooser anchors are left out of the tuned tables, because they are the same sentences
labeled a second way). It reports the accuracy on the ambiguous tokens of the development split, the accuracy of the
analysis on the anchor dev part and the table size. It never reads the test splits, the challenge set, the holdout or
the real-text sentences and writes no file, so the reported evaluation stays honest.

Workflow: change a value in `BasicHyperparameters.Default` (or add values to `Grid`) and run `tune-basic` to compare
variants; put the chosen value into `Default`, run `train-basic` (the word image must be current, `compile-image`, because
the dictionary is read from the core package), rebuild, run `evaluate basic` and compare its table with the previous
run; commit `basic.bin` and the changed hyper-parameter file. Retraining is deterministic: the same inputs give a
byte-identical table. The values that the runtime needs (gamma, beta, the three lambdas, the HMM weight and the inverse
temperature) travel in the header of `basic.bin`, so changing them needs retraining but no runtime change; a change of
the table format needs the reader in `BasicTables` and its tests to change too. Tune only against the development split
and the anchor sample; never against the holdout, the real-text sentences, the test splits of the open corpus or its
challenge set, or the treebank, which are the measurements (one real-text row is a fraction of a point, so a smaller
move than a few rows is noise). To change the training data (anchors, sense inventory or open-corpus sentences), edit it
and run `open-counts` (when the open corpus changed) and `train-basic`; `evaluate-tagger` measures the part of speech.
The grid has been tried on the open-corpus tables: the best variants (gamma 1.0, beta 1.0, closed-class 3 / 0.8) gain
about 0.5 points of ambiguous part of speech on the open development split and lose about 1.8 points on the anchor
development subset (the hard, targeted ambiguous words), so the defaults stay.

#### Tagger evaluation (`TaggerEvaluator.EvaluateAsync`)

Classifies the sentences of a split with `BasicClassifier` and the given table, the path an application uses, and
compares the part of speech of the selected analysis with the gold tag. A token is scored when the classifier answers
it (the lexicon knows the word) and its gold tag is a word class; the infinitive marker `to` counts as a preposition,
and punctuation and other gold tags are excluded and counted. The accuracy of the tokens that have at least two parts of
speech in the lexicon is reported on its own, because an unambiguous word is right by construction. The headline set is
the test split of the representative part; the targeted test split is a challenge set and is reported on its own line.
An external gold file (`sentence id`, form and tag letter per line, for example the development and test files of a
treebank mapped to the lexicon conventions by `tools/open-corpus/scripts/prepare_tagger_eval.py`, which are used for
evaluation only and never committed) adds a line.

#### Appendix (`AppendixGenerator.Write`)

Writes a deterministic Markdown appendix: the explanation of parts of speech, groups and forms, the
summary of the dictionary words, an alphabetical list of the trained lemmas, each with its senses (id, group, OEWN sense
key and gloss), the tier and anchor counts of each sense and their recognized forms (there is no pronunciation
section). Lines are wrapped to
the repository's
markdown line limit.

### Error Handling

The `Main` method returns 0 on success and 1 for a usage or data error. An unknown or removed command, a missing or
malformed option, an unknown model name, an invalid or inconsistent corpus or inventory (`InvalidDataException`), a
missing file, a download whose checksum differs from the pin, an I/O failure or a typed failure of a model
(`WordSenseException`: model unavailable, damaged or misconfigured) is reported as a one-line message on the
error stream. The corpus validation is strict: a malformed row, a sense id that does not exist, a part of speech that
disagrees with the sense and a sentence that is not disjoint from the sets it must not resemble are errors. The image
compilation fails on a sense without a label, a sense in two groups, a group of fewer than two senses or of all the
senses of its lemma and part of speech, a group that mixes lemmas or
parts of speech, and a group id without the group marker or equal to a sense id. Nothing is written when validation
fails before the output step.

### Dependencies

- Core: `WordLexicon`, `WordDetails`, `PartOfSpeech`, `SenseLevel`, `ClassifyResult`, `IWordSenseModel`,
  `IDownloadableWordSenseModel` and `WordSenseException`.
- Basic: `BasicTables`, `BasicClassifier`, `CueModel` and `PosTag` (the format, the tag set, the feature extraction and
  hash, and the segmentation shared with training).
- Onnx: `OnnxWordSenseModel`, `OnnxEncoder`, the weights data types (read for the frozen mean) and `FeatureExtractor`.
- MiniLM and BertBaseCased: the two model classes.
- The WordData subsystem (`DataSchema`, `DataLayout`, `DataTables` and the validated tables of `data/`).
- The corpus files and the model files under `model/`; SemCor 3.0 and the English EWT treebank files in their caches
  (the treebank for evaluation only).

### Callers

A developer or the CI pipeline runs the builder from the command line. The `lint` script calls `appendix` to check
that the committed appendix is current. No shipped package references the builder.

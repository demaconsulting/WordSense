# Re-keyed Model Data: Decisions and Measurements

The shipped model data is keyed by class id (`lemma#label`, the `SenseId` of the public API) instead of by lemma, part
of speech and pronunciation meaning. Each model resolves its class ids to word image indexes at load and fails loudly
when the data and the image disagree (strict load). The legacy bridge in the core package (`WordLexicon.View`,
`MeaningOf`, `TrainedLemmaOf`, `ClassesOf`) is removed.

## Accuracy

Accuracy and the per-sentence outcomes are unchanged. The evaluator was run on the previous commit (the flat image
commit) and on this one; the outcome files of all three models are byte-identical (SHA-256 equal).

| Model         | Holdout | Real text |
|---------------|---------|-----------|
| Basic         | 91.17   | 85.49     |
| MiniLM        | 96.91   | 92.60     |
| BertBaseCased | 99.05   | 97.39     |

## Formats

- Word image v2 carries a class-inventory hash (SHA-256 over the ordered class ids and their parts of speech), computed
  when the image is opened. It is not stored in the file; a corrupt class section changes it.
- `WSX2` (MiniLM, BertBaseCased weights): model id, class hash, dimension, flag, mean, then per lemma the class ids of
  its senses. A Binary entry must have exactly two classes (reader and writer).
- `WSB2` (Basic table): class hash, then the cue senses as class id, prior byte and a meaning flag byte.

## Decisions

1. **Class hash is computed at open** and not stored in the image, so the image cannot disagree with itself.
2. **Weights carry class id strings** (no parsing of `lemma#label`).
3. **The lexicon is a `Func`** in the Basic trainer and classifier, so tests do not need a dictionary.
4. **`ClassFacts` lives in the builder.** The evaluation and tuning tools take the pronunciation meaning from the sense
   inventory, because the runtime no longer carries it.
5. **The shipped blobs were converted, not retrained.** A temporary command copied the numeric payloads verbatim and
   replaced the sense keys by class ids; the command is deleted. This is why the gate is exact.
6. **Binary weights need exactly two senses** (follow-up of the first package review).
7. **The Basic cue table keeps a meaning flag per sense.** The previous matching rule let an analysis without a sense
   id (such as a form's own extra sense) use the cue column of its part of speech that had no meaning. A class with a
   plain label may still carry a meaning (`content#verb` is stress-last), so the flag is stored explicitly. This keeps
   the answers identical; it is a deliberate compatibility rule, recorded here so a later retraining can drop it.
8. **`BasicTables.Load(blob)`** is a convenience overload that resolves against the embedded image.
9. **The test model folder helpers validate every required file**, and report Git LFS pointer files as missing.
10. **Encoder tests assert nothing near ties**; they assert candidate membership and robust outcomes only.

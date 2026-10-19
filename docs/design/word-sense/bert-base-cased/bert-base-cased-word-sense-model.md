### BertBaseCasedWordSenseModel Unit Design

![BertBaseCased Structure](BertBaseCasedView.svg)

#### Purpose

`BertBaseCasedWordSenseModel` is the encoder model based on bert-base-cased, quantized to int8: the larger model,
which keeps capitalization as context.

#### Data Model

A sealed public class deriving from `OnnxWordSenseModel` without state of its own. Its model description: model id
`bert-base-cased-int8`; files `model_int8.onnx` (about 103.5 MiB; 104 MiB for all four files), `vocab.txt`,
`config.json` and `tokenizer.json`, each
with a pinned SHA-256; case-preserving tokenizer; hidden size 768; embedded weights `weights.bin` (fine-sense
classifiers, form choosers and per-lemma fine and group thresholds calibrated for this encoder) trained at sequence
length 128. The `model_int8.onnx` file is the derived encoder export, not the upstream masked language model export.

#### Key Methods

The constructor `(string? modelDirectory = null, int maxSequenceLength = 128)` passes the description to the base
class. All other behavior is inherited.

#### Error Handling

Inherited from `OnnxWordSenseModel`.

#### Dependencies

`OnnxWordSenseModel`.

#### Callers

Application code and the builder (`ModelCatalog`, `weights bert-base-cased`, `evaluate bert-base-cased`).

### MiniLMWordSenseModel Unit Design

![MiniLM Structure](MiniLMView.svg)

#### Purpose

`MiniLMWordSenseModel` is the encoder model based on all-MiniLM-L6-v2, quantized to int8: the smaller and faster of
the two encoder models.

#### Data Model

A sealed public class deriving from `OnnxWordSenseModel` without state of its own. Its model description: model id
`all-MiniLM-L6-v2-int8`; files `model_int8.onnx` (about 21.9 MiB; 22 MiB with `vocab.txt`) and `vocab.txt`, each with a
pinned SHA-256;
lower-casing (uncased) tokenizer; hidden size 384; embedded weights `weights.bin` (fine-sense classifiers, form
choosers and per-lemma fine and group thresholds calibrated for this encoder) trained at sequence length 128.

#### Key Methods

The constructor `(string? modelDirectory = null, int maxSequenceLength = 128)` passes the description to the base
class. All other behavior is inherited.

#### Error Handling

Inherited from `OnnxWordSenseModel`.

#### Dependencies

`OnnxWordSenseModel`.

#### Callers

Application code and the builder (`ModelCatalog`, `weights minilm`, `evaluate minilm`).

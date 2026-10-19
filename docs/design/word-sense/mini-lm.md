## MiniLM Subsystem Design

![MiniLM Structure](MiniLMView.svg)

### Overview

The MiniLM subsystem is the package `DemaConsulting.WordSense.MiniLM`. It adds one public class,
`MiniLMWordSenseModel`, that pins the all-MiniLM-L6-v2 int8 encoder (uncased, hidden size 384) and embeds the fine-sense
weights and per-lemma thresholds trained and calibrated for it. All behavior lives in the Onnx package; this package
only states which model it is.

The subsystem contains one unit, **MiniLMWordSenseModel**. It references the Onnx package (and, through it, the Basic
and core packages).

### Interfaces

It exposes `MiniLMWordSenseModel` with the constructor `(string? modelDirectory = null, int maxSequenceLength = 128)`,
and through its base class `EnsureModelAsync`, `ClassifyAsync`, `HiddenSize`, `ModelId` and `Dispose`. It consumes the
internal model description type of the Onnx package and the weights embedded in its own assembly.

### Design

The class holds a static model description (model id `all-MiniLM-L6-v2-int8`; files `model_int8.onnx` and
`vocab.txt` with their SHA-256 digests; ONNX file `model_int8.onnx`; vocabulary `vocab.txt`; lower-casing tokenizer;
the embedded weights resource of this assembly; hidden size 384) and passes it to the base class constructor. It adds
no members and no logic. The weights embedded in the assembly (the format `WSX3`) were trained by the builder for
exactly this model: they hold the fine-sense classifiers of the trained lemmas, the form choosers and the per-lemma fine
and group thresholds measured on the embeddings of this encoder, and they are matched against the shipped word image at
load. The model answers the ambiguous words of the trained lemmas at the sense, group or low-confidence group level
its thresholds allow, and the other ambiguous words with the Basic fallback of the base class at the part of speech
level.

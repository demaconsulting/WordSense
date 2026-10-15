# Tokenizer Parity Fixture

`generate_fixture.py` records the WordPiece ids and UTF-16 offsets that the Hugging Face `tokenizers` library produces
with `model/tokenizer.json` for the sentences in `sentences.txt` (plus every tenth seed corpus sentence). The unit tests
compare the C# `WordPieceTokenizer` with this fixture, so a tokenizer regression cannot go unnoticed.

Regenerate after changing `sentences.txt`:

```text
pip install tokenizers
python tools/tokenizer-fixture/generate_fixture.py
```

The output is `test/DemaConsulting.WordSense.Tests/Fixtures/tokenizer-parity.json`.

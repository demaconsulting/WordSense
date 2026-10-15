"""Generates the tokenizer parity fixture from the Hugging Face tokenizers library.

The fixture records, for each sentence, the WordPiece ids and the UTF-16 offsets that the reference
implementation produces with ``model/tokenizer.json``. The C# ``WordPieceTokenizer`` must reproduce
them exactly. Run from the repository root:

    python tools/tokenizer-fixture/generate_fixture.py

Requires ``pip install tokenizers``.
"""

import json
from pathlib import Path

from tokenizers import Tokenizer

ROOT = Path(__file__).resolve().parents[2]
SENTENCES = ROOT / "tools" / "tokenizer-fixture" / "sentences.txt"
CORPUS = ROOT / "corpus" / "anchors" / "seed-34.tsv"
OUTPUT = ROOT / "test" / "DemaConsulting.WordSense.Tests" / "Fixtures" / "tokenizer-parity.json"


def utf16_offsets(text: str):
    """Maps code point indexes to UTF-16 code unit indexes."""
    table = [0]
    for ch in text:
        table.append(table[-1] + (2 if ord(ch) > 0xFFFF else 1))
    return table


def corpus_sentences():
    """Every tenth corpus sentence with the target braces removed."""
    lines = CORPUS.read_text(encoding="utf-8").split("\n")
    rows = [line.split("\t")[2] for line in lines if line.count("\t") == 2]
    return [row.replace("{", "").replace("}", "") for row in rows[::10]]


def main():
    tokenizer = Tokenizer.from_file(str(ROOT / "model" / "tokenizer.json"))
    # The shipped configuration pads and truncates to 128 tokens; the fixture wants the raw tokens
    tokenizer.no_padding()
    tokenizer.no_truncation()
    texts = [line for line in SENTENCES.read_text(encoding="utf-8").split("\n") if line]
    texts += corpus_sentences()
    cases = []
    for text in texts:
        encoding = tokenizer.encode(text, add_special_tokens=False)
        table = utf16_offsets(text)
        cases.append(
            {
                "text": text,
                "ids": encoding.ids,
                "starts": [table[start] for start, _ in encoding.offsets],
                "ends": [table[end] for _, end in encoding.offsets],
            }
        )
    OUTPUT.write_text(json.dumps(cases, ensure_ascii=False, indent=1) + "\n", encoding="utf-8", newline="\n")
    print(f"Wrote {len(cases)} cases to {OUTPUT}")


if __name__ == "__main__":
    main()

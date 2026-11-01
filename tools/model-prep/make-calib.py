"""Draws the calibration sentences of the static int8 encoder (calib.tsv) from the open corpus.

The sentences are a random sample of the train split of the representative part of the open corpus (corpus/open): the
sentences are public domain or CC0 per source and the annotations are CC0. 2,000 sentences of 6 to 40 words, seed 20261031.
Nothing else is read: no development, test, targeted, held-out or gold sentence is used, and the script checks that no
drawn sentence occurs in the development or the test split.

The output has one line per sentence: the sentence id, a tab and the words joined by spaces. The script checks the SHA-256 of
the source file and of the result, so the calibration set is the same everywhere.

Usage: python make-calib.py [OUTPUT.tsv]    (default: calib.tsv beside this script)
"""

import hashlib
import os
import random
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
DATA = os.path.join(HERE, "..", "..", "corpus", "open", "representative", "data")
TRAIN_SHA256 = "f03b07619a5052756364cd93eec151603af267d336256e462795a92067060788"
CALIB_SHA256 = "9dc59cfbf6d2a683f5a774c614e3a20eadbd30b5d15b05d72e1c57cfe794da5e"
SEED = 20261031
COUNT = 2000
MIN_WORDS, MAX_WORDS = 6, 40


def sha256_file(path):
    digest = hashlib.sha256()
    with open(path, "rb") as stream:
        for block in iter(lambda: stream.read(1 << 20), b""):
            digest.update(block)
    return digest.hexdigest()


def read_sentences(path):
    """Reads (sentence id, words) pairs of a CoNLL-style file: '# sent_id = x' lines, one token per line, a blank line after a sentence."""
    sentences, current, words = [], None, []
    with open(path, encoding="utf-8") as stream:
        for line in stream:
            line = line.rstrip("\n")
            if line.startswith("# sent_id"):
                current = line.split("=", 1)[1].strip()
            elif line.startswith("#"):
                continue
            elif not line.strip():
                if words and current:
                    sentences.append((current, words))
                words = []
            else:
                words.append(line.split("\t")[1])
        if words and current:
            sentences.append((current, words))
    return sentences


def main(output):
    train = os.path.join(DATA, "train.tsv")
    if sha256_file(train) != TRAIN_SHA256:
        sys.exit("ERROR: corpus/open/representative/data/train.tsv is not the file the calibration set was drawn from")
    sentences = [s for s in read_sentences(train) if MIN_WORDS <= len(s[1]) <= MAX_WORDS]
    random.Random(SEED).shuffle(sentences)
    drawn = sentences[:COUNT]

    held_out_ids, held_out_texts = set(), set()
    for split in ("dev.tsv", "test.tsv"):
        for sentence_id, words in read_sentences(os.path.join(DATA, split)):
            held_out_ids.add(sentence_id)
            held_out_texts.add(" ".join(words))
    clash = [sid for sid, words in drawn if sid in held_out_ids or " ".join(words) in held_out_texts]
    if clash:
        sys.exit(f"ERROR: {len(clash)} calibration sentences occur in the development or test split, for example {clash[0]}")

    with open(output, "w", encoding="utf-8", newline="\n") as stream:
        for sentence_id, words in drawn:
            stream.write(f"{sentence_id}\t{' '.join(words)}\n")
    actual = sha256_file(output)
    print(f"{len(drawn)} calibration sentences, mean {sum(len(w) for _, w in drawn) / len(drawn):.1f} words; sha256 {actual}")
    if actual != CALIB_SHA256:
        sys.exit(f"ERROR: expected sha256 {CALIB_SHA256}")


if __name__ == "__main__":
    main(sys.argv[1] if len(sys.argv) > 1 else os.path.join(HERE, "calib.tsv"))

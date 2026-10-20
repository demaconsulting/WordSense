"""Loads the word data tables (data/) into python structures for the quota-fill scripts.

Standard library only. Public sense id = lemma#label (sense_label.label).
"""
import csv
import glob
import os
import sys

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
DATA = os.path.join(ROOT, "data")
CORPUS = os.path.join(ROOT, "corpus")

POS_NAMES = {"n": "Noun", "v": "Verb", "a": "Adjective", "s": "Adjective", "r": "Adverb", "u": "Pronoun",
             "p": "Preposition", "c": "Conjunction", "i": "Interjection", "d": "Determiner", "x": "Auxiliary"}


def read_table(name):
    """Yield dict rows of every file of a data table."""
    csv.field_size_limit(sys.maxsize)
    for path in sorted(glob.glob(os.path.join(DATA, name, "*.tsv"))):
        with open(path, encoding="utf-8", newline="") as handle:
            reader = csv.reader(handle, delimiter="\t", quoting=csv.QUOTE_NONE)
            header = next(reader)
            for row in reader:
                if len(row) < len(header):
                    row = row + [""] * (len(header) - len(row))
                yield dict(zip(header, row))


def load_senses():
    """Return a dict public_id -> dict(lemma, pos, pos_name, gloss, source, tag_count, synset, examples, entry_id)."""
    entries = {r["entry_id"]: r for r in read_table("entry")}
    synsets = {r["synset_id"]: r for r in read_table("synset")}
    glosses = {}
    for r in read_table("gloss"):
        glosses.setdefault(r["synset_id"], {})[int(r["seq"])] = r["text"]
    examples = {}
    for r in read_table("example"):
        examples.setdefault(r["synset_id"], []).append((int(r["seq"]), r["text"]))
    labels = {r["sense_id"]: r["label"] for r in read_table("sense_label")}
    freq = {r["sense_key"]: int(r["tag_count"]) for r in read_table("sense_frequency")}
    result = {}
    for r in read_table("sense"):
        entry = entries.get(r["entry_id"])
        if entry is None:
            continue
        label = labels.get(r["sense_id"])
        if label is None:
            continue
        syn = synsets.get(r["synset_id"], {})
        gl = glosses.get(r["synset_id"], {})
        gloss = gl[min(gl)] if gl else ""
        pos = entry["pos"]
        if pos == "s":
            pos = "a"
        pid = entry["lemma"] + "#" + label
        result[pid] = {
            "id": pid,
            "sense_id": r["sense_id"],
            "lemma": entry["lemma"],
            "pos": pos,
            "pos_name": POS_NAMES.get(pos, pos),
            "gloss": gloss,
            "source": entry["source_id"],
            "tag_count": freq.get(r["sense_key"], 0) if r["sense_key"] else 0,
            "synset": r["synset_id"],
            "token_count": int(entry["token_count"] or 1),
            "examples": [t for _, t in sorted(examples.get(r["synset_id"], []))],
            "lexfile": syn.get("lexfile", ""),
        }
    return result


if __name__ == "__main__":
    senses = load_senses()
    print(len(senses), "senses")

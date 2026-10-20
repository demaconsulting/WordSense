"""Shared state of the quota filling: senses in scope, the form index, the keys of the existing corpus, waves, paths."""
import csv
import glob
import json
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import lexdata
import lexindex
import textutil

ROOT = lexdata.ROOT
CORPUS = lexdata.CORPUS
QDIR = os.path.join(ROOT, "tools", "real-text", "cache", "quota")
WORK = os.path.join(QDIR, "work")
POS_LETTER = {"Noun": "n", "Verb": "v", "Adjective": "a", "Adverb": "r", "Pronoun": "u", "Preposition": "p",
              "Conjunction": "c", "Interjection": "i", "Determiner": "d", "Auxiliary": "x"}

# Waves by the rank of a lemma (total WordNet 3.0 tag count of its senses, ties by name).
WAVE_CUTS = [50, 200, 500, 10 ** 6]


def load_world():
    """Returns a dict with senses, scope lemma -> [sense ids], form index, entry table."""
    senses = lexdata.load_senses()
    scope = lexindex.read_scope()
    by_lemma = {}
    for sid in scope:
        by_lemma.setdefault(senses[sid]["lemma"], []).append(sid)
    index, entries = lexindex.form_index()
    return {"senses": senses, "scope": scope, "by_lemma": by_lemma, "index": index, "entries": entries}


def lemma_ranking(world):
    tags = {l: sum(world["senses"][s]["tag_count"] for s in ids) for l, ids in world["by_lemma"].items()}
    return sorted(tags, key=lambda l: (-tags[l], l)), tags


def wave_of_rank(rank0):
    for i, cut in enumerate(WAVE_CUTS):
        if rank0 < cut:
            return i + 1
    return len(WAVE_CUTS)


def corpus_files(folder):
    return sorted(glob.glob(os.path.join(CORPUS, folder, "*.tsv")))


def corpus_keys():
    """Duplicate keys of every sentence of the corpus (fine-sense rows, real text, disputed rows, choosers) and the suite."""
    keys = set()
    for folder in ("anchors", "holdout", "realtext", "disputed"):
        for path in corpus_files(folder):
            with open(path, encoding="utf-8") as handle:
                for line in handle:
                    parts = line.rstrip("\n").split("\t")
                    for cell in parts:
                        if cell.count(" ") >= 3:
                            keys.add(textutil.key_of(cell))
    for sub in ("anchors", "holdout"):
        for path in corpus_files(os.path.join("choosers", sub)):
            with open(path, encoding="utf-8") as handle:
                for line in handle:
                    parts = line.rstrip("\n").split("\t")
                    if len(parts) == 3:
                        keys.add(textutil.key_of(parts[2]))
    suite = os.path.join(QDIR, "suite.tsv")
    if os.path.exists(suite):
        with open(suite, encoding="utf-8", newline="") as handle:
            for r in csv.DictReader(handle, delimiter="\t", quoting=csv.QUOTE_NONE):
                keys.add(textutil.key_of(r["sentence"]))
    return keys


def sense_line(world, sid):
    s = world["senses"][sid]
    gloss = s["gloss"]
    if len(gloss) > 170:
        gloss = gloss[:167].rstrip() + "..."
    return {"id": sid, "pos": s["pos_name"], "gloss": gloss}


def write_json(path, obj):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    with open(path, "w", encoding="utf-8", newline="\n") as handle:
        json.dump(obj, handle, ensure_ascii=False, indent=1)


def read_json(path):
    with open(path, encoding="utf-8") as handle:
        return json.load(handle)

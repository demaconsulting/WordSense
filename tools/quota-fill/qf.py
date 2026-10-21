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
WAVE_CUTS = [50, 120, 250, 500, 10 ** 6]

EVAL_DOCUMENT_SHARE = 4  # one document in four is an evaluation document (held-out rows only)


def prov_document(sid, doc, family):
    """The document id of a pool sentence in the form the provenance rows use (RealSplit.Document)."""
    if family == "tatoeba":
        return "tatoeba-cc0:" + sid.split("-", 1)[1]
    if family == "gutenberg":
        return "gutenberg:pg" + doc.split("-", 1)[1]
    source = {"noaa-nws": "nws"}.get(family, family)
    return source + ":" + doc


def is_eval_document(prov_doc):
    """Role of a document: an evaluation document supplies held-out rows only, every other document anchors only."""
    import hashlib
    return hashlib.sha256(("evaldoc|" + prov_doc).encode("utf-8")).digest()[0] % EVAL_DOCUMENT_SHARE == 0


def real_anchor_counts():
    """sense id -> number of real anchors (provenance origin real, split anchors)."""
    result = {}
    for path in glob.glob(os.path.join(CORPUS, "provenance", "*.tsv")):
        with open(path, encoding="utf-8", newline="") as handle:
            reader = csv.reader(handle, delimiter="\t", quoting=csv.QUOTE_NONE)
            header = next(reader, None)
            if not header or "origin" not in header or "sense" not in header:
                continue
            o, se, sp = header.index("origin"), header.index("sense"), header.index("split")
            for row in reader:
                if len(row) > max(o, se, sp) and row[o] == "real" and row[sp] == "anchors":
                    result[row[se]] = result.get(row[se], 0) + 1
    return result


def extra_real_wanted():
    """sense id -> number of extra real anchors still wanted: the real target of work/extra-real.tsv (sense, target)
    minus the real anchors the sense has now."""
    path = os.path.join(WORK, "extra-real.tsv")
    wanted = {}
    if os.path.exists(path):
        have = real_anchor_counts()
        with open(path, encoding="utf-8") as handle:
            for line in handle:
                p = line.rstrip("\n").split("\t")
                if len(p) >= 2 and p[1].isdigit():
                    wanted[p[0]] = max(0, int(p[1]) - have.get(p[0], 0))
    return {k: v for k, v in wanted.items() if v > 0}


def real_documents_by_split():
    """prov document -> set of splits ('anchors', 'holdout') in which the corpus already has a real row of it."""
    result = {}
    for path in glob.glob(os.path.join(CORPUS, "provenance", "*.tsv")):
        with open(path, encoding="utf-8", newline="") as handle:
            reader = csv.reader(handle, delimiter="\t", quoting=csv.QUOTE_NONE)
            header = next(reader, None)
            if not header or "origin" not in header or "source_key" not in header:
                continue
            o, s, k, sp = (header.index(c) for c in ("origin", "source", "source_key", "split"))
            for row in reader:
                if len(row) <= max(o, s, k, sp) or row[o] != "real":
                    continue
                key = row[k]
                doc = row[s] + ":" + (key if row[s].startswith("tatoeba") else key.split("#")[0])
                result.setdefault(doc, set()).add(row[sp])
    return result


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

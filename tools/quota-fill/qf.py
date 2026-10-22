"""Shared state of the quota filling: senses in scope, the form index, the keys of the existing corpus, waves, paths."""
import csv
import glob
import hashlib
import json
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import lexdata
import lexindex
import textutil

ROOT = lexdata.ROOT
CORPUS = lexdata.CORPUS
# The git-ignored work folder: the Tatoeba CC0 export (cc0.tar.bz2) is in tools/quota-fill/cache, the pool in quota/.
QDIR = os.path.join(ROOT, "tools", "quota-fill", "cache", "quota")
WORK = os.path.join(QDIR, "work")
# The sources of sentences that were written for the project; every other allowed source is real text. Keep this equal to
# the written sources of tools/WordSense.Builder/ProvenanceSources.cs.
WRITTEN_SOURCES = {"llm-corpus", "authored"}
POS_LETTER = {"Noun": "n", "Verb": "v", "Adjective": "a", "Adverb": "r", "Pronoun": "u", "Preposition": "p",
              "Conjunction": "c", "Interjection": "i", "Determiner": "d", "Auxiliary": "x"}

# Waves by the rank of a lemma (total WordNet 3.0 tag count of its senses, ties by name). The last wave of this branch is
# wave 3 (ranks 121-200); the later cuts are for follow-up work.
WAVE_CUTS = [50, 120, 200, 500, 10 ** 6]

# The technical lemmas of wave 3 (maintenance and engineering instructions): the first group is new to the curation
# (curate_new.py) and outside the ranking, the second group is ranked but also gets technical-sense sentences.
TECH_NEW = "mount bearing bleed torque run position support install remove attach inspect clean".split()
TECH_RANKED = "arm seal check lead flush tap lock".split()
TECH_ALL = TECH_NEW + TECH_RANKED
LAST_WAVE = 3

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


def doc_role(pdoc, doc_splits):
    """The role of a document for new rows: "eval" (held-out rows only), "train" (anchors only) or None (the document
    already supplies the other kind of row, so it supplies no new rows)."""
    splits = doc_splits.get(pdoc, set())
    if is_eval_document(pdoc):
        return "eval" if "anchors" not in splits else None
    return "train" if "holdout" not in splits else None


def provenance_rows():
    """(id, split, source, source_key) of every row of corpus/provenance/*.tsv."""
    for path in sorted(glob.glob(os.path.join(CORPUS, "provenance", "*.tsv"))):
        with open(path, encoding="utf-8", newline="") as handle:
            reader = csv.reader(handle, delimiter="\t", quoting=csv.QUOTE_NONE)
            header = next(reader, None)
            if not header or any(c not in header for c in ("id", "split", "source", "source_key")):
                continue
            i, sp, s, k = (header.index(c) for c in ("id", "split", "source", "source_key"))
            for row in reader:
                if len(row) > max(i, sp, s, k):
                    yield row[i], row[sp], row[s], row[k]


def real_anchor_counts():
    """sense id -> number of real anchors: the rows of anchors/*.tsv whose provenance source is real text."""
    real = {i for i, _sp, source, _k in provenance_rows() if source not in WRITTEN_SOURCES}
    result = {}
    for path in glob.glob(os.path.join(CORPUS, "anchors", "*.tsv")):
        with open(path, encoding="utf-8", newline="") as handle:
            for line in handle:
                p = line.rstrip("\n").split("\t")
                if len(p) >= 4 and hashlib.sha256((p[0] + "|" + p[3]).encode("utf-8")).hexdigest()[:12] in real:
                    result[p[1]] = result.get(p[1], 0) + 1
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
    for _id, split, source, key in provenance_rows():
        if source in WRITTEN_SOURCES:
            continue
        doc = source + ":" + (key if source.startswith("tatoeba") else key.split("#")[0])
        result.setdefault(doc, set()).add(split)
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
    tags = {l: sum(world["senses"][s]["tag_count"] for s in ids) for l, ids in world["by_lemma"].items()
            if l not in TECH_NEW}
    return sorted(tags, key=lambda l: (-tags[l], l)), tags


def lemmas_up_to(world, wave):
    """The lemmas of the waves up to `wave` (from wave 3 on, with the technical lemmas)."""
    ranking, _tags = lemma_ranking(world)
    result = {l for i, l in enumerate(ranking) if wave_of_rank(i) <= wave}
    if wave >= LAST_WAVE:
        result |= {l for l in TECH_ALL if l in world["by_lemma"]}
    return result


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


def read_tech_targets():
    """sense id -> number of extra anchors for the technical senses (corpus/curation/tech-senses.tsv)."""
    path = os.path.join(CORPUS, "curation", "tech-senses.tsv")
    result = {}
    if os.path.exists(path):
        with open(path, encoding="utf-8", newline="") as handle:
            for row in csv.DictReader(handle, delimiter="\t", quoting=csv.QUOTE_NONE):
                result[row["sense_id"]] = int(row["extra_anchors"])
    return result


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

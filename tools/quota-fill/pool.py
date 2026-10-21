"""Builds the harvest pool: every clean sentence of the public-domain documents, deduplicated.

Inputs (git-ignored cache, tools/real-text/cache/quota/):
  text/<doc_id>.txt        extracted paragraphs of a document (Gutenberg between its START and END markers, VOA article
                           bodies, USGS, NPS and NWS prose); copied from the verified cache of the open-corpus project
  sources.tsv              one row per document with its family, title, URL, author, year and licence basis
  ../cc0.tar.bz2           the Tatoeba export of the sentences that are licensed CC0 1.0 (the CC BY sentences are never read)
Outputs:
  pool.tsv                 sid, doc, family, text   (sid = <doc>#s<ordinal>, or tatoeba-<id> for Tatoeba)
  documents.tsv            doc, family, title, author, year, url, licence basis

Usage: python pool.py build
"""
import csv
import glob
import os
import sys
import tarfile

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import textutil

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
CACHE = os.path.join(ROOT, "tools", "real-text", "cache")
QUOTA = os.path.join(CACHE, "quota")
POOL = os.path.join(QUOTA, "pool.tsv")
DOCS = os.path.join(QUOTA, "documents.tsv")
EXCLUDED_DOCS = {"gutenberg-58560"}  # an index compiled by Gutenberg volunteers, not the author's text


def read_sources():
    csv.field_size_limit(10 ** 9)
    with open(os.path.join(QUOTA, "sources.tsv"), encoding="utf-8", newline="") as handle:
        return {r["source_id"]: r for r in csv.DictReader(handle, delimiter="\t", quoting=csv.QUOTE_NONE)}


def read_excluded():
    with open(os.path.join(QUOTA, "sources_excluded.tsv"), encoding="utf-8", newline="") as handle:
        return {r["doc_id"] for r in csv.DictReader(handle, delimiter="\t", quoting=csv.QUOTE_NONE)}


def build():
    sources = read_sources()
    excluded = read_excluded() | EXCLUDED_DOCS
    seen, rows, docs = set(), [], []
    counts = {}
    for path in sorted(glob.glob(os.path.join(QUOTA, "text", "*.txt"))):
        doc = os.path.splitext(os.path.basename(path))[0]
        meta = sources.get(doc)
        if meta is None or doc in excluded or meta["family"] == "tatoeba":
            continue
        with open(path, encoding="utf-8") as handle:
            text = handle.read()
        n = 0
        for paragraph in text.split("\n\n"):
            for sent in textutil.split_sentences(paragraph.translate(textutil.LIGATURES)):
                sent = textutil.normalize_text(sent)
                n += 1
                if not textutil.acceptable(sent):
                    continue
                key = textutil.key_of(sent)
                if key in seen:
                    continue
                seen.add(key)
                rows.append((f"{doc}#s{n}", doc, meta["family"], sent))
        counts[meta["family"]] = counts.get(meta["family"], 0) + 1
        docs.append((doc, meta["family"], meta["title"], meta["author"], meta["year"], meta["url"], meta["license_basis"]))
    # Tatoeba: the CC0 export only
    tatoeba = 0
    with tarfile.open(os.path.join(CACHE, "cc0.tar.bz2"), "r:bz2") as tar:
        member = tar.getmembers()[0]
        for line in tar.extractfile(member):
            parts = line.decode("utf-8").rstrip("\r\n").split("\t")
            if len(parts) < 3 or parts[1] != "eng":
                continue
            sent = textutil.normalize_text(parts[2])
            if not textutil.acceptable(sent):
                continue
            key = textutil.key_of(sent)
            if key in seen:
                continue
            seen.add(key)
            rows.append((f"tatoeba-{parts[0]}", "tatoeba-cc0", "tatoeba", sent))
            tatoeba += 1
    docs.append(("tatoeba-cc0", "tatoeba", "Tatoeba sentences (CC0 1.0 export)", "Tatoeba contributors", "",
                 "https://tatoeba.org/en/downloads", "Tatoeba sentences licensed CC0 1.0 (the official sentences_CC0 export)"))
    with open(POOL, "w", encoding="utf-8", newline="\n") as handle:
        for r in rows:
            handle.write("\t".join(r) + "\n")
    with open(DOCS, "w", encoding="utf-8", newline="\n") as handle:
        handle.write("doc\tfamily\ttitle\tauthor\tyear\turl\tlicense_basis\n")
        for d in docs:
            handle.write("\t".join(x.replace("\t", " ") for x in d) + "\n")
    by_family = {}
    for r in rows:
        by_family[r[2]] = by_family.get(r[2], 0) + 1
    print(len(rows), "sentences;", by_family, "documents:", counts)


def load_pool():
    """Returns (list of (sid, doc, family, text), documents dict)."""
    rows = []
    with open(POOL, encoding="utf-8") as handle:
        for line in handle:
            rows.append(tuple(line.rstrip("\n").split("\t")))
    docs = {}
    with open(DOCS, encoding="utf-8", newline="") as handle:
        for r in csv.DictReader(handle, delimiter="\t", quoting=csv.QUOTE_NONE):
            docs[r["doc"]] = r
    return rows, docs


if __name__ == "__main__":
    if sys.argv[1:] == ["build"]:
        build()
    else:
        print(__doc__)

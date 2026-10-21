#!/usr/bin/env python3
"""Keeps the training corpus CC0-clean: drops every sentence row whose licence is not CC0 1.0, public domain in the United
States or project-authored (and therefore dedicated to CC0 1.0 by the project owner).

  python license_filter.py [CORPUS_DIR]       default: corpus (run from the repository root)

What it does (idempotent; the counts are printed and recorded in corpus/README.md):

1. Reads every provenance row (corpus/provenance/*.tsv). A row is allowed when its licence starts with `CC0 1.0`,
   `Public domain` or `MIT (project-authored)` / `CC0 1.0 (project-authored)`; anything else (for example the Tatoeba sentences
   under `CC BY 2.0 FR`, and any CC BY-SA text) is banned.
2. Drops the banned rows from the sentence files of anchors/, holdout/, realtext/ and from the chooser files of
   choosers/anchors and choosers/holdout (a chooser row is a second view of a sentence of the fine-sense corpus, so it goes
   with the sentence, found by its provenance id or by the text of a banned sentence), and from the provenance files.
3. Rewrites the recorded licence of the project-authored rows to `CC0 1.0 (project-authored)` (the project owner dedicates
   them to the public domain under CC0 1.0) and spells the public-domain licence `Public domain in the United States`.

The provenance id of a row is the first 12 hexadecimal digits of SHA-256 of `lemma|sentence` (`form|sentence` or
`label|sentence` for a chooser row).
"""
import collections
import csv
import glob
import hashlib
import os
import re
import sys

ALLOWED_PREFIXES = ("CC0 1.0", "Public domain", "MIT (project-authored)")
SENSE_FOLDERS = ["anchors", "holdout", "realtext"]
CHOOSER_FOLDERS = ["choosers/anchors", "choosers/holdout"]


def row_id(text):
    return hashlib.sha256(text.encode("utf-8")).hexdigest()[:12]


def normalize(sentence):
    return re.sub(r"[^a-z0-9]+", " ", sentence.replace("{", "").replace("}", "").lower()).strip()


def rewrite_licence(text):
    return (text.replace("MIT (project-authored)", "CC0 1.0 (project-authored)")
            .replace("project-authored (MIT)", "project-authored (CC0 1.0)"))


def read_lines(path):
    with open(path, encoding="utf-8", newline="") as handle:
        return handle.read().split("\n")


def write_lines(path, lines):
    with open(path, "w", encoding="utf-8", newline="\n") as handle:
        handle.write("\n".join(lines))


def main():
    corpus = sys.argv[1] if len(sys.argv) > 1 else "corpus"
    banned = {}
    licences = collections.Counter()
    for path in sorted(glob.glob(os.path.join(corpus, "provenance", "*.tsv"))):
        with open(path, encoding="utf-8", newline="") as handle:
            for r in csv.DictReader(handle, delimiter="\t", quoting=csv.QUOTE_NONE):
                licences[r["licence"]] += 1
                if not r["licence"].startswith(ALLOWED_PREFIXES):
                    banned[r["id"]] = r["licence"]
    print("licences:", dict(licences))
    print("banned provenance ids:", len(banned), dict(collections.Counter(banned.values())))

    # Sense rows first: they give the text of the banned sentences
    banned_text = set()
    dropped = collections.Counter()
    for folder in SENSE_FOLDERS:
        for path in sorted(glob.glob(os.path.join(corpus, folder, "*.tsv"))):
            kept, drop = [], 0
            for line in read_lines(path):
                if not line:
                    kept.append(line)
                    continue
                c = line.split("\t")
                if row_id(c[0] + "|" + c[3]) in banned:
                    banned_text.add(normalize(c[3]))
                    drop += 1
                else:
                    kept.append(line)
            if drop:
                write_lines(path, kept)
                dropped[folder] += drop
    for folder in CHOOSER_FOLDERS:
        for path in sorted(glob.glob(os.path.join(corpus, folder, "*.tsv"))):
            kept, drop = [], 0
            for line in read_lines(path):
                if not line:
                    kept.append(line)
                    continue
                c = line.split("\t")
                if (row_id(c[0] + "|" + c[2]) in banned or row_id(c[1] + "|" + c[2]) in banned
                        or normalize(c[2]) in banned_text):
                    drop += 1
                else:
                    kept.append(line)
            if drop:
                write_lines(path, kept)
                dropped[folder] += drop
    print("dropped sentence rows:", dict(dropped), "total", sum(dropped.values()))

    # Provenance: drop the banned rows, rewrite the licences
    for path in sorted(glob.glob(os.path.join(corpus, "provenance", "*.tsv"))):
        lines = read_lines(path)
        out = [lines[0]]
        for line in lines[1:]:
            if not line:
                out.append(line)
                continue
            c = line.split("\t")
            if c[0] in banned:
                continue
            out.append(rewrite_licence(line))
        out = [re.sub(r"\tPublic domain\t", "\tPublic domain in the United States\t", l) for l in out]
        write_lines(path, out)
    print("provenance rewritten")


if __name__ == "__main__":
    main()

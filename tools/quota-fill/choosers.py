"""Chooser status: the forms whose analyses are not all labeled by the chooser rows.

  python choosers.py status        -> counts and the list of (form, missing lemmas) written to work/chooser-missing.tsv

A chooser is trained for a form when its rows name at least two labels and every analysis of the form has a label (the
lemma, or lemma/features for a lemma whose analyses of the form differ in features). This script works at the level of
the lemma: the forms whose analyses stand for two or more lemmas and the lemmas that no row of the form labels.
"""
import collections
import glob
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import lexdata
import qf


def form_lemmas():
    entries = {r["entry_id"]: r["lemma"] for r in lexdata.read_table("entry")}
    result = collections.defaultdict(lambda: collections.defaultdict(set))
    for r in lexdata.read_table("form"):
        lemma = entries.get(r["entry_id"])
        if lemma:
            result[r["spelling"].lower()][lemma].add(r["features"])
    return result


def tense_split_forms():
    """Forms where one entry has two analyses with different features (read, cast, ...) or that have slash labels."""
    split = set()
    per_entry = collections.defaultdict(set)
    for r in lexdata.read_table("form"):
        per_entry[(r["spelling"].lower(), r["entry_id"])].add(r["features"])
    for (form, _entry), feats in per_entry.items():
        if len(feats) > 1 and any("Tense=" in f for f in feats):
            split.add(form)
    for form, per in chooser_labels().items():
        if any("/" in l for (_s, l) in per):
            split.add(form)
    return split


def chooser_labels():
    labels = collections.defaultdict(collections.Counter)
    for sub in ("anchors", "holdout"):
        for path in glob.glob(os.path.join(lexdata.CORPUS, "choosers", sub, "*.tsv")):
            with open(path, encoding="utf-8") as handle:
                for line in handle:
                    parts = line.rstrip("\n").split("\t")
                    if len(parts) == 3:
                        labels[parts[0]][(sub, parts[1])] += 1
    return labels


def status():
    fl = form_lemmas()
    labels = chooser_labels()
    missing = {}
    complete, tense_forms = 0, 0
    for form, per in labels.items():
        have = {l.split("/")[0] for (_s, l) in per}
        have_anchor = {l.split("/")[0] for (s, l) in per if s == "anchors"}
        lemmas = set(fl.get(form, {}))
        if any("/" in l for (_s, l) in per):
            tense_forms += 1
            continue
        gap = lemmas - have_anchor
        if gap:
            missing[form] = sorted(gap)
        else:
            complete += 1
    return fl, labels, missing, complete, tense_forms


if __name__ == "__main__":
    fl, labels, missing, complete, tense_forms = status()
    print("forms with chooser rows:", len(labels), "complete:", complete, "tense forms (skipped here):", tense_forms,
          "missing a lemma:", len(missing))
    os.makedirs(qf.WORK, exist_ok=True)
    with open(os.path.join(qf.WORK, "chooser-missing.tsv"), "w", encoding="utf-8", newline="\n") as handle:
        for form in sorted(missing):
            handle.write(form + "\t" + ",".join(missing[form]) + "\n")
    print(list(missing.items())[:15])

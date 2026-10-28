"""Lexicon index and quota status for the quota filling.

form_index(): lower-case form -> set of (lemma, pos letter) analyses, from data/form and data/entry.
quota_status(): per sense of the lemmas in scope: tier, tag count, anchors, holdout and the deficits. The quota follows the
tier (C 20 + 5, U 10 + 3, R 5 + 2); an R sense of a lemma with 15 or more senses needs 3 + 1.
"""
import csv
import glob
import os
import re
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import lexdata

QUOTA = {"C": (20, 5), "U": (10, 3), "R": (5, 2)}
# A sense without a tag count (tier R) of a lemma with this many senses or more needs fewer rows: such a sense carries
# no usage, and the very polysemous lemmas have many of them.
LARGE_LEMMA_SENSES = 15
QUOTA_R_LARGE = (3, 1)


def quota_of(tier, lemma_senses):
    """(anchors, held-out rows) a sense of this tier needs; `lemma_senses` is the number of senses of its lemma."""
    if tier == "R" and lemma_senses >= LARGE_LEMMA_SENSES:
        return QUOTA_R_LARGE
    return QUOTA[tier]


def tier_of(sense):
    if sense["source"] == "dema":
        return "U"
    t = sense["tag_count"]
    return "C" if t >= 5 else ("U" if t >= 1 else "R")


def form_index():
    """Returns (index, entries): index[lower form] = set of (lemma, pos)."""
    entries = {r["entry_id"]: (r["lemma"], "a" if r["pos"] == "s" else r["pos"]) for r in lexdata.read_table("entry")}
    index = {}
    for r in lexdata.read_table("form"):
        lemma_pos = entries.get(r["entry_id"])
        if lemma_pos is None:
            continue
        index.setdefault(r["spelling"].lower(), set()).add(lemma_pos)
    return index, entries


def corpus_rows(folder):
    """Yield (file, lemma, sense, pos, sentence) of every fine-sense file in corpus/<folder>."""
    for path in sorted(glob.glob(os.path.join(lexdata.CORPUS, folder, "*.tsv"))):
        with open(path, encoding="utf-8") as handle:
            for line in handle:
                parts = line.rstrip("\n").split("\t")
                if len(parts) == 4:
                    yield (os.path.basename(path), *parts)


def read_scope():
    """Lemmas in scope = the senses of the coverage report of the relabeled corpus plus the senses of the curation."""
    path = os.path.join(lexdata.CORPUS, "coverage", "relabel-coverage.tsv")
    ids = []
    with open(path, encoding="utf-8") as handle:
        next(handle)
        for line in handle:
            ids.append(line.split("\t", 1)[0])
    seen = set(ids)
    with open(os.path.join(lexdata.CORPUS, "curation", "senses.tsv"), encoding="utf-8", newline="") as handle:
        for row in csv.DictReader(handle, delimiter="\t", quoting=csv.QUOTE_NONE):
            if row["sense_id"] not in seen:
                seen.add(row["sense_id"])
                ids.append(row["sense_id"])
    return ids


def curation_tiers():
    """sense_id -> (tier, tag_count text) of corpus/curation/senses.tsv (the tier the quota report uses)."""
    result = {}
    with open(os.path.join(lexdata.CORPUS, "curation", "senses.tsv"), encoding="utf-8", newline="") as handle:
        for row in csv.DictReader(handle, delimiter="\t", quoting=csv.QUOTE_NONE):
            result[row["sense_id"]] = (row["tier"], row["tag_count"])
    return result


def quota_status(senses, scope_ids=None):
    scope_ids = scope_ids or read_scope()
    tiers = curation_tiers()
    size = {}
    for s in senses.values():
        size[s["lemma"]] = size.get(s["lemma"], 0) + 1
    anchors, holdout = {}, {}
    for _, _lemma, sid, _pos, _s in corpus_rows("anchors"):
        anchors[sid] = anchors.get(sid, 0) + 1
    for _, _lemma, sid, _pos, _s in corpus_rows("holdout"):
        holdout[sid] = holdout.get(sid, 0) + 1
    rows = []
    for sid in scope_ids:
        s = senses[sid]
        tier, tags = tiers.get(sid) or (tier_of(s), str(s["tag_count"]))
        aq, hq = quota_of(tier, size[s["lemma"]])
        a, h = anchors.get(sid, 0), holdout.get(sid, 0)
        status = "met" if a >= aq and h >= hq else ("empty" if a + h == 0 else "short")
        rows.append({"sense_id": sid, "part_of_speech": s["pos_name"], "tier": tier,         "tag_count": tags,
                     "anchors": a, "anchor_quota": aq, "holdout": h, "holdout_quota": hq, "status": status})
    return rows


def write_coverage(rows, path):
    cols = ["sense_id", "part_of_speech", "tier", "tag_count", "anchors", "anchor_quota", "holdout", "holdout_quota", "status"]
    with open(path, "w", encoding="utf-8", newline="\n") as handle:
        handle.write("\t".join(cols) + "\n")
        for r in rows:
            handle.write("\t".join(str(r[c]) for c in cols) + "\n")


if __name__ == "__main__":
    senses = lexdata.load_senses()
    rows = quota_status(senses)
    import collections
    print(collections.Counter(r["status"] for r in rows))
    print("anchors missing", sum(max(0, r["anchor_quota"] - r["anchors"]) for r in rows),
          "holdout missing", sum(max(0, r["holdout_quota"] - r["holdout"]) for r in rows))
    # compare with the committed report
    old = {}
    with open(os.path.join(lexdata.CORPUS, "coverage", "relabel-coverage.tsv"), encoding="utf-8") as handle:
        next(handle)
        for line in handle:
            p = line.rstrip("\n").split("\t")
            old[p[0]] = p
    diff = [r["sense_id"] for r in rows if [r["sense_id"], r["part_of_speech"], r["tier"], str(r["tag_count"]), str(r["anchors"]),
            str(r["anchor_quota"]), str(r["holdout"]), str(r["holdout_quota"]), r["status"]] != old.get(r["sense_id"], [])
            and r["sense_id"] in old]
    print("rows differing from the committed report:", len(diff), diff[:5])

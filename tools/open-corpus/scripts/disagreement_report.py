#!/usr/bin/env python3
"""Report the disagreements between annotators on the pilot (input for rebuilding the guideline's hard-case list).

  disagreement_report.py --sentences S.tsv --first FIRST.tsv --second SECOND.tsv [--third THIRD.tsv] --out DIR

Each annotation file has columns uid, idx, upos (primary.tsv / second.tsv layout). A token is "disputed" when the first and
second annotators differ, or (with --third) when the first and third differ. Writes DIR/pair_counts.tsv (unordered tag pair,
count, most frequent words) and DIR/disagreements.tsv (one row per token with the sentence, the target token and the tags),
so that rule proposals can be checked against the evidence. Only aggregate information and our own sentences are used.
"""
import argparse
import collections
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from common import read_tsv, write_tsv


def load(path):
    return {(r["uid"], int(r["idx"])): r["upos"] for r in read_tsv(path)}


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--sentences", required=True)
    ap.add_argument("--first", required=True)
    ap.add_argument("--second", required=True)
    ap.add_argument("--third")
    ap.add_argument("--out", required=True)
    args = ap.parse_args()
    sents = {r["uid"]: r for r in read_tsv(args.sentences)}
    a, b = load(args.first), load(args.second)
    c = load(args.third) if args.third else {}
    rows, pairs = [], collections.defaultdict(list)
    for (uid, idx), tag in sorted(a.items()):
        other = [("second", b.get((uid, idx))), ("third", c.get((uid, idx)))]
        diff = [(n, t) for n, t in other if t is not None and t != tag]
        if not diff:
            continue
        s = sents[uid]
        toks = s["tokens"].split(" ")
        rows.append({"uid": uid, "family": s["family"], "idx": idx, "token": toks[idx], "first": tag, "second": b.get((uid, idx), ""),
                     "third": c.get((uid, idx), ""), "sentence": " ".join(f"{k}:{t}" for k, t in enumerate(toks))})
        for _, t in diff:
            pairs[tuple(sorted((tag, t)))].append(toks[idx].lower())
    write_tsv(os.path.join(args.out, "disagreements.tsv"), rows, ["uid", "family", "idx", "token", "first", "second", "third", "sentence"])
    out = [{"pair": "/".join(p), "count": len(w), "words": "; ".join(f"{k} {v}" for k, v in collections.Counter(w).most_common(12))}
           for p, w in sorted(pairs.items(), key=lambda x: -len(x[1]))]
    write_tsv(os.path.join(args.out, "pair_counts.tsv"), out, ["pair", "count", "words"])
    print(f"{len(rows)} disputed tokens of {len(a)} ({100 * len(rows) / len(a):.2f}%); {len(out)} tag pairs")


if __name__ == "__main__":
    main()

#!/usr/bin/env python3
"""EXPERIMENT ONLY (superseded): the final pipeline has no adjudicator (see quality.md).

Compare adjudication policies on the evaluation sample (evaluation only; the reference tags are never shown to any judge).

  compare_judges.py --workdir DIR --gold GOLD.tsv --out judges.json [--judges luna gemma qwen]

Policies: keep-primary (the primary annotator's tag wins every disagreement; the token is marked 'disputed') and one
policy per judge (the judge's blind choice of one of the two tags wins; 'U' keeps the primary tag, status unresolved).
For each policy: accuracy on the disagreement tokens, final UPOS accuracy and collapsed accuracy on all tokens, and how
often the judge picked the primary tag, the second tag, or U. Also the oracle upper bound (a perfect judge).
"""
import argparse
import json
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from common import COLLAPSE, read_tsv


def pct(x, n):
    return round(100.0 * x / n, 2) if n else None


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--workdir", required=True)
    ap.add_argument("--gold", required=True)
    ap.add_argument("--out", required=True)
    ap.add_argument("--judges", nargs="*", default=["luna", "gemma", "qwen"])
    args = ap.parse_args()
    gold = {(r["uid"], int(r["idx"])): r["upos"] for r in read_tsv(args.gold)}
    prim = {(r["uid"], int(r["idx"])): r["upos"] for r in read_tsv(os.path.join(args.workdir, "primary", "primary.tsv"))}
    sec = {(r["uid"], int(r["idx"])): r["upos"] for r in read_tsv(os.path.join(args.workdir, "second", "second.tsv"))}
    n = len(prim)
    dis = [k for k in prim if prim[k] != sec[k]]
    base_ok = sum(prim[k] == gold[k] for k in prim)
    base_c = sum(COLLAPSE[prim[k]] == COLLAPSE[gold[k]] for k in prim)
    res = {"tokens": n, "disagreement_tokens": len(dis),
           "second_alone_upos_percent": pct(sum(sec[k] == gold[k] for k in prim), n),
           "oracle_upos_percent": pct(sum(gold[k] in (prim[k], sec[k]) or prim[k] == gold[k] for k in prim), n),
           "policies": {"keep-primary": {
               "disagreement_correct": sum(prim[k] == gold[k] for k in dis), "disagreement_percent": pct(sum(prim[k] == gold[k] for k in dis), len(dis)),
               "final_upos_percent": pct(base_ok, n), "final_collapsed_percent": pct(base_c, n)}}}
    keyrows = {r["item"]: r for r in read_tsv(os.path.join(args.workdir, "adjudication", "key.tsv"))}
    for j in args.judges:
        path = os.path.join(args.workdir, "adjudication", f"adjudicated_{j}.tsv")
        if not os.path.exists(path):
            continue
        final = dict(prim)
        picked = {"primary": 0, "second": 0, "U": 0}
        for a in read_tsv(path):
            k = (a["uid"], int(a["idx"]))
            if a["choice"] == "U":
                picked["U"] += 1
            else:
                is_primary = (a["choice"] == "A") == (keyrows[a["item"]]["a_is"] == "primary")
                picked["primary" if is_primary else "second"] += 1
                final[k] = prim[k] if is_primary else sec[k]
        ok = sum(final[k] == gold[k] for k in dis)
        res["policies"][j] = {
            "picked_primary_tag": picked["primary"], "picked_second_tag": picked["second"], "unresolved": picked["U"],
            "disagreement_correct": ok, "disagreement_percent": pct(ok, len(dis)),
            "final_upos_percent": pct(sum(final[k] == gold[k] for k in prim), n),
            "final_collapsed_percent": pct(sum(COLLAPSE[final[k]] == COLLAPSE[gold[k]] for k in prim), n)}
    os.makedirs(os.path.dirname(os.path.abspath(args.out)), exist_ok=True)
    with open(args.out, "w", encoding="utf-8", newline="\n") as f:
        json.dump(res, f, indent=1)
        f.write("\n")
    print(json.dumps(res, indent=1))


if __name__ == "__main__":
    main()

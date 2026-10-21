#!/usr/bin/env python3
"""Corpus statistics (markdown tables for README.md): sentences and tokens per source family and split, documents,
UPOS distribution, consensus status. One part at a time; usage statistics come from the representative part only.

  stats.py                       representative part (default)
  stats.py --part targeted       targeted part: counts per split and per selection key (never usage statistics)
"""
import argparse
import collections
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from common import DATA, ROOT, TARGETED_DATA, read_tsv


def load(data_dir, names):
    sources = {r["source_id"]: r for r in read_tsv(os.path.join(ROOT, "sources.tsv"))}
    rows = []
    for name in names:
        path = os.path.join(data_dir, name + ".tsv")
        if not os.path.exists(path):
            continue
        text = open(path, encoding="utf-8").read()
        for block in [b for b in text.split("\n\n") if "\n# sent_id = " in "\n" + b]:
            lines = block.split("\n")
            meta = {l[2:].split(" = ", 1)[0]: l.split(" = ", 1)[1] for l in lines if l.startswith("# ") and " = " in l}
            toks = [l.split("\t") for l in lines if l and not l.startswith("#")]
            rows.append({"split": name, "sent_id": meta["sent_id"], "source_id": meta["source_id"], "selection": meta.get("selection", ""),
                         "family": sources[meta["source_id"]]["family"], "tokens": toks})
    return rows


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--part", choices=["representative", "targeted"], default="representative")
    args = ap.parse_args()
    targeted = args.part == "targeted"
    names = ("train", "test") if targeted else ("train", "dev", "test")
    rows = load(TARGETED_DATA if targeted else DATA, names)
    print(f"Part: {args.part}" + ("" if targeted else " (the only input for usage statistics)"))
    if not rows:
        print("no sentences")
        return
    fams = sorted({r["family"] for r in rows})
    print("| Family | " + " | ".join(names) + " | total sentences | documents | tokens |")
    print("|---|" + "---|" * (len(names) + 3))
    for fam in fams + ["ALL"]:
        sub = [r for r in rows if fam == "ALL" or r["family"] == fam]
        cells = [sum(1 for r in sub if r["split"] == s) for s in names]
        print(f"| {fam} | " + " | ".join(map(str, cells)) + f" | {len(sub)} | {len({r['source_id'] for r in sub})} | "
              f"{sum(len(r['tokens']) for r in sub)} |")
    toks = [t for r in rows for t in r["tokens"]]
    print("\nUPOS distribution:", ", ".join(f"{k} {v}" for k, v in collections.Counter(t[3] for t in toks).most_common()))
    print("Consensus:", dict(collections.Counter(t[6] for t in toks)))
    print("Tokens", len(toks), "sentences", len(rows), "documents", len({r["source_id"] for r in rows}))
    for s in names:
        print(s, "tokens", sum(len(r["tokens"]) for r in rows if r["split"] == s))
    if targeted:
        print("Selection keys:", dict(collections.Counter(r["selection"].split("=", 1)[0] for r in rows)))


if __name__ == "__main__":
    main()

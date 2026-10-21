#!/usr/bin/env python3
"""Drop sentences of sources excluded after selection and annotation.

  apply_exclusions.py --sentences build/sentences.tsv --exclusions build/post_exclusions.tsv --out build/sentences_final.tsv

The exclusions file has the columns source_id and reason. The annotation runs on the full selected set (the second annotator
was already running when a source was found to be unsuitable); every later step reads the filtered file, and sources.tsv
does not list the excluded sources.
"""
import argparse
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from common import read_tsv, write_tsv


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--sentences", required=True)
    ap.add_argument("--exclusions", required=True)
    ap.add_argument("--out", required=True)
    ap.add_argument("--sources", help="sources.tsv to rewrite without the excluded sources")
    args = ap.parse_args()
    excluded = {r["source_id"] for r in read_tsv(args.exclusions)}
    if args.sources:
        src = read_tsv(args.sources)
        write_tsv(args.sources, [r for r in src if r["source_id"] not in excluded], list(src[0].keys()))
    rows = read_tsv(args.sentences)
    kept = [r for r in rows if r["source_id"] not in excluded]
    write_tsv(args.out, kept, list(rows[0].keys()))
    print(f"{len(rows)} sentences, {len(rows) - len(kept)} dropped from {len(excluded)} excluded sources, {len(kept)} kept")


if __name__ == "__main__":
    main()

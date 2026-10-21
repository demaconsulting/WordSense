#!/usr/bin/env python3
"""Merge the primary annotation, the second annotator's UPOS and the adjudication into the final token table.

  finalize.py --sentences SENTENCES.tsv --workdir DIR

Per token: if the two annotators agree the status is 'unanimous'; if they disagree the adjudicated tag is used with
status 'adjudicated', or the primary annotator's tag with status 'unresolved' when the judge flagged it. With --policy keep-primary there is no judge: the primary tag is kept and the token is marked 'disputed'. A token of a sentence
that has no second annotation (the second pass was stopped or not run) keeps the primary tag with status 'unchecked'; rerunning
annotate_second.py resumes from its checkpoint, and rerunning this script then turns 'unchecked' into 'unanimous' or 'disputed'. Lemma and
features come from the primary annotator; features that are not allowed on the final UPOS are removed (counted).
Writes DIR/final.tsv: uid, idx, form, lemma, upos, collapsed, feats, status, primary_upos, second_upos.
"""
import argparse
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from common import COLLAPSE, UPOS, read_tsv, write_tsv
from ingest_primary import clean_feats


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--sentences", required=True)
    ap.add_argument("--workdir", required=True)
    ap.add_argument("--policy", default="keep-primary", help="keep-primary, or the judge whose adjudicated_NAME.tsv decides (luna, gemma, qwen)")
    args = ap.parse_args()
    keep = {r["uid"] for r in read_tsv(args.sentences)}
    prim = [r for r in read_tsv(os.path.join(args.workdir, "primary", "primary.tsv")) if r["uid"] in keep]
    sec = {(r["uid"], int(r["idx"])): r["upos"] for r in read_tsv(os.path.join(args.workdir, "second", "second.tsv"))}
    adj = {}
    if args.policy != "keep-primary":
        adj_path = os.path.join(args.workdir, "adjudication", f"adjudicated_{args.policy}.tsv")
        adj = {(r["uid"], int(r["idx"])): r for r in read_tsv(adj_path)}
    out, dropped = [], 0
    for p in prim:
        key = (p["uid"], int(p["idx"]))
        s = sec.get(key)
        if s is None:
            final, status, s = p["upos"], "unchecked", ""
        elif s == p["upos"]:
            final, status = p["upos"], "unanimous"
        elif args.policy == "keep-primary":
            final, status = p["upos"], "disputed"
        else:
            a = adj[key]
            final, status = a["final_upos"], a["status"]
        feats, bad, drop = clean_feats(final, p["feats"])
        if bad:
            raise SystemExit(f"{key}: {bad}")
        dropped += len(drop)
        out.append({"uid": p["uid"], "idx": p["idx"], "form": p["form"], "lemma": p["lemma"], "upos": final,
                    "collapsed": COLLAPSE[final], "feats": feats, "status": status, "primary_upos": p["upos"],
                    "second_upos": s})
    write_tsv(os.path.join(args.workdir, "final.tsv"), out,
              ["uid", "idx", "form", "lemma", "upos", "collapsed", "feats", "status", "primary_upos", "second_upos"])
    counts = {}
    for r in out:
        counts[r["status"]] = counts.get(r["status"], 0) + 1
    print(len(out), "tokens", counts, "features dropped after tag change:", dropped)


if __name__ == "__main__":
    main()

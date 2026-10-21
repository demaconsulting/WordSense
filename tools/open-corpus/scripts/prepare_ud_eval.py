#!/usr/bin/env python3
"""Draw the fixed 300-sentence accuracy-measurement sample from UD English-EWT DEV (evaluation only).

UD data never enters the corpus folder: the sentences and gold tags are written to --out-dir (outside the corpus) and
only the sample's sent_id values are recorded in eval/ud_dev_sample_ids.txt. The gold file also carries the UD features, the
relation and the head's tag, which only the convention map of evaluate.py uses.

The sample is disjoint from the 500-sentence exploratory sample used before the guideline was written (reproduced
here by the same procedure: eligible sentences, shuffled with seed 20261019, first 500), so that the measured accuracy
is not on sentences that were looked at when the guideline's topic list was chosen.

  prepare_ud_eval.py --ud en_ewt-ud-dev.conllu --out-dir tools/open-corpus/work/ud-eval

The UD file is downloaded by hand into the git-ignored tools/open-corpus/cache (pinned SHA-256 below); nothing from
UD is ever committed or shipped.
"""
import argparse
import os
import random
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from common import ROOT, sha256_file, write_tsv

PINNED_SHA256 = "39239E0A60DB3AE68F4B7036189F11B6692741D10FF8240DD91F74F2760D90F8".lower()
SEED = 3003
SAMPLE = 300
EXPLORATORY_SEED = 20261019
EXPLORATORY_COUNT = 500


def read_conllu(path):
    sents, cur, sid = [], [], None
    for line in open(path, encoding="utf-8"):
        line = line.rstrip("\n")
        if line.startswith("# sent_id"):
            sid = line.split("=", 1)[1].strip()
        if not line:
            if cur:
                sents.append((sid, cur))
                cur = []
            continue
        if line.startswith("#"):
            continue
        c = line.split("\t")
        if "-" in c[0] or "." in c[0]:
            continue
        cur.append({"form": c[1], "lemma": c[2], "upos": c[3], "feats": c[5], "head": c[6], "deprel": c[7]})
    if cur:
        sents.append((sid, cur))
    return sents


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--ud", required=True)
    ap.add_argument("--out-dir", required=True)
    args = ap.parse_args()
    digest = sha256_file(args.ud)
    if digest != PINNED_SHA256:
        sys.exit(f"unexpected SHA-256 {digest}")
    sents = read_conllu(args.ud)
    eligible = [i for i, (_, s) in enumerate(sents) if 0 < len(s) <= 40]
    explore = list(eligible)
    random.Random(EXPLORATORY_SEED).shuffle(explore)
    banned = set(explore[:EXPLORATORY_COUNT])
    pool = [i for i in eligible if i not in banned]
    random.Random(SEED).shuffle(pool)
    pick = sorted(pool[:SAMPLE])
    rows, gold, ids = [], [], []
    for n, i in enumerate(pick, 1):
        sid, toks = sents[i]
        uid = f"E{n:03d}"
        rows.append({"uid": uid, "sent_id": sid, "source_id": "ud-ewt-dev", "family": "eval", "group": "eval",
                     "ntokens": len(toks), "text": " ".join(t["form"] for t in toks), "tokens": " ".join(t["form"] for t in toks)})
        for k, t in enumerate(toks):
            head = toks[int(t["head"]) - 1] if t["head"].isdigit() and int(t["head"]) >= 1 and int(t["head"]) <= len(toks) else None
            gold.append({"uid": uid, "idx": k, "form": t["form"], "lemma": t["lemma"], "upos": t["upos"], "feats": t["feats"],
                         "deprel": t["deprel"], "head_upos": head["upos"] if head else "", "head_feats": head["feats"] if head else ""})
        ids.append(sid)
    os.makedirs(args.out_dir, exist_ok=True)
    write_tsv(os.path.join(args.out_dir, "sentences.tsv"), rows, ["uid", "sent_id", "source_id", "family", "group", "ntokens", "text", "tokens"])
    write_tsv(os.path.join(args.out_dir, "gold.tsv"), gold, ["uid", "idx", "form", "lemma", "upos", "feats", "deprel", "head_upos", "head_feats"])
    os.makedirs(os.path.join(ROOT, "eval"), exist_ok=True)
    with open(os.path.join(ROOT, "eval", "ud_dev_sample_ids.txt"), "w", encoding="utf-8", newline="\n") as f:
        f.write("# UD English-EWT dev sample used ONLY to measure annotation accuracy (ids only; no UD text is stored here)\n")
        f.write(f"# file: en_ewt-ud-dev.conllu sha256 {PINNED_SHA256}; commit 4a4d77f599ea53cc405f85d0cec4b2f14f81d42b\n")
        f.write(f"# seed {SEED}; {SAMPLE} sentences; disjoint from the exploratory sample (seed {EXPLORATORY_SEED}, first {EXPLORATORY_COUNT})\n")
        f.write("\n".join(ids) + "\n")
    print(len(rows), "sentences", len(gold), "tokens")


if __name__ == "__main__":
    main()

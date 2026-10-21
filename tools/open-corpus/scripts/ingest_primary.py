#!/usr/bin/env python3
"""Ingest and validate the primary annotator's batch outputs.

  ingest_primary.py --sentences SENTENCES.tsv --workdir DIR

Reads DIR/primary/out/*.tsv and DIR/primary/redo_out/*.tsv (a redo overrides the original for the same sentence),
validates every sentence (token count, forms, UPOS in the tag set, non-empty lemma, features well formed and allowed for
the UPOS) and writes DIR/primary/primary.tsv (uid, idx, form, lemma, upos, feats) for valid sentences and
DIR/primary/invalid.txt (uid and reason) for the rest. Feature problems that can be repaired mechanically (a feature
that is not allowed on that UPOS, or a bad ordering) are repaired and counted; unknown names or values make the
sentence invalid.
"""
import argparse
import glob
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from common import FEATURE_VALUES, FEATURES_FOR_UPOS, UPOS, read_tsv, write_tsv


def clean_feats(upos, feats):
    """Returns (normalized feats, problem or '', dropped features)."""
    if feats in ("", "_"):
        return "_", "", []
    pairs, dropped = [], []
    for part in feats.split("|"):
        name, eq, value = part.partition("=")
        if not eq or name not in FEATURE_VALUES or value not in FEATURE_VALUES[name]:
            return feats, f"malformed feature {part!r}", []
        if name in FEATURES_FOR_UPOS.get(upos, set()):
            pairs.append((name, value))
        else:
            dropped.append(part)
    pairs = sorted(set(pairs))
    names = [n for n, _ in pairs]
    if len(names) != len(set(names)):
        return feats, "duplicate feature name", []
    return ("|".join(f"{n}={v}" for n, v in pairs) or "_"), "", dropped


def load(paths):
    by_uid = {}
    for p in paths:
        for line in open(p, encoding="utf-8"):
            line = line.rstrip("\r\n")
            if not line or line.startswith("#"):
                continue
            c = line.split("\t")
            by_uid.setdefault(c[0], []).append(c)
    return by_uid


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--sentences", required=True)
    ap.add_argument("--workdir", required=True)
    args = ap.parse_args()
    base = os.path.join(args.workdir, "primary")
    rows = read_tsv(args.sentences)
    orig = load(sorted(glob.glob(os.path.join(base, "out", "*.tsv"))))
    redo = load(sorted(glob.glob(os.path.join(base, "redo_out", "*.tsv"))))
    out, invalid, repaired = [], [], {"dropped_features": 0, "sentences_redone": 0}
    for s in rows:
        toks = s["tokens"].split(" ")
        problem = ""
        sources = [redo.get(s["uid"]), orig.get(s["uid"])]
        for lines in [x for x in sources if x]:
            problem, parsed, dropped_total = "", [], 0
            if len(lines) != len(toks):
                problem = f"expected {len(toks)} lines, got {len(lines)}"
            else:
                for k, (c, tok) in enumerate(zip(lines, toks)):
                    if len(c) != 6:
                        problem = f"token {k}: {len(c)} columns"
                    elif c[1] != str(k) or c[2] != tok:
                        problem = f"token {k}: index/form mismatch ({c[1]} {c[2]!r} vs {tok!r})"
                    elif c[4] not in UPOS:
                        problem = f"token {k}: bad UPOS {c[4]!r}"
                    elif not c[3].strip():
                        problem = f"token {k}: empty lemma"
                    else:
                        feats, bad, dropped = clean_feats(c[4], c[5])
                        if bad:
                            problem = f"token {k}: {bad}"
                        else:
                            parsed.append({"uid": s["uid"], "idx": k, "form": tok, "lemma": c[3], "upos": c[4], "feats": feats})
                            dropped_total += len(dropped)
                    if problem:
                        break
            if not problem:
                out.extend(parsed)
                repaired["dropped_features"] += dropped_total
                if lines is redo.get(s["uid"]):
                    repaired["sentences_redone"] += 1
                break
        else:
            invalid.append((s["uid"], problem or "missing"))
    write_tsv(os.path.join(base, "primary.tsv"), out, ["uid", "idx", "form", "lemma", "upos", "feats"])
    with open(os.path.join(base, "invalid.txt"), "w", encoding="utf-8", newline="\n") as f:
        for uid, why in invalid:
            f.write(f"{uid}\t{why}\n")
    print(f"valid sentences {len(rows) - len(invalid)}/{len(rows)}; invalid {len(invalid)}; {repaired}")
    for uid, why in invalid[:20]:
        print("  invalid", uid, why)


if __name__ == "__main__":
    main()

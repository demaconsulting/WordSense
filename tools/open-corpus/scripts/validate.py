#!/usr/bin/env python3
"""Mechanical validation of both parts of the corpus: representative/data/{train,dev,test}.tsv and targeted/data/{train,test}.tsv.

  validate.py [--rep-data DIR] [--targeted-data DIR] [--sources sources.tsv]

Checks, for both parts: header and comment lines; seven columns per token; sequential ids; UPOS in the 17-tag set; collapsed tag
equals the mapping of the UPOS; lemma non-empty; features well formed, sorted, allowed for the UPOS; consensus status in
{unanimous, adjudicated, unresolved, disputed, unchecked}; the tokens concatenate to the sentence text (no token lost or
invented); 4-40 tokens per sentence; every source_id is listed in sources.tsv; every document is in exactly one split and that
split equals the hash rule of split_data.py; no duplicate sent_id or sentence text, also ACROSS the two parts.
Representative: no `# selection` or `# query` line (the part is a random sample).
Targeted: every sentence has a non-empty `# selection = <key>=<value>` line (key one of sense, convention, form, genre, error,
pattern) and a non-empty `# query = ...` line; only train and test files exist.
Exit code 1 on any error.
"""
import argparse
import os
import re
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from common import COLLAPSE, DATA, FEATURE_VALUES, FEATURES_FOR_UPOS, ROOT, TARGETED_DATA, UPOS, read_tsv
from split_data import split_of, targeted_split_of

STATUSES = {"unanimous", "adjudicated", "unresolved", "disputed", "unchecked"}
SELECTION_RE = re.compile(r"^(sense|convention|form|genre|error|pattern)=\S.*$")


def check_part(part, data_dir, names, pick, known, errors, seen_ids, seen_text):
    targeted = part == "targeted"
    doc_split, totals = {}, {}
    for name in names:
        path = os.path.join(data_dir, name + ".tsv")
        if not os.path.exists(path):
            errors.append(f"{part}/{name}: file missing")
            continue
        text = open(path, encoding="utf-8").read()
        if "\r" in text:
            errors.append(f"{part}/{name}: CR characters")
        blocks = [b for b in text.split("\n\n") if b.strip()]
        n_sent = n_tok = 0
        for block in blocks:
            lines = [l for l in block.split("\n") if l]
            comments = {l[2:].split(" = ", 1)[0]: l.split(" = ", 1)[1] for l in lines if l.startswith("# ") and " = " in l}
            toks = [l.split("\t") for l in lines if not l.startswith("#")]
            if "sent_id" not in comments:
                if toks:  # only a comment-only block is the file header
                    errors.append(f"{part}/{name}: a block of {len(toks)} token lines has no `# sent_id` line")
                continue
            sid = comments["sent_id"]
            n_sent += 1
            n_tok += len(toks)
            if sid in seen_ids:
                errors.append(f"{sid}: duplicate sent_id")
            seen_ids.add(sid)
            if comments.get("text") in seen_text:
                errors.append(f"{sid}: duplicate text")
            seen_text.add(comments.get("text"))
            if targeted:
                sel, query = comments.get("selection", ""), comments.get("query", "")
                if not SELECTION_RE.match(sel):
                    errors.append(f"{sid}: missing or malformed `# selection` line ({sel!r})")
                if not query.strip():
                    errors.append(f"{sid}: missing `# query` line")
            elif "selection" in comments or "query" in comments:
                errors.append(f"{sid}: representative sentences must not carry selection/query lines")
            src = comments.get("source_id", "")
            if src not in known:
                errors.append(f"{sid}: source_id {src} not in sources.tsv")
            if doc_split.setdefault(src, name) != name or pick(src) != name:
                errors.append(f"{sid}: document {src} in split {name}, expected {pick(src)}")
            if not 4 <= len(toks) <= 40:
                errors.append(f"{sid}: {len(toks)} tokens")
            if "".join(t[1] for t in toks if len(t) > 1) != comments.get("text", "").replace(" ", ""):
                errors.append(f"{sid}: tokens do not concatenate to the text")
            for k, t in enumerate(toks, 1):
                if len(t) != 7:
                    errors.append(f"{sid}: token line with {len(t)} columns")
                    continue
                tid, form, lemma, upos, coll, feats, status = t
                if tid != str(k):
                    errors.append(f"{sid}: id {tid} expected {k}")
                if upos not in UPOS:
                    errors.append(f"{sid}:{k} bad UPOS {upos}")
                elif coll != COLLAPSE[upos]:
                    errors.append(f"{sid}:{k} collapsed {coll} != {COLLAPSE[upos]}")
                if not lemma.strip() or not form.strip():
                    errors.append(f"{sid}:{k} empty form or lemma")
                if status not in STATUSES:
                    errors.append(f"{sid}:{k} bad status {status}")
                if feats != "_":
                    pairs = [p.split("=", 1) for p in feats.split("|")]
                    names = [p[0] for p in pairs]
                    if len(set(names)) != len(names):
                        errors.append(f"{sid}:{k} repeated feature name in {feats}")
                    if pairs != sorted(pairs) or any(len(p) != 2 or p[0] not in FEATURE_VALUES or p[1] not in FEATURE_VALUES[p[0]]
                                                     or p[0] not in FEATURES_FOR_UPOS.get(upos, set()) for p in pairs):
                        errors.append(f"{sid}:{k} bad features {feats} for {upos}")
        totals[name] = (n_sent, n_tok)
    if targeted:
        for extra in ("dev.tsv",):
            if os.path.exists(os.path.join(data_dir, extra)):
                errors.append(f"targeted/{extra}: the targeted part has only train and test")
    return totals


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--rep-data", default=DATA)
    ap.add_argument("--targeted-data", default=TARGETED_DATA)
    ap.add_argument("--sources", default=os.path.join(ROOT, "sources.tsv"))
    args = ap.parse_args()
    known = {r["source_id"] for r in read_tsv(args.sources)}
    errors, seen_ids, seen_text = [], set(), set()
    rep = check_part("representative", args.rep_data, ("train", "dev", "test"), split_of, known, errors, seen_ids, seen_text)
    tgt = check_part("targeted", args.targeted_data, ("train", "test"), targeted_split_of, known, errors, seen_ids, seen_text)
    print("validated representative", rep)
    print("validated targeted", tgt)
    for e in errors[:50]:
        print("ERROR", e)
    print(f"{len(errors)} errors")
    sys.exit(1 if errors else 0)


if __name__ == "__main__":
    main()

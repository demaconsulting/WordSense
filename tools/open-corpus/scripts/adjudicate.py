#!/usr/bin/env python3
"""EXPERIMENT ONLY (superseded): the final pipeline has no adjudicator (see quality.md).

Adjudication of every token on which the two annotators disagree (UPOS).

  adjudicate.py prepare --workdir DIR [--batch-size 60]
  adjudicate.py ingest  --workdir DIR [--judge luna|NAME]

prepare: compares DIR/primary/primary.tsv with DIR/second/second.tsv, writes one item per disagreeing token into
DIR/adjudication/in/batchNN.txt and the private key DIR/adjudication/key.tsv (item, uid, idx, tags, which annotator was
shown as A). The judge sees only the guideline, the sentence, the target token and two anonymous candidate tags; which
candidate came from which annotator is decided by a hash of the token position (about half and half) and is not shown.
ingest: reads DIR/adjudication/out/*.tsv (item TAB A|B|U), maps the choice back through the key and writes
DIR/adjudication/adjudicated_JUDGE.tsv (item, uid, idx, primary_upos, second_upos, choice, final_upos, status), status being
'adjudicated' (A or B) or 'unresolved' (U).
"""
import argparse
import glob
import hashlib
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from common import ROOT, SEED, read_tsv, write_tsv

TASK = """You are an impartial judge of part-of-speech tags. Do exactly the following and nothing else.

1. Read the annotation guideline: {guideline}
2. Read the items to judge: {input}
   Each item shows a tokenized sentence (idx:token), one target token, and two candidate UPOS tags, Option A and Option B.
3. For each item decide which option the guideline supports for that token in that sentence. Answer `A` or `B`.
   Answer `U` only if the guideline and the sentence genuinely cannot decide between them (an unresolvable
   ambiguity) - do not use `U` just because the choice is difficult.
4. Write one line per item to this file with the `create` tool: {output}
   Each line is the item id, a TAB character, and the answer (A, B or U), for example `A0007<TAB>B`. No header, no
   comments, one line per item, in the same order as the input.
5. Finish by replying with one line: the number of lines you wrote.

Rules: judge only the target token; do not read, search or use any other file, web page, treebank or tool; do not run
commands; do not ask questions.
"""


def read_tags(path, col):
    out = {}
    for r in read_tsv(path):
        out[(r["uid"], int(r["idx"]))] = r
    return out


def cmd_prepare(args):
    base = os.path.join(args.workdir, "adjudication")
    prim = read_tags(os.path.join(args.workdir, "primary", "primary.tsv"), "upos")
    sec = read_tags(os.path.join(args.workdir, "second", "second.tsv"), "upos")
    sentences = {r["uid"]: r["tokens"].split(" ") for r in read_tsv(args.sentences)}
    key_path = os.path.join(base, "key.tsv")
    old = read_tsv(key_path) if os.path.exists(key_path) else []  # incremental: items already judged keep their ids
    known = {(r["uid"], int(r["idx"])) for r in old}
    first_new_batch = len(os.listdir(os.path.join(base, "in"))) if old else 0
    items = []
    for (uid, idx), p in sorted(prim.items()):
        s = sec.get((uid, idx))
        if s is None or s["upos"] == p["upos"] or (uid, idx) in known:
            continue
        a_is_primary = int(hashlib.sha256(f"{SEED}:{uid}:{idx}".encode()).hexdigest()[:8], 16) % 2 == 0
        items.append({"item": f"A{len(old) + len(items) + 1:04d}", "uid": uid, "idx": idx, "primary_upos": p["upos"],
                      "second_upos": s["upos"], "a_is": "primary" if a_is_primary else "second"})
    os.makedirs(os.path.join(base, "in"), exist_ok=True)
    os.makedirs(os.path.join(base, "out"), exist_ok=True)
    os.makedirs(os.path.join(base, "prompt"), exist_ok=True)
    write_tsv(key_path, old + items, ["item", "uid", "idx", "primary_upos", "second_upos", "a_is"])
    guideline = os.path.join(ROOT, "GUIDELINE.md")
    for b in range(0, len(items), args.batch_size):
        name = f"batch{first_new_batch + b // args.batch_size:02d}"
        in_path = os.path.join(base, "in", name + ".txt")
        with open(in_path, "w", encoding="utf-8", newline="\n") as f:
            for it in items[b:b + args.batch_size]:
                toks = sentences[it["uid"]]
                a, bb = (it["primary_upos"], it["second_upos"]) if it["a_is"] == "primary" else (it["second_upos"], it["primary_upos"])
                f.write(f"### item {it['item']}\nSentence: {' '.join(f'{k}:{t}' for k, t in enumerate(toks))}\n"
                        f"Target token: {it['idx']} {toks[it['idx']]!r}\nOption A: {a}\nOption B: {bb}\n\n")
        out_path = os.path.abspath(os.path.join(base, "out", name + ".tsv"))
        with open(os.path.join(base, "prompt", name + ".md"), "w", encoding="utf-8", newline="\n") as f:
            f.write(TASK.format(guideline=guideline, input=os.path.abspath(in_path), output=out_path))
    print(f"{len(items)} disagreeing tokens in {(len(items) + args.batch_size - 1) // args.batch_size} batches")


def cmd_ingest(args):
    base = os.path.join(args.workdir, "adjudication")
    key = {r["item"]: r for r in read_tsv(os.path.join(base, "key.tsv"))}
    judge = args.judge
    folder = "out" if judge == "luna" else f"out_{judge}"  # sub-agent judge (gpt-5.6-luna) writes to out/
    choices = {}
    for p in sorted(glob.glob(os.path.join(base, folder, "*.tsv"))):
        for line in open(p, encoding="utf-8"):
            parts = line.rstrip("\r\n").split("\t")
            if len(parts) == 2 and parts[0] in key and parts[1].strip() in ("A", "B", "U"):
                choices[parts[0]] = parts[1].strip()
    rows, missing = [], []
    for item, k in key.items():
        c = choices.get(item)
        if c is None:
            missing.append(item)
            continue
        if c == "U":
            final, status = k["primary_upos"], "unresolved"
        else:
            chosen_is_primary = (c == "A") == (k["a_is"] == "primary")
            final, status = (k["primary_upos"] if chosen_is_primary else k["second_upos"]), "adjudicated"
        rows.append({"item": item, "uid": k["uid"], "idx": k["idx"], "primary_upos": k["primary_upos"],
                     "second_upos": k["second_upos"], "choice": c, "final_upos": final, "status": status})
    write_tsv(os.path.join(base, f"adjudicated_{judge}.tsv"), rows,
              ["item", "uid", "idx", "primary_upos", "second_upos", "choice", "final_upos", "status"])
    with open(os.path.join(base, f"missing_{judge}.txt"), "w", encoding="utf-8", newline="\n") as f:
        f.write("\n".join(missing) + ("\n" if missing else ""))
    print(f"adjudicated {len(rows)} items; missing {len(missing)}")


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    sub = ap.add_subparsers(dest="cmd", required=True)
    p = sub.add_parser("prepare")
    p.add_argument("--workdir", required=True)
    p.add_argument("--sentences", required=True)
    p.add_argument("--batch-size", type=int, default=60)
    p.set_defaults(fn=cmd_prepare)
    i = sub.add_parser("ingest")
    i.add_argument("--workdir", required=True)
    i.add_argument("--judge", default="luna", help="luna (sub-agent judge, out/), or the name of an Ollama judge (out_NAME/)")
    i.set_defaults(fn=cmd_ingest)
    args = ap.parse_args()
    args.fn(args)


if __name__ == "__main__":
    main()

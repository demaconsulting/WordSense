#!/usr/bin/env python3
"""Prepare the primary-annotator batches and the few-shot rendering.

  prepare_primary.py --sentences SENTENCES.tsv --workdir DIR [--batch-size 50] [--redo]

Writes DIR/primary/in/batchNN.txt (the tokens to annotate: sent_id, idx, form per line, with a comment line showing
each sentence) and DIR/primary/prompt/batchNN.md (the exact task prompt given to the sub-agent). With --redo it writes
the sentences that failed validation into DIR/primary/redo_in/ instead.
The same few-shot examples (prompts/fewshot.txt) are rendered for the primary prompt here and, UPOS only, for the
second annotator in annotate_second.py.
"""
import argparse
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from common import PROMPTS, ROOT, read_tsv


def load_fewshot():
    examples, cur = [], []
    for line in open(os.path.join(PROMPTS, "fewshot.txt"), encoding="utf-8"):
        line = line.rstrip("\n")
        if line.startswith("#"):
            continue
        if not line:
            if cur:
                examples.append(cur)
                cur = []
            continue
        form, lemma, upos, feats = line.split("|", 3)
        cur.append((form, lemma, upos, feats))
    if cur:
        examples.append(cur)
    return examples


def render_fewshot_primary():
    out = []
    for n, ex in enumerate(load_fewshot(), 1):
        out.append(f"# Example sentence: {' '.join(t[0] for t in ex)}")
        for k, (form, lemma, upos, feats) in enumerate(ex):
            out.append(f"X{n}\t{k}\t{form}\t{lemma}\t{upos}\t{feats}")
        out.append("")
    return "\n".join(out)


TASK = """You are a deterministic part-of-speech annotation tool. Do exactly the following and nothing else.

1. Read the annotation guideline: {guideline}
2. Read the worked examples (the output format you must produce): {fewshot}
3. Read the input batch: {input}
4. For every token line in the input, write one output line: the three input columns (sent_id, idx, form) copied
   exactly, followed by lemma, UPOS and features, all separated by single TAB characters. Output exactly one line per
   input token, in the same order, with no header, no comment lines, no blank lines and no extra text.
   Features are `Name=Value` pairs joined with `|` in alphabetical order, or `_` when none apply.
5. Write the complete result to this file with the `create` tool: {output}
6. Finish by replying with one line: the number of token lines you wrote.

Rules: apply the guideline strictly; never re-tokenize (the form column must match the input exactly); use only the 17
UPOS tags; do not read, search or use any other file, web page, treebank or tool; do not run commands; do not ask
questions. The lines starting with `#` in the input are only the sentence text for your convenience.
"""


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--sentences", required=True)
    ap.add_argument("--workdir", required=True)
    ap.add_argument("--batch-size", type=int, default=50)
    ap.add_argument("--redo", action="store_true")
    ap.add_argument("--uids", help="file with uids to redo (one per line)")
    args = ap.parse_args()
    rows = read_tsv(args.sentences)
    base = os.path.join(args.workdir, "primary")
    indir = os.path.join(base, "redo_in" if args.redo else "in")
    outdir = "redo_out" if args.redo else "out"
    if args.redo:
        wanted = {l.strip() for l in open(args.uids, encoding="utf-8") if l.strip()}
        rows = [r for r in rows if r["uid"] in wanted]
    os.makedirs(indir, exist_ok=True)
    os.makedirs(os.path.join(base, "prompt"), exist_ok=True)
    os.makedirs(os.path.join(base, outdir), exist_ok=True)
    fewshot_path = os.path.join(base, "fewshot_primary.md")
    with open(fewshot_path, "w", encoding="utf-8", newline="\n") as f:
        f.write(render_fewshot_primary())
    guideline = os.path.join(ROOT, "GUIDELINE.md")
    for b in range(0, len(rows), args.batch_size):
        chunk = rows[b:b + args.batch_size]
        name = f"batch{b // args.batch_size:02d}"
        in_path = os.path.join(indir, name + ".txt")
        with open(in_path, "w", encoding="utf-8", newline="\n") as f:
            for r in chunk:
                f.write(f"# {r['uid']}: {r['text']}\n")
                for k, tok in enumerate(r["tokens"].split(" ")):
                    f.write(f"{r['uid']}\t{k}\t{tok}\n")
        out_path = os.path.abspath(os.path.join(base, outdir, name + ".tsv"))
        with open(os.path.join(base, "prompt", ("redo_" if args.redo else "") + name + ".md"), "w", encoding="utf-8", newline="\n") as f:
            f.write(TASK.format(guideline=guideline, fewshot=os.path.abspath(fewshot_path), input=os.path.abspath(in_path),
                                output=out_path))
    print(f"{len(rows)} sentences in {(len(rows) + args.batch_size - 1) // args.batch_size} batches -> {indir}")


if __name__ == "__main__":
    main()

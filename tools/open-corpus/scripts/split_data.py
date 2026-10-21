#!/usr/bin/env python3
"""Split the final annotation into train/dev/test files by DOCUMENT, for one of the two parts of the corpus.

  split_data.py --sentences build/sentences_final.tsv --workdir annotations                       (representative part, default)
  split_data.py --part targeted --sentences FILE.tsv --workdir DIR                                (targeted part)

representative: writes representative/data/{train,dev,test}.tsv. The split of a document is a function of its source_id only:
bucket = int(SHA-256(source_id)[:8], 16) % 100; buckets 0-79 -> train, 80-89 -> dev, 90-99 -> test. The sentences file
must be a random sample (it must not have a `selection` column: hand-picked sentences never go into this part).

targeted: writes targeted/data/{train,test}.tsv. The sentences file must have the columns `selection` and `query` (the reason
the sentence was chosen and how it was found; see targeted/README.md). The split uses the SAME document bucket: buckets 0-79 ->
train, 80-99 -> test (the challenge set). A document therefore lands in the same side of the train/evaluation line in both
parts (representative train -> targeted train; representative dev and test -> targeted test), so training on targeted/train never
sees a document that representative/dev or representative/test evaluates on.

All sentences of one document land in the same split, so no sentence leaks across splits through its neighbors.

File format (our own, CoNLL-U-like; documented in README.md): UTF-8, LF, one token per line with seven TAB separated
columns id, form, lemma, upos, collapsed, feats, consensus (unanimous = the primary and the second annotator agree on the UPOS;
disputed = they differ, the tag is the primary annotator's; unchecked = no second annotation exists for the sentence);
sentences separated by a blank line and introduced by the comment lines `# sent_id = ...`, `# source_id = ...`, `# text = ...`
(targeted adds `# selection = ...` and `# query = ...` before `# text`).
"""
import argparse
import hashlib
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from common import DATA, TARGETED_DATA, read_tsv

COLUMNS = "id form lemma upos collapsed feats consensus"


def bucket_of(source_id):
    return int(hashlib.sha256(source_id.encode("utf-8")).hexdigest()[:8], 16) % 100


def split_of(source_id):
    """Representative part: train / dev / test."""
    b = bucket_of(source_id)
    return "train" if b < 80 else "dev" if b < 90 else "test"


def targeted_split_of(source_id):
    """Targeted part: train / test (the challenge set)."""
    return "train" if bucket_of(source_id) < 80 else "test"


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--part", choices=["representative", "targeted"], default="representative")
    ap.add_argument("--sentences", required=True)
    ap.add_argument("--workdir", required=True)
    ap.add_argument("--out-dir", help="override the output folder (testing)")
    args = ap.parse_args()
    targeted = args.part == "targeted"
    sentences = read_tsv(args.sentences) if os.path.getsize(args.sentences) else []
    has_selection = bool(sentences) and "selection" in sentences[0]
    if not targeted and has_selection:
        raise SystemExit("the representative part must be a random sample: the sentences file has a `selection` column")
    if targeted and sentences and not (has_selection and "query" in sentences[0]):
        raise SystemExit("targeted sentences need the columns `selection` and `query`")
    out_dir = args.out_dir or (TARGETED_DATA if targeted else DATA)
    names = ("train", "test") if targeted else ("train", "dev", "test")
    pick = targeted_split_of if targeted else split_of
    tokens = {}
    for r in read_tsv(os.path.join(args.workdir, "final.tsv")) if sentences else []:
        tokens.setdefault(r["uid"], []).append(r)
    os.makedirs(out_dir, exist_ok=True)
    files = {n: [] for n in names}
    counts = {n: [0, 0] for n in names}
    docs = {n: set() for n in names}
    for s in sentences:
        name = pick(s["source_id"])
        lines = [f"# sent_id = {s['sent_id']}", f"# source_id = {s['source_id']}"]
        if targeted:
            lines += [f"# selection = {s['selection']}", f"# query = {s['query']}"]
        lines.append(f"# text = {s['text']}")
        for t in tokens[s["uid"]]:
            lines.append("\t".join([str(int(t["idx"]) + 1), t["form"], t["lemma"], t["upos"], t["collapsed"], t["feats"], t["status"]]))
        files[name].append("\n".join(lines))
        counts[name][0] += 1
        counts[name][1] += len(tokens[s["uid"]])
        docs[name].add(s["source_id"])
    if targeted:
        header = ("# corpus = open English POS corpus, TARGETED part (sentences chosen on purpose; never use for usage statistics; test = challenge set)\n"
                  "# license = annotations and selection CC0 1.0; sentences public domain or CC0 per source, see sources.tsv\n"
                  f"# columns = {COLUMNS}\n# consensus = unanimous | disputed | unchecked\n"
                  "# sentence comments = sent_id, source_id, selection (required), query (required), text\n\n")
    else:
        header = ("# corpus = open English POS corpus, REPRESENTATIVE part (random sampling; the only input for usage statistics)\n"
                  "# license = annotations and selection CC0 1.0; sentences public domain or CC0 per source, see sources.tsv\n"
                  f"# columns = {COLUMNS}\n# consensus = unanimous | disputed | unchecked\n\n")
    for name, blocks in files.items():
        with open(os.path.join(out_dir, name + ".tsv"), "w", encoding="utf-8", newline="\n") as f:
            f.write(header + ("\n\n".join(blocks) + "\n" if blocks else ""))
    for n, (ns, nt) in counts.items():
        print(f"{args.part}/{n}: {ns} sentences, {nt} tokens, {len(docs[n])} documents")


if __name__ == "__main__":
    main()

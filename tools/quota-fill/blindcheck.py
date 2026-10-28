"""Blind check of the rows that a wave selected: every row is labeled again by a labeler that never sees the gold label.

  python blindcheck.py export WAVE [--kind real|written] [--every N]
        work/w<WAVE>/chk-in/*.json (+ chk-meta): the rows of quota-w<WAVE>-<kind>.tsv (anchors and holdout) as labeling
        batches of the labeler format; the senses are shuffled. --every N takes every Nth row (a sample)
  python blindcheck.py judge WAVE   reads chk-out/*.tsv (first labeler) and writes chk2-in/*.json: the rows that the first
        labeler did not confirm (another sense, low confidence or unsure), with the senses in another order, for a second labeler
  python blindcheck.py apply WAVE   reads chk2-out/*.tsv and changes the corpus files of the wave in place: a row stays when the
        first labeler, or else the second, names its sense with high confidence; it is relabeled to another sense of the same
        lemma when both name that sense with high confidence; every other row is removed (anchors, holdout, choosers,
        provenance). Writes work/w<WAVE>/chk-report.tsv

Batch format, prompts and answers are those of sentence-labeler.md. Run it after `fill.py select` and before the written
top-up, so that the written rows fill what the check removed.
"""
import argparse
import collections
import glob
import hashlib
import os
import random
import re
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import fill
import harvest
import qf
import shortfix

SPLITS = ("anchors", "holdout")


def wave_dir(wave):
    return os.path.join(qf.WORK, f"w{wave}")


def corpus_path(folder, wave, kind):
    return os.path.join(qf.CORPUS, folder, f"quota-w{wave}-{kind}.tsv")


def read_rows(wave, kind):
    """(split, lemma, sense, pos name, sentence) of the wave's rows, in file order."""
    rows = []
    for split in SPLITS:
        path = corpus_path(split, wave, kind)
        if not os.path.exists(path):
            continue
        with open(path, encoding="utf-8") as handle:
            for line in handle:
                p = line.rstrip("\n").split("\t")
                if len(p) == 4:
                    rows.append((split, p[0], p[1], p[2], p[3]))
    return rows


def form_of(sentence):
    m = re.search(r"\{([A-Za-z]+)\}", sentence)
    return m.group(1).lower() if m else None


def write_batches(world, items, wdir, in_dir, meta_dir, prefix, seed):
    """items: dicts with sentence, form, lemma, sense, split. One labeling batch per about 230 items."""
    groups = collections.defaultdict(list)
    skipped = 0
    for it in items:
        key = harvest.group_key(world, it["form"]) if it["form"] else ()
        if not key or it["sense"] not in harvest.senses_of_pairs(world, key):
            skipped += 1
            continue
        groups[key].append(it)
    batches, cur, cur_items, cur_senses = [], [], 0, 0
    for key in sorted(groups):
        sense_ids = harvest.senses_of_pairs(world, key)
        part_items = groups[key]
        pos = 0
        while pos < len(part_items):
            room = harvest.BATCH_ITEMS - cur_items
            if room <= 0 or (cur_senses + len(sense_ids) > harvest.BATCH_SENSES and cur):
                batches.append(cur)
                cur, cur_items, cur_senses = [], 0, 0
                room = harvest.BATCH_ITEMS
            part = part_items[pos:pos + room]
            cur.append((key, sense_ids, part))
            cur_items += len(part)
            cur_senses += len(sense_ids)
            pos += len(part)
    if cur:
        batches.append(cur)
    os.makedirs(os.path.join(wdir, in_dir), exist_ok=True)
    os.makedirs(os.path.join(wdir, meta_dir), exist_ok=True)
    for b, batch in enumerate(batches, 1):
        bid = f"{prefix}{b:03d}"
        rng = random.Random(f"{seed}|{bid}")
        groups_json, meta, n = [], {}, 0
        for gi, (key, sense_ids, part) in enumerate(batch, 1):
            shuffled = sense_ids[:]
            rng.shuffle(shuffled)
            part = part[:]
            rng.shuffle(part)
            its = []
            for it in part:
                n += 1
                its.append({"n": n, "sentence": it["sentence"]})
                meta[str(n)] = {**it, "group": gi}
            groups_json.append({"senses": [qf.sense_line(world, x) for x in shuffled], "items": its})
        qf.write_json(os.path.join(wdir, in_dir, bid + ".json"), {"batch": bid, "groups": groups_json})
        qf.write_json(os.path.join(wdir, meta_dir, bid + ".json"),
                      {"groups": [[list(p) for p in key] for key, _s, _p in batch], "items": meta})
    return len(batches), skipped


def prefix(kind):
    """The folder prefix of the check of a kind: chk for real rows, wchk for written rows."""
    return "chk" if kind == "real" else "wchk"


def cmd_export(args):
    pre = prefix(args.kind)
    world = qf.load_world()
    rows = read_rows(args.wave, args.kind)
    if args.every > 1:
        rows = rows[::args.every]
    items = [{"split": sp, "lemma": lemma, "sense": sense, "pos": pos, "sentence": sentence, "form": form_of(sentence)}
             for sp, lemma, sense, pos, sentence in rows]
    wdir = wave_dir(args.wave)
    for sub in (pre + "-out", pre + "2-out"):
        os.makedirs(os.path.join(wdir, sub), exist_ok=True)
    n, skipped = write_batches(world, items, wdir, pre + "-in", pre + "-meta", "c", f"chk|{args.wave}")
    print(len(items), "rows,", n, "batches, skipped (no sense list for the form):", skipped)


def read_answers(path):
    """n -> (sense, confidence) or None (unsure) of a labeler output, or None when the file is missing."""
    return fill.read_labels(path)


def confirmed(meta, answers, n):
    """The sense named with high confidence for item n, or None."""
    a = (answers or {}).get(n)
    return a[0] if a and a[1] == "high" else None


def cmd_judge(args):
    pre = prefix(args.kind)
    shortfix.require_complete(args.wave, pre)
    wdir = wave_dir(args.wave)
    world = qf.load_world()
    stats = collections.Counter()
    again = []
    for meta_path in sorted(glob.glob(os.path.join(wdir, pre + "-meta", "*.json"))):
        bid = os.path.splitext(os.path.basename(meta_path))[0]
        meta = qf.read_json(meta_path)
        answers = read_answers(os.path.join(wdir, pre + "-out", bid + ".tsv"))
        if answers is None:
            stats["batch without output"] += 1
        for n, item in meta["items"].items():
            stats["rows"] += 1
            if confirmed(meta, answers, n) == item["sense"]:
                stats["confirmed by the first labeler"] += 1
            else:
                again.append(dict((k, v) for k, v in item.items() if k != "group"))
    n, skipped = write_batches(world, again, wdir, pre + "2-in", pre + "2-meta", "j", f"chk2|{args.wave}")
    os.makedirs(os.path.join(wdir, pre + "2-out"), exist_ok=True)
    print(dict(stats), "->", len(again), "rows for the second labeler in", n, "batches, skipped", skipped)


def cmd_apply(args):
    pre = prefix(args.kind)
    shortfix.require_complete(args.wave, pre)
    shortfix.require_complete(args.wave, pre + "2")
    wdir = wave_dir(args.wave)
    world = qf.load_world()
    senses = world["senses"]
    first, second = {}, {}
    for meta_path in sorted(glob.glob(os.path.join(wdir, pre + "-meta", "*.json"))):
        bid = os.path.splitext(os.path.basename(meta_path))[0]
        meta = qf.read_json(meta_path)
        answers = read_answers(os.path.join(wdir, pre + "-out", bid + ".tsv")) or {}
        for n, item in meta["items"].items():
            first[(item["split"], item["lemma"], item["sense"], item["sentence"])] = confirmed(meta, answers, n)
    for meta_path in sorted(glob.glob(os.path.join(wdir, pre + "2-meta", "*.json"))):
        bid = os.path.splitext(os.path.basename(meta_path))[0]
        meta = qf.read_json(meta_path)
        answers = read_answers(os.path.join(wdir, pre + "2-out", bid + ".tsv")) or {}
        for n, item in meta["items"].items():
            second[(item["split"], item["lemma"], item["sense"], item["sentence"])] = confirmed(meta, answers, n)
    kind = args.kind
    decision = {}
    report = collections.defaultdict(collections.Counter)
    for key, a in first.items():
        split, lemma, sense, sentence = key
        c = report[lemma]
        c["rows"] += 1
        if a == sense:
            decision[key] = ("keep", sense)
            c["confirmed first"] += 1
            continue
        b = second.get(key)
        if b == sense:
            decision[key] = ("keep", sense)
            c["confirmed second"] += 1
        elif b and a == b and b in world["by_lemma"].get(lemma, []) and senses[b]["lemma"] == lemma:
            decision[key] = ("relabel", b)
            c["relabeled"] += 1
        else:
            decision[key] = ("remove", sense)
            c["removed"] += 1
    removed_prov = set()
    for split in SPLITS:
        path = corpus_path(split, args.wave, kind)
        if not os.path.exists(path):
            continue
        out_lines = []
        with open(path, encoding="utf-8") as handle:
            for line in handle:
                p = line.rstrip("\n").split("\t")
                if len(p) != 4:
                    out_lines.append(line.rstrip("\n"))
                    continue
                d = decision.get((split, p[0], p[1], p[3]))
                if d is None or d[0] == "keep":
                    out_lines.append(line.rstrip("\n"))
                elif d[0] == "relabel":
                    out_lines.append("\t".join([p[0], d[1], senses[d[1]]["pos_name"], p[3]]))
                else:
                    removed_prov.add(fill.prov_id(p[0], p[3]))
        fill.write_lines(path, out_lines)
    # choosers and provenance of removed rows
    removed_sentences = {(k[1], k[3]) for k, d in decision.items() if d[0] == "remove"}
    for sub in SPLITS:
        path = os.path.join(qf.CORPUS, "choosers", sub, f"quota-w{args.wave}-{kind}.tsv")
        if os.path.exists(path):
            with open(path, encoding="utf-8") as handle:
                lines = [l.rstrip("\n") for l in handle]
            fill.write_lines(path, [l for l in lines if tuple(l.split("\t")[1:3]) not in removed_sentences])
    ppath = os.path.join(qf.CORPUS, "provenance", f"quota-w{args.wave}-{kind}.tsv")
    if os.path.exists(ppath):
        with open(ppath, encoding="utf-8") as handle:
            lines = [l.rstrip("\n") for l in handle]
        header, rest = lines[0], lines[1:]
        fill.write_lines(ppath, [l for l in rest if l.split("\t")[0] not in removed_prov], header)
    cols = ["rows", "confirmed first", "confirmed second", "relabeled", "removed"]
    with open(os.path.join(wdir, f"chk-report-{kind}.tsv"), "w", encoding="utf-8", newline="\n") as handle:
        handle.write("lemma\t" + "\t".join(cols) + "\n")
        for lemma in sorted(report):
            handle.write(lemma + "\t" + "\t".join(str(report[lemma][c]) for c in cols) + "\n")
    total = collections.Counter()
    for c in report.values():
        total.update(c)
    print({c: total[c] for c in cols})


if __name__ == "__main__":
    ap = argparse.ArgumentParser()
    sub = ap.add_subparsers(dest="cmd", required=True)
    for name in ("export", "judge", "apply"):
        p = sub.add_parser(name)
        p.add_argument("wave", type=qf.wave_id)
        p.add_argument("--kind", choices=("real", "written", "shape"), default="real")
        if name == "export":
            p.add_argument("--every", type=int, default=1, help="check every Nth row (a sample)")
    a = ap.parse_args()
    {"export": cmd_export, "judge": cmd_judge, "apply": cmd_apply}[a.cmd](a)

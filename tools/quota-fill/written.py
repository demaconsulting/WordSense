"""The top-up by writing: export senses that are still short, validate the written sentences, make the blind-label batches.

  python written.py export WAVE     work/w<WAVE>/wr-in/*.json for the writer (gpt-5.6-luna), one batch = about 220 sentences
  python written.py ingest WAVE     validate work/w<WAVE>/wr-out/*.tsv and write the blind-label batches work/w<WAVE>/wr-label/
  python written.py labeled WAVE    read the blind labels (wr-label-out) and write wr-labeled.tsv
Then `python fill.py select WAVE --kind written` writes the corpus rows of the sentences that the labeler placed in the
intended sense.
"""
import argparse
import collections
import glob
import math
import os
import re
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import harvest
import lexindex
import qf
import textutil

BATCH_SENTENCES = 220
MIN_WORDS, MAX_WORDS = 6, 30


def wave_dir(wave):
    return os.path.join(qf.WORK, f"w{wave}")


def count_for(short):
    return max(6, min(45, math.ceil(1.5 * short) + 2))


def allowed_forms(world, lemma, pos):
    return sorted(f for f, pairs in world["index"].items() if (lemma, pos) in pairs and f.isalpha() and f.islower())


def cmd_export(args):
    world = qf.load_world()
    senses = world["senses"]
    status = lexindex.quota_status(senses, world["scope"])
    wanted = set(world["by_lemma"]) if args.tier else qf.lemmas_up_to(world, args.lemmas_up_to or args.wave)
    if args.skip_up_to:
        wanted -= qf.lemmas_up_to(world, args.skip_up_to) - set(qf.TECH_ALL)
    only = set(args.only_lemmas.split(",")) if args.only_lemmas else None
    tech = qf.read_tech_targets() if args.tech else {}
    if args.tech:
        # the written STE-style sentences complete the real FAA rows to the target, and are at least 8 per sense
        real_file = os.path.join(qf.CORPUS, "anchors", f"quota-w{args.wave}-real.tsv")
        have = collections.Counter()
        if os.path.exists(real_file):
            with open(real_file, encoding="utf-8") as handle:
                for line in handle:
                    parts = line.rstrip("\n").split("\t")
                    if len(parts) == 4:
                        have[parts[1]] += 1
        tech = {s: max(8, n - have[s]) for s, n in tech.items()}
    todo = []
    for r in status:
        s = senses[r["sense_id"]]
        if args.tier and r["tier"] != args.tier:
            continue
        if only is not None:
            if s["lemma"] not in only:
                continue
        elif s["lemma"] not in wanted:
            continue
        short = max(0, r["anchor_quota"] - r["anchors"]) + max(0, r["holdout_quota"] - r["holdout"])
        if args.tech:
            short = tech.get(r["sense_id"], 0)
        if short > 0:
            todo.append((r["sense_id"], short))
    wdir = wave_dir(args.wave)
    for sub in ("wr-in", "wr-out", "wr-meta", "wr-label"):
        os.makedirs(os.path.join(wdir, sub), exist_ok=True)
    batches, cur, total = [], [], 0
    for sid, short in sorted(todo):
        s = senses[sid]
        n = count_for(short)
        if total + n > BATCH_SENTENCES and cur:
            batches.append(cur)
            cur, total = [], 0
        forms = allowed_forms(world, s["lemma"], s["pos"])
        others = [qf.sense_line(world, o) for o in world["by_lemma"][s["lemma"]] if o != sid][:25]
        entry = {"sense": sid, "lemma": s["lemma"], "pos": s["pos_name"], "gloss": s["gloss"],
                 "examples": s["examples"][:3], "forms": forms[:14], "others": others, "count": n}
        if args.tech and sid in tech:
            entry["domain"] = "technical instructions (maintenance, engineering, operation of machines)"
        cur.append(entry)
        total += n
    if cur:
        batches.append(cur)
    for b, batch in enumerate(batches, 1):
        qf.write_json(os.path.join(wdir, "wr-in", f"w{args.wave}r{b:03d}.json"),
                      {"batch": f"w{args.wave}r{b:03d}", "senses": batch})
    print(len(todo), "senses,", sum(count_for(s) for _i, s in todo), "sentences,", len(batches), "writer batches")


def check_sentence(sentence, forms):
    """Returns the lower-case braced form when the sentence passes, else a reason string."""
    if sentence.count("{") != 1 or sentence.count("}") != 1:
        return "braces"
    m = re.search(r"\{([A-Za-z]+)\}", sentence)
    if not m:
        return "target is not one alphabetic word"
    form = m.group(1).lower()
    if form not in forms:
        return "form not allowed"
    plain = textutil.strip_braces(sentence)
    n = len(plain.split())
    if not MIN_WORDS <= n <= MAX_WORDS:
        return "length"
    if re.search(r"[^\x20-\x7e]", sentence):
        return "non-ASCII"
    if not re.search(r"""[.?!]["')]?$""", plain):
        return "no final punctuation"
    return form


def cmd_ingest(args):
    world = qf.load_world()
    senses = world["senses"]
    wdir = wave_dir(args.wave)
    blocked = qf.corpus_keys()
    seen_keys, sets_by_lemma = set(), collections.defaultdict(list)
    reasons = collections.Counter()
    items = []  # (sense, form, sentence)
    for path in sorted(glob.glob(os.path.join(wdir, "wr-in", "*.json"))):
        bid = os.path.splitext(os.path.basename(path))[0]
        spec = qf.read_json(path)
        out_path = os.path.join(wdir, "wr-out", bid + ".tsv")
        if not os.path.exists(out_path):
            reasons["batch without output"] += 1
            continue
        forms_of = {e["sense"]: set(allowed_forms(world, e["lemma"], senses[e["sense"]]["pos"]))
                    for e in spec["senses"]}
        with open(out_path, encoding="utf-8") as handle:
            for line in handle:
                line = line.rstrip("\r\n")
                if not line.strip():
                    continue
                parts = line.split("\t")
                if len(parts) != 2 or parts[0] not in forms_of:
                    reasons["bad line"] += 1
                    continue
                sense, sentence = parts[0], parts[1].strip()
                res = check_sentence(sentence, forms_of[sense])
                if res not in forms_of[sense]:
                    reasons[res] += 1
                    continue
                key = textutil.key_of(sentence)
                if key in blocked or key in seen_keys:
                    reasons["duplicate"] += 1
                    continue
                lemma = senses[sense]["lemma"]
                toks = textutil.jaccard_tokens(sentence)
                if any(textutil.jaccard(toks, t) >= 0.8 for t in sets_by_lemma[lemma]):
                    reasons["near duplicate"] += 1
                    continue
                seen_keys.add(key)
                sets_by_lemma[lemma].append(toks)
                items.append((sense, res, sentence))
                reasons["kept"] += 1
    print(dict(reasons))
    # blind-label batches: the group of an item is the sense list of its form (the intended sense is not shown)
    groups = collections.defaultdict(list)
    for sense, form, sentence in items:
        key = harvest.group_key(world, form)
        groups[key].append((sense, form, sentence))
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
    os.makedirs(os.path.join(wdir, "wr-label-out"), exist_ok=True)
    for b, batch in enumerate(batches, 1):
        bid = f"w{args.wave}l{b:03d}"
        groups_json, meta, n = [], {}, 0
        for gi, (key, sense_ids, part) in enumerate(batch, 1):
            its = []
            for sense, form, sentence in part:
                n += 1
                its.append({"n": n, "sentence": sentence})
                meta[str(n)] = {"sid": f"written:{sense}:{n}", "form": form, "doc": "written", "family": "written",
                                "sentence": sentence, "lemma": senses[sense]["lemma"], "group": gi, "intended": sense}
            groups_json.append({"senses": [qf.sense_line(world, x) for x in sense_ids], "items": its})
        qf.write_json(os.path.join(wdir, "wr-label", bid + ".json"), {"batch": bid, "groups": groups_json})
        qf.write_json(os.path.join(wdir, "wr-label", bid + ".meta.json"),
                      {"groups": [[list(p) for p in key] for key, _s, _p in batch], "items": meta})
    print(len(items), "sentences,", len(batches), "blind-label batches")


def cmd_labeled(args):
    world = qf.load_world()
    wdir = wave_dir(args.wave)
    stats = collections.Counter()
    rows = []
    for meta_path in sorted(glob.glob(os.path.join(wdir, "wr-label", "*.meta.json"))):
        bid = os.path.basename(meta_path)[:-len(".meta.json")]
        meta = qf.read_json(meta_path)
        out_path = os.path.join(wdir, "wr-label-out", bid + ".tsv")
        if not os.path.exists(out_path):
            stats["batch without output"] += 1
            continue
        allowed = []
        for pairs in meta["groups"]:
            ids = set()
            for lemma, pos in pairs:
                for sid in world["by_lemma"][lemma]:
                    if world["senses"][sid]["pos"] == pos:
                        ids.add(sid)
            allowed.append(ids)
        seen = set()
        with open(out_path, encoding="utf-8") as handle:
            for line in handle:
                parts = line.rstrip("\r\n").split("\t")
                if not parts[0].strip() or parts[0].strip() not in meta["items"]:
                    continue
                n = parts[0].strip()
                seen.add(n)
                item = meta["items"][n]
                stats["items"] += 1
                if len(parts) < 2 or parts[1].strip() == "unsure" or parts[1].strip() not in allowed[item["group"] - 1]:
                    stats["unsure"] += 1
                    continue
                sense, conf = parts[1].strip(), (parts[2].strip() if len(parts) > 2 else "low")
                stats["same" if sense == item["intended"] else "different"] += 1
                if sense == item["intended"] and conf == "high":
                    stats["same high"] += 1
                rows.append((item["sid"], item["form"], item["intended"], sense, conf, "written", "written",
                             item["sentence"]))
        stats["missing"] += len(meta["items"]) - len(seen)
    with open(os.path.join(wdir, "wr-labeled.tsv"), "w", encoding="utf-8", newline="\n") as handle:
        for r in rows:
            handle.write("\t".join(r) + "\n")
    print(dict(stats))


if __name__ == "__main__":
    ap = argparse.ArgumentParser()
    sub = ap.add_subparsers(dest="cmd", required=True)
    for name in ("export", "ingest", "labeled"):
        p = sub.add_parser(name)
        p.add_argument("wave", type=qf.wave_id)
        if name == "export":
            p.add_argument("--tier", default="", choices=("", "C", "U", "R"),
                           help="every lemma, but only the short senses of this tier (a tier wave, e.g. 4c)")
            p.add_argument("--lemmas-up-to", type=int, default=0,
                           help="include the lemmas of every wave up to this one (a later round of an earlier wave)")
            p.add_argument("--skip-up-to", type=int, default=0, help="leave out the lemmas of the waves up to this one")
            p.add_argument("--only-lemmas", default="", help="comma-separated lemmas that replace the wave set")
            p.add_argument("--tech", action="store_true", help="the technical senses of tech-senses.tsv (STE-style sentences)")
            p.add_argument("--retry", action="store_true", help="second round for senses that stayed short (another prompt)")
    a = ap.parse_args()
    if a.cmd == "export":
        qf.check_tier_wave(ap, a)
    {"export": cmd_export, "ingest": cmd_ingest, "labeled": cmd_labeled}[a.cmd](a)

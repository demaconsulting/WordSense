"""Harvest of chooser data for the forms that lack a lemma (the participles that are adjectives and the like).

  python chooser_harvest.py candidates WAVE [--per-form N]   batches work/w<WAVE>/ch-in/*.json (+ meta)
  python chooser_harvest.py select WAVE                      rows of choosers/{anchors,holdout}/quota-w<WAVE>-forms.tsv
                                                             and provenance/quota-w<WAVE>-forms.tsv

A chooser row is `form`, `label` (the lemma) and the sentence. A form is a target when its chooser rows lack a lemma
(see choosers.py status) and every missing lemma is lower case. The labeler (gpt-6.1-sol) chooses the lemma among all
lemmas of the form. All rows are targeted (found or written for one form).
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
import choosers
import fill
import harvest
import pool
import qf
import textutil

MAX_PER_LABEL = 12
DOC_CAP_FORM = 3


def wave_dir(wave):
    return os.path.join(qf.WORK, f"w{wave}")


def target_forms():
    fl, labels, missing, _c, _t = choosers.status()
    forms = {}
    for form, miss in missing.items():
        if all(not m[0].isupper() for m in miss):
            forms[form] = miss
    return forms, fl


def options_of(world, fl, form):
    """lemma -> (sorted pos letters, glosses)"""
    result = {}
    for lemma in sorted(fl[form]):
        pos = sorted({p for (l, p) in world["index"].get(form, ()) if l == lemma})
        glosses = []
        cands = [s for s in world["senses"].values() if s["lemma"] == lemma]
        cands.sort(key=lambda s: -s["tag_count"])
        for s in cands[:2]:
            glosses.append(f"({s['pos_name']}) {s['gloss'][:110]}")
        result[lemma] = (pos, glosses)
    return result


def cmd_candidates(args):
    world = qf.load_world()
    forms, fl = target_forms()
    wdir = wave_dir(args.wave)
    for sub in ("ch-in", "ch-out", "ch-meta"):
        os.makedirs(os.path.join(wdir, sub), exist_ok=True)
    rows, _docs = pool.load_pool()
    blocked = qf.corpus_keys()
    rows = [r for r in rows if textutil.key_of(r[3]) not in blocked]
    used = harvest.load_used(wdir)
    hits = harvest.scan_pool(rows, set(forms))
    selected = []
    for form in sorted(forms):
        if not hits.get(form):
            continue
        rng = random.Random(int(hashlib.sha1(("ch|" + form).encode()).hexdigest()[:12], 16))
        doc_counts = collections.Counter()
        for h in harvest.pick_for_form(rng, hits[form], rows, args.per_form, doc_counts, used):
            selected.append((form, h))
    groups = collections.defaultdict(list)
    for form, h in selected:
        groups[tuple(sorted(fl[form]))].append((form, h))
    batches, cur, cur_n = [], [], 0
    for key in sorted(groups):
        items = groups[key]
        pos = 0
        while pos < len(items):
            room = harvest.BATCH_ITEMS - cur_n
            if room <= 0:
                batches.append(cur)
                cur, cur_n, room = [], 0, harvest.BATCH_ITEMS
            part = items[pos:pos + room]
            cur.append((key, part))
            cur_n += len(part)
            pos += len(part)
    if cur:
        batches.append(cur)
    total = 0
    for b, batch in enumerate(batches, 1):
        bid = f"w{args.wave}c{b:03d}"
        gj, meta, n = [], {}, 0
        for gi, (key, part) in enumerate(batch, 1):
            opts = options_of(world, fl, part[0][0])
            items = []
            for form, (i, s, e) in part:
                n += 1
                sid, doc, family, text = rows[i]
                braced = text[:s] + "{" + text[s:e] + "}" + text[e:]
                items.append({"n": n, "sentence": braced})
                meta[str(n)] = {"sid": sid, "form": form, "doc": doc, "family": family, "sentence": braced, "group": gi}
            gj.append({"options": [{"lemma": l, "pos": ", ".join(v[0]), "glosses": v[1]} for l, v in opts.items()],
                       "items": items})
        qf.write_json(os.path.join(wdir, "ch-in", bid + ".json"), {"batch": bid, "groups": gj})
        qf.write_json(os.path.join(wdir, "ch-meta", bid + ".json"),
                      {"groups": [list(k) for k, _p in batch], "items": meta})
        total += n
    harvest.save_used(used)
    print(len(forms), "forms,", len(batches), "batches,", total, "items")


def cmd_select(args):
    wdir = wave_dir(args.wave)
    tag = f"quota-w{args.wave}-forms.tsv"
    for folder in (os.path.join("choosers", "anchors"), os.path.join("choosers", "holdout"), "provenance"):
        old = os.path.join(qf.CORPUS, folder, tag)
        if os.path.exists(old):
            os.remove(old)
    blocked = qf.corpus_keys()
    doc_splits = qf.real_documents_by_split()
    current = choosers.form_lemmas()
    cands = collections.defaultdict(list)
    stats = collections.Counter()
    for meta_path in sorted(glob.glob(os.path.join(wdir, "ch-meta", "*.json"))):
        bid = os.path.splitext(os.path.basename(meta_path))[0]
        meta = qf.read_json(meta_path)
        out = os.path.join(wdir, "ch-out", bid + ".tsv")
        if not os.path.exists(out):
            stats["batch without output"] += 1
            continue
        with open(out, encoding="utf-8") as handle:
            for line in handle:
                p = line.rstrip("\r\n").split("\t")
                if p[0].strip() not in meta["items"]:
                    continue
                item = meta["items"][p[0].strip()]
                stats["items"] += 1
                if len(p) < 3 or p[1] == "unsure":
                    stats["unsure"] += 1
                    continue
                lemma, conf = p[1].strip(), p[2].strip()
                if lemma not in meta["groups"][item["group"] - 1] or conf != "high":
                    stats["dropped"] += 1
                    continue
                if lemma not in current.get(item["form"], ()):
                    stats["lemma is no longer a lemma of the form"] += 1
                    continue
                if textutil.key_of(item["sentence"]) in blocked:
                    continue
                stats["labeled high"] += 1
                item["role"] = qf.doc_role(qf.prov_document(item["sid"], item["doc"], item["family"]), doc_splits)
                if item["role"] is None:
                    stats["document has the other split"] += 1
                    continue
                cands[(item["form"], lemma)].append(item)
    out_rows = {"anchors": [], "holdout": [], "prov": []}
    for (form, lemma), items in sorted(cands.items()):
        rng = random.Random(int(hashlib.sha1((form + "|" + lemma).encode()).hexdigest()[:12], 16))
        rng.shuffle(items)
        doc_counts, chosen = collections.Counter(), []
        # evaluation documents give held-out rows (up to a quarter), the other documents anchors
        limit = {"eval": max(1, MAX_PER_LABEL // 4), "train": MAX_PER_LABEL - MAX_PER_LABEL // 4}
        taken = collections.Counter()
        for it in items:
            if taken[it["role"]] >= limit[it["role"]]:
                continue
            cap = 10 ** 6 if it["doc"] == "tatoeba-cc0" else DOC_CAP_FORM
            if doc_counts[it["doc"]] >= cap:
                continue
            doc_counts[it["doc"]] += 1
            taken[it["role"]] += 1
            chosen.append(it)
        for it in chosen:
            split = "holdout" if it["role"] == "eval" else "anchors"
            out_rows[split].append("\t".join([form, lemma, it["sentence"]]))
            src = fill.provenance_fields(it["sid"], it["family"])
            out_rows["prov"].append("\t".join([fill.prov_id(form, it["sentence"]), split, *src]))
        stats[f"rows {lemma}"] += 0
    fill.write_lines(os.path.join(qf.CORPUS, "choosers", "anchors", tag), sorted(out_rows["anchors"]))
    fill.write_lines(os.path.join(qf.CORPUS, "choosers", "holdout", tag), sorted(out_rows["holdout"]))
    fill.write_lines(os.path.join(qf.CORPUS, "provenance", tag), out_rows["prov"], "\t".join(fill.PROV_COLUMNS))
    print(dict((k, v) for k, v in stats.items() if not k.startswith("rows")), len(out_rows["anchors"]),
          len(out_rows["holdout"]))


def cmd_write_export(args):
    """Writer batches (gpt-5.6-luna) for the forms that still lack a lemma after the harvest."""
    world = qf.load_world()
    forms, fl = target_forms()
    wdir = wave_dir(args.wave)
    for sub in ("chw-in", "chw-out", "chw-label", "chw-label-out"):
        os.makedirs(os.path.join(wdir, sub), exist_ok=True)
    _f, _l, missing, _c, _t = choosers.status()
    entries = []
    for form in sorted(forms):
        for lemma in missing.get(form, ()):
            if lemma[0].isupper():
                continue
            opts = options_of(world, fl, form)
            entries.append({"form": form, "target_lemma": lemma, "target_pos": ", ".join(opts[lemma][0]),
                            "target_glosses": opts[lemma][1],
                            "other_lemmas": [{"lemma": l, "pos": ", ".join(v[0]), "glosses": v[1]}
                                             for l, v in opts.items() if l != lemma],
                            "count": args.count})
    for b in range(0, len(entries), 28):
        bid = f"w{args.wave}w{b // 28 + 1:03d}"
        qf.write_json(os.path.join(wdir, "chw-in", bid + ".json"), {"batch": bid, "forms": entries[b:b + 28]})
    print(len(entries), "entries,", (len(entries) + 27) // 28, "batches")


def cmd_write_ingest(args):
    """Validates the written sentences and makes the lemma-choice batches (the intended lemma is not shown)."""
    world = qf.load_world()
    _forms, fl = target_forms()
    wdir = wave_dir(args.wave)
    blocked = qf.corpus_keys()
    seen, items, stats = set(), [], collections.Counter()
    for path in sorted(glob.glob(os.path.join(wdir, "chw-in", "*.json"))):
        bid = os.path.splitext(os.path.basename(path))[0]
        spec = qf.read_json(path)
        out = os.path.join(wdir, "chw-out", bid + ".tsv")
        if not os.path.exists(out):
            stats["batch without output"] += 1
            continue
        target = {e["form"]: e["target_lemma"] for e in spec["forms"]}
        with open(out, encoding="utf-8") as handle:
            for line in handle:
                p = line.rstrip("\r\n").split("\t")
                if len(p) != 2 or p[0] not in target:
                    stats["bad line"] += 1
                    continue
                form, sentence = p[0], p[1].strip()
                m = re.search(r"\{([A-Za-z]+)\}", sentence)
                plain = textutil.strip_braces(sentence)
                if (sentence.count("{") != 1 or sentence.count("}") != 1 or not m or m.group(1).lower() != form
                        or not 6 <= len(plain.split()) <= 30 or re.search(r"[^\x20-\x7e]", sentence)):
                    stats["invalid"] += 1
                    continue
                key = textutil.key_of(sentence)
                if key in blocked or key in seen:
                    stats["duplicate"] += 1
                    continue
                seen.add(key)
                items.append((form, target[form], sentence))
    groups = collections.defaultdict(list)
    for item in items:
        groups[tuple(sorted(fl[item[0]]))].append(item)
    batches, cur, cur_n = [], [], 0
    for key in sorted(groups):
        part_items = groups[key]
        pos = 0
        while pos < len(part_items):
            room = harvest.BATCH_ITEMS - cur_n
            if room <= 0:
                batches.append(cur)
                cur, cur_n, room = [], 0, harvest.BATCH_ITEMS
            part = part_items[pos:pos + room]
            cur.append((key, part))
            cur_n += len(part)
            pos += len(part)
    if cur:
        batches.append(cur)
    for b, batch in enumerate(batches, 1):
        bid = f"w{args.wave}x{b:03d}"
        gj, meta, n = [], {}, 0
        for gi, (key, part) in enumerate(batch, 1):
            opts = options_of(world, fl, part[0][0])
            its = []
            for form, lemma, sentence in part:
                n += 1
                its.append({"n": n, "sentence": sentence})
                meta[str(n)] = {"sid": f"written:{form}:{n}", "form": form, "doc": "written", "family": "written",
                                "sentence": sentence, "group": gi, "intended": lemma}
            gj.append({"options": [{"lemma": l, "pos": ", ".join(v[0]), "glosses": v[1]} for l, v in opts.items()],
                       "items": its})
        qf.write_json(os.path.join(wdir, "chw-label", bid + ".json"), {"batch": bid, "groups": gj})
        qf.write_json(os.path.join(wdir, "chw-label", bid + ".meta.json"),
                      {"groups": [list(k) for k, _p in batch], "items": meta})
    print(dict(stats), len(items), "sentences,", len(batches), "lemma-choice batches")


def cmd_write_select(args):
    wdir = wave_dir(args.wave)
    tag = f"quota-w{args.wave}-forms-written.tsv"
    for folder in (os.path.join("choosers", "anchors"), os.path.join("choosers", "holdout"), "provenance"):
        old = os.path.join(qf.CORPUS, folder, tag)
        if os.path.exists(old):
            os.remove(old)
    blocked = qf.corpus_keys()
    current = choosers.form_lemmas()
    cands = collections.defaultdict(list)
    stats = collections.Counter()
    for meta_path in sorted(glob.glob(os.path.join(wdir, "chw-label", "*.meta.json"))):
        bid = os.path.basename(meta_path)[:-len(".meta.json")]
        meta = qf.read_json(meta_path)
        out = os.path.join(wdir, "chw-label-out", bid + ".tsv")
        if not os.path.exists(out):
            stats["batch without output"] += 1
            continue
        with open(out, encoding="utf-8") as handle:
            for line in handle:
                p = line.rstrip("\r\n").split("\t")
                if p[0].strip() not in meta["items"]:
                    continue
                item = meta["items"][p[0].strip()]
                stats["items"] += 1
                if len(p) < 3 or p[1] != item["intended"] or p[2].strip() != "high":
                    stats["not confirmed"] += 1
                    continue
                if textutil.key_of(item["sentence"]) in blocked:
                    continue
                if item["intended"] not in current.get(item["form"], ()):
                    stats["lemma is no longer a lemma of the form"] += 1
                    continue
                stats["confirmed"] += 1
                cands[(item["form"], item["intended"])].append(item)
    out_rows = {"anchors": [], "holdout": [], "prov": []}
    for (form, lemma), its in sorted(cands.items()):
        n = len(its)
        h = max(1, round(n * 0.25)) if n >= 4 else 0
        hold = fill.spaced(n, h)
        for i, it in enumerate(its):
            split = "holdout" if i in hold else "anchors"
            out_rows[split].append("\t".join([form, lemma, it["sentence"]]))
            src = (fill.WRITTEN_SOURCE, f"quota-w{args.wave}-forms-written")
            out_rows["prov"].append("\t".join([fill.prov_id(form, it["sentence"]), split, *src]))
    fill.write_lines(os.path.join(qf.CORPUS, "choosers", "anchors", tag), sorted(out_rows["anchors"]))
    fill.write_lines(os.path.join(qf.CORPUS, "choosers", "holdout", tag), sorted(out_rows["holdout"]))
    fill.write_lines(os.path.join(qf.CORPUS, "provenance", tag), out_rows["prov"], "\t".join(fill.PROV_COLUMNS))
    print(dict(stats), len(out_rows["anchors"]), len(out_rows["holdout"]))


if __name__ == "__main__":
    ap = argparse.ArgumentParser()
    sub = ap.add_subparsers(dest="cmd", required=True)
    c = sub.add_parser("candidates")
    c.add_argument("wave", type=int)
    c.add_argument("--per-form", type=int, default=40)
    s = sub.add_parser("select")
    s.add_argument("wave", type=int)
    w1 = sub.add_parser("write-export")
    w1.add_argument("wave", type=int)
    w1.add_argument("--count", type=int, default=8)
    w2 = sub.add_parser("write-ingest")
    w2.add_argument("wave", type=int)
    w3 = sub.add_parser("write-select")
    w3.add_argument("wave", type=int)
    a = ap.parse_args()
    {"candidates": cmd_candidates, "select": cmd_select, "write-export": cmd_write_export,
     "write-ingest": cmd_write_ingest, "write-select": cmd_write_select}[a.cmd](a)

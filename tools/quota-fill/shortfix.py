"""Short answers of the labeling agents: find the items that an answer file lacks and label only those.

  python shortfix.py check WAVE STAGE    lists the batches whose answer file has fewer lines than the batch has items
  python shortfix.py export WAVE STAGE   writes <in>-fix/<batch>.json with only the missing items (their numbers kept)
  python shortfix.py merge WAVE STAGE    appends the answers in <out>-fix/<batch>.tsv to the answer files, then checks again

STAGE is one of real-sol, real-sonnet (the two labelers of the harvest batches), chk, chk2 (the first and the second blind
check of real rows, `blindcheck.py`), wchk, wchk2 (the same for written rows) and wr-label (the blind labels of the written
sentences). `fill.py agree`, `blindcheck.py judge` and `apply` and `written.py labeled` refuse to run while an answer is
short. A line count that equals the item count is the rule: a labeler that stops early is common for long batches, and the
fix batch is short.
"""
import argparse
import glob
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import qf

# stage -> (input folder, meta folder or None for <batch>.meta.json beside the input, answer folder)
STAGES = {
    "real-sol": ("in", "meta", "out-sol"),
    "real-sonnet": ("in", "meta", "out-sonnet"),
    "chk": ("chk-in", "chk-meta", "chk-out"),
    "chk2": ("chk2-in", "chk2-meta", "chk2-out"),
    "wchk": ("wchk-in", "wchk-meta", "wchk-out"),
    "wchk2": ("wchk2-in", "wchk2-meta", "wchk2-out"),
    "wr-label": ("wr-label", None, "wr-label-out"),
}


def wave_dir(wave):
    return os.path.join(qf.WORK, f"w{wave}")


def metas(wave, stage):
    """(batch id, set of item numbers) of every batch of the stage."""
    in_dir, meta_dir, _out = STAGES[stage]
    wdir = wave_dir(wave)
    if meta_dir is None:
        paths = sorted(glob.glob(os.path.join(wdir, in_dir, "*.meta.json")))
        names = [(os.path.basename(p)[:-len(".meta.json")], p) for p in paths]
    else:
        paths = sorted(glob.glob(os.path.join(wdir, meta_dir, "*.json")))
        names = [(os.path.splitext(os.path.basename(p))[0], p) for p in paths]
    for bid, path in names:
        yield bid, set(qf.read_json(path)["items"])


def answered(path):
    """Item numbers (text) that an answer file has a line for."""
    result = set()
    if os.path.exists(path):
        with open(path, encoding="utf-8") as handle:
            for line in handle:
                n = line.split("\t", 1)[0].strip()
                if n:
                    result.add(n)
    return result


def short_batches(wave, stage):
    """batch id -> sorted missing item numbers (ints) for every batch whose answer lacks items."""
    out_dir = os.path.join(wave_dir(wave), STAGES[stage][2])
    missing = {}
    for bid, items in metas(wave, stage):
        gap = items - answered(os.path.join(out_dir, bid + ".tsv"))
        if gap:
            missing[bid] = sorted(int(n) for n in gap)
    return missing


def require_complete(wave, stage):
    """Exits with the list of short batches when an answer is short."""
    missing = short_batches(wave, stage)
    if missing:
        sys.exit(f"{stage}: {len(missing)} batches have short answers (python shortfix.py export {wave} {stage}): "
                 + ", ".join(f"{b}({len(m)})" for b, m in sorted(missing.items())[:20]))


def cmd_check(args):
    missing = short_batches(args.wave, args.stage)
    print(args.stage, len(missing), "short batches", {b: len(m) for b, m in missing.items()})


def cmd_export(args):
    in_dir, _meta, out_dir = STAGES[args.stage]
    wdir = wave_dir(args.wave)
    os.makedirs(os.path.join(wdir, out_dir + "-fix"), exist_ok=True)
    missing = short_batches(args.wave, args.stage)
    for bid, nums in missing.items():
        spec = qf.read_json(os.path.join(wdir, in_dir, bid + ".json"))
        wanted = set(nums)
        groups = []
        for group in spec["groups"]:
            items = [i for i in group["items"] if i["n"] in wanted]
            if items:
                groups.append({**group, "items": items})
        qf.write_json(os.path.join(wdir, in_dir + "-fix", bid + ".json"), {"batch": bid, "groups": groups})
    print(len(missing), "fix batches in", in_dir + "-fix:", " ".join(f"{b}({len(m)})" for b, m in sorted(missing.items())))


def cmd_merge(args):
    _in, _meta, out_dir = STAGES[args.stage]
    wdir = wave_dir(args.wave)
    merged = 0
    for path in sorted(glob.glob(os.path.join(wdir, out_dir + "-fix", "*.tsv"))):
        bid = os.path.splitext(os.path.basename(path))[0]
        target = os.path.join(wdir, out_dir, bid + ".tsv")
        have = answered(target)
        with open(path, encoding="utf-8") as src:
            lines = [l.rstrip("\r\n") for l in src if l.strip() and l.split("\t", 1)[0].strip() not in have]
        with open(target, "a", encoding="utf-8", newline="\n") as dst:
            for line in lines:
                dst.write(line + "\n")
        merged += len(lines)
        os.remove(path)
    print(merged, "lines merged;", end=" ")
    cmd_check(args)


if __name__ == "__main__":
    ap = argparse.ArgumentParser()
    ap.add_argument("cmd", choices=("check", "export", "merge"))
    ap.add_argument("wave", type=qf.wave_id)
    ap.add_argument("stage", choices=sorted(STAGES))
    a = ap.parse_args()
    {"check": cmd_check, "export": cmd_export, "merge": cmd_merge}[a.cmd](a)

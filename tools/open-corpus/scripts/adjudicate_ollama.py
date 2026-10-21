#!/usr/bin/env python3
"""EXPERIMENT ONLY (superseded): the final pipeline has no adjudicator (see quality.md).

Blind judge on a local Ollama server (Python standard library only): same task as the sub-agent judge.

  adjudicate_ollama.py --workdir DIR --sentences SENTENCES.tsv --judge NAME --model MODEL [--host URL]

Reads DIR/adjudication/key.tsv (written by `adjudicate.py prepare`; the items and the anonymous A/B order are the same
for every judge). For each item the judge sees the guideline (system prompt), the tokenized sentence, the target token
and two anonymous candidate tags (Option A / Option B, no indication of origin) and answers A, B or U through a JSON
schema. 10 items per request, 4 requests in flight, temperature 0, fixed seed, thinking off. Missing answers are asked
once more; anything still missing is recorded as U.
Writes DIR/adjudication/out_NAME/answers.tsv (item TAB choice), DIR/adjudication/out_NAME/log.jsonl and meta.json.
"""
import argparse
import concurrent.futures
import json
import os
import sys
import time
import urllib.request

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from common import ROOT, read_tsv, write_json

SEED = 42
SCHEMA = {"type": "object", "properties": {"answers": {"type": "array", "items": {
    "type": "object", "properties": {"item": {"type": "string"}, "choice": {"type": "string", "enum": ["A", "B", "U"]}},
    "required": ["item", "choice"]}}}, "required": ["answers"]}


def system_prompt():
    guideline = open(os.path.join(ROOT, "GUIDELINE.md"), encoding="utf-8").read()
    return ("You are an impartial judge of part-of-speech tags. For each item you get a tokenized sentence (idx:token), one "
            "target token and two candidate UPOS tags, Option A and Option B. Decide which option the annotation guideline below "
            "supports for that token in that sentence. Answer A or B. Answer U only if the guideline and the sentence genuinely cannot "
            "decide between them (an unresolvable ambiguity); do not use U just because the choice is difficult. Judge only the target token. "
            'Answer only with JSON: {"answers":[{"item":"A0001","choice":"A"}]} with one entry per item, in order.\n\n'
            "=== ANNOTATION GUIDELINE ===\n" + guideline)


def render(items, sentences):
    out = ["Judge these items."]
    for it in items:
        toks = sentences[it["uid"]]
        a, b = (it["primary_upos"], it["second_upos"]) if it["a_is"] == "primary" else (it["second_upos"], it["primary_upos"])
        out.append(f"### item {it['item']}\nSentence: {' '.join(f'{k}:{t}' for k, t in enumerate(toks))}\n"
                   f"Target token: {it['idx']} {toks[int(it['idx'])]!r}\nOption A: {a}\nOption B: {b}\n")
    return "\n".join(out)


def ask(host, model, system, user, num_ctx):
    payload = {"model": model, "stream": False, "think": False, "format": SCHEMA, "keep_alive": "30m",
               "options": {"temperature": 0, "seed": SEED, "num_ctx": num_ctx, "num_predict": 2048},
               "messages": [{"role": "system", "content": system}, {"role": "user", "content": user}]}
    req = urllib.request.Request(host + "/api/chat", data=json.dumps(payload).encode("utf-8"), headers={"Content-Type": "application/json"})
    t0 = time.time()
    with urllib.request.urlopen(req, timeout=1800) as r:
        resp = json.loads(r.read().decode("utf-8"))
    return resp["message"]["content"], round(time.time() - t0, 1)


def parse(content, items):
    try:
        got = {a["item"]: a["choice"] for a in json.loads(content)["answers"] if a.get("choice") in ("A", "B", "U")}
    except Exception:
        got = {}
    return {it["item"]: got[it["item"]] for it in items if it["item"] in got}


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--workdir", required=True)
    ap.add_argument("--sentences", required=True)
    ap.add_argument("--judge", required=True)
    ap.add_argument("--model", required=True)
    ap.add_argument("--host", default="http://localhost:11434")
    ap.add_argument("--per-request", type=int, default=10)
    ap.add_argument("--concurrency", type=int, default=4)
    ap.add_argument("--num-ctx", type=int, default=16384)
    args = ap.parse_args()
    base = os.path.join(args.workdir, "adjudication")
    out_dir = os.path.join(base, f"out_{args.judge}")
    os.makedirs(out_dir, exist_ok=True)
    key = read_tsv(os.path.join(base, "key.tsv"))
    sentences = {r["uid"]: r["tokens"].split(" ") for r in read_tsv(args.sentences)}
    key = [k for k in key if k["uid"] in sentences]
    system = system_prompt()
    chunks = [key[i:i + args.per_request] for i in range(0, len(key), args.per_request)]
    t0 = time.time()

    def work(chunk):
        log = []
        content, secs = ask(args.host, args.model, system, render(chunk, sentences), args.num_ctx)
        got = parse(content, chunk)
        log.append({"items": [c["item"] for c in chunk], "attempt": 1, "seconds": secs, "response": content})
        missing = [c for c in chunk if c["item"] not in got]
        if missing:
            content, secs = ask(args.host, args.model, system, render(missing, sentences), args.num_ctx)
            got.update(parse(content, missing))
            log.append({"items": [c["item"] for c in missing], "attempt": 2, "seconds": secs, "response": content})
        return got, log

    answers, logs = {}, []
    with concurrent.futures.ThreadPoolExecutor(max_workers=args.concurrency) as ex:
        for i, fut in enumerate([ex.submit(work, c) for c in chunks]):
            got, log = fut.result()
            answers.update(got)
            logs.extend(log)
            print(f"chunk {i + 1}/{len(chunks)} answered {len(got)}", flush=True)
    still_missing = [k["item"] for k in key if k["item"] not in answers]
    with open(os.path.join(out_dir, "answers.tsv"), "w", encoding="utf-8", newline="\n") as f:
        for k in key:
            f.write(f"{k['item']}\t{answers.get(k['item'], 'U')}\n")
    with open(os.path.join(out_dir, "log.jsonl"), "w", encoding="utf-8", newline="\n") as f:
        for e in logs:
            f.write(json.dumps(e, ensure_ascii=False) + "\n")
    write_json(os.path.join(out_dir, "meta.json"), {"judge": args.judge, "model": args.model, "host": args.host, "seed": SEED, "temperature": 0,
                                                    "think": False, "num_ctx": args.num_ctx, "items": len(key), "requests": len(logs),
                                                    "missing_recorded_as_U": still_missing, "wall_seconds": round(time.time() - t0, 1)})
    print(f"done: {len(key)} items, missing {len(still_missing)}, {round(time.time() - t0)}s")


if __name__ == "__main__":
    main()

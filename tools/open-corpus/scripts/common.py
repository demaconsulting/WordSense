"""Shared helpers for the open English POS corpus pilot (Python standard library only).

Everything here is deterministic: no clock, no network, no unseeded randomness.
"""
import hashlib
import json
import os
import re

# The scripts live in tools/open-corpus/scripts; the corpus (data, documentation, build records) is corpus/open and the
# git-ignored downloads are tools/open-corpus/cache.
TOOL_ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
REPOSITORY = os.path.dirname(os.path.dirname(TOOL_ROOT))
ROOT = os.path.join(REPOSITORY, "corpus", "open")
CACHE = os.path.join(TOOL_ROOT, "cache")
BUILD = os.path.join(ROOT, "build")
REPRESENTATIVE = os.path.join(ROOT, "representative")
DATA = os.path.join(REPRESENTATIVE, "data")
TARGETED = os.path.join(ROOT, "targeted")
TARGETED_DATA = os.path.join(TARGETED, "data")
SOURCES = os.path.join(ROOT, "sources")
PROMPTS = os.path.join(ROOT, "prompts")

# Recorded sampling seed (the date the pilot was started). Every random choice derives from it.
SEED = 20261019

UPOS = ["ADJ", "ADP", "ADV", "AUX", "CCONJ", "DET", "INTJ", "NOUN", "NUM", "PART", "PRON", "PROPN", "PUNCT", "SCONJ",
        "SYM", "VERB", "X"]
COLLAPSE = {"NOUN": "N", "PROPN": "N", "PRON": "R", "VERB": "V", "ADJ": "J", "ADV": "B", "ADP": "P", "CCONJ": "C",
            "SCONJ": "C", "INTJ": "I", "DET": "D", "AUX": "A", "PART": "T", "NUM": "U", "PUNCT": "O", "SYM": "O",
            "X": "O"}

# Feature names, their allowed values and the UPOS tags that may carry them (see GUIDELINE.md).
FEATURE_VALUES = {
    "Number": {"Sing", "Plur"},
    "Tense": {"Past", "Pres"},
    "VerbForm": {"Fin", "Inf", "Part", "Ger"},
    "Person": {"1", "2", "3"},
    "Mood": {"Ind", "Imp", "Sub"},
    "Degree": {"Pos", "Cmp", "Sup"},
    "PronType": {"Prs", "Dem", "Int", "Rel", "Art", "Ind", "Neg", "Tot"},
    "Poss": {"Yes"},
}
FEATURES_FOR_UPOS = {
    "NOUN": {"Number"},
    "PROPN": {"Number"},
    "PRON": {"Number", "Person", "PronType", "Poss"},
    "DET": {"Number", "PronType", "Poss"},
    "VERB": {"VerbForm", "Tense", "Person", "Number", "Mood"},
    "AUX": {"VerbForm", "Tense", "Person", "Number", "Mood"},
    "ADJ": {"Degree"},
    "ADV": {"Degree", "PronType"},
}


def sha256_bytes(data):
    """Return the SHA-256 of a bytes object as a lower-case hex string."""
    return hashlib.sha256(data).hexdigest()


def sha256_text(text):
    """Return the SHA-256 of a string (as UTF-8) as a lower-case hex string."""
    return sha256_bytes(text.encode("utf-8"))


def sha256_file(path):
    """Return the SHA-256 of a file, read in blocks of 1 MiB, as a lower-case hex string."""
    h = hashlib.sha256()
    with open(path, "rb") as f:
        for block in iter(lambda: f.read(1 << 20), b""):
            h.update(block)
    return h.hexdigest()


def read_tsv(path):
    """Read a tab separated file with a header row into a list of dicts."""
    with open(path, encoding="utf-8", newline="") as f:
        lines = [l.rstrip("\r\n") for l in f if l.strip()]
    header = lines[0].split("\t")
    return [dict(zip(header, l.split("\t"))) for l in lines[1:]]


def write_tsv(path, rows, columns):
    """Write dicts as a tab separated file: a header row, then one line per row with the given columns.

    The directory of the file is created. A tab or a line feed inside a value is replaced by a space, and a missing
    column is written empty. The file is UTF-8 with LF line endings.
    """
    os.makedirs(os.path.dirname(path), exist_ok=True)
    with open(path, "w", encoding="utf-8", newline="\n") as f:
        f.write("\t".join(columns) + "\n")
        for r in rows:
            f.write("\t".join(str(r.get(c, "")).replace("\t", " ").replace("\n", " ") for c in columns) + "\n")


def read_json(path):
    """Read a UTF-8 JSON file and return its content."""
    with open(path, encoding="utf-8") as f:
        return json.load(f)


def write_json(path, obj):
    """Write an object as UTF-8 JSON with sorted keys, one-space indent and LF line endings; the directory is created."""
    os.makedirs(os.path.dirname(path), exist_ok=True)
    with open(path, "w", encoding="utf-8", newline="\n") as f:
        json.dump(obj, f, ensure_ascii=False, indent=1, sort_keys=True)
        f.write("\n")


# ---------------------------------------------------------------- text normalization and tokenization

def normalize_text(text):
    """Normalize typography so that tokenization and annotation see plain ASCII quotes and dashes."""
    text = (text.replace("\u2018", "'").replace("\u2019", "'").replace("\u201c", '"').replace("\u201d", '"')
            .replace("\u00a0", " ").replace("\u2014", "--").replace("\u2013", "--").replace("\u2026", "..."))
    return re.sub(r"\s+", " ", text).strip()


ABBREVIATIONS = {"mr.", "mrs.", "ms.", "dr.", "st.", "mt.", "prof.", "gen.", "col.", "capt.", "lieut.", "rev.", "hon.",
                 "jr.", "sr.", "no.", "vs.", "etc.", "e.g.", "i.e.", "a.m.", "p.m.", "messrs.", "gov.", "sgt.", "lt.",
                 "cf.", "inc.", "co.", "ltd.", "ft.", "approx."}
OPENERS = set("\"'([{<`$#")
CLOSERS = set(".,;:!?)]}>\"'%")
IRREGULAR_NT = {"can't": ("ca", "n't"), "won't": ("wo", "n't"), "shan't": ("sha", "n't"), "ain't": ("ai", "n't"),
                "cannot": ("can", "not")}
LEADING_APOSTROPHE_WORDS = {"'tis", "'twas", "'em", "'cause", "'til", "'bout", "'n'", "'twere"}
CLITIC_RE = re.compile(r"^([A-Za-z][A-Za-z']*)('(?:s|re|ve|ll|d|m))$", re.IGNORECASE)
NT_RE = re.compile(r"^([A-Za-z]+)(n't)$", re.IGNORECASE)
INITIALS_RE = re.compile(r"^(?:[A-Za-z]\.){1,}$")


def _split_body(body):
    """Split a punctuation-free chunk into tokens (dashes and clitics)."""
    out = []
    for piece in re.split(r"(--+)", body):
        if not piece:
            continue
        if piece.startswith("--"):
            out.append(piece)
            continue
        low = piece.lower()
        if low in IRREGULAR_NT:
            a, b = IRREGULAR_NT[low]
            out.extend([piece[:len(a)], piece[len(a):]])
            continue
        m = NT_RE.match(piece)
        if m:
            out.extend([m.group(1), m.group(2)])
            continue
        m = CLITIC_RE.match(piece)
        if m:
            out.extend([m.group(1), m.group(2)])
            continue
        out.append(piece)
    return out


def tokenize(text):
    """Rule-based tokenizer (documented in README.md). The tokens always concatenate back to the text minus spaces."""
    tokens = []
    for chunk in text.split():
        lead = []
        while chunk and chunk[0] in OPENERS:
            if chunk[0] == "'" and chunk.lower().rstrip(".,;:!?\"')") in LEADING_APOSTROPHE_WORDS:
                break
            lead.append(chunk[0])
            chunk = chunk[1:]
        trail = []
        while chunk:
            if chunk.endswith("...") and len(chunk) > 3:
                trail.append("...")
                chunk = chunk[:-3]
            elif chunk[-1] in CLOSERS:
                if chunk[-1] == "." and (chunk.lower() in ABBREVIATIONS or INITIALS_RE.match(chunk)):
                    break
                trail.append(chunk[-1])
                chunk = chunk[:-1]
            else:
                break
        tokens.extend(lead)
        if chunk:
            tokens.extend(_split_body(chunk))
        tokens.extend(reversed(trail))
    if tokens and re.fullmatch(r"[A-Za-z]\.", tokens[-1]) and len(tokens) > 1:
        tokens[-1:] = [tokens[-1][0], "."]
    return tokens


def check_tokenization(text, tokens):
    """Return True when the tokens, joined without spaces, are the text without its spaces (no character lost or invented)."""
    return "".join(tokens) == text.replace(" ", "")


# ---------------------------------------------------------------- sentence splitting

SENT_END_RE = re.compile(r"""([.?!]+["')\]]*)\s+(?=["'(\[]*[A-Z0-9])""")


def split_sentences(paragraph):
    """Conservative regex sentence splitter: never splits after known abbreviations or single capital initials."""
    parts = []
    start = 0
    for m in SENT_END_RE.finditer(paragraph):
        candidate = paragraph[start:m.end()].rstrip()
        last = candidate.split()[-1].lower().strip("\"'([")
        if last in ABBREVIATIONS or re.fullmatch(r"[a-z]\.", last) or re.fullmatch(r"(?:[a-z]\.){2,}", last):
            continue
        parts.append(candidate)
        start = m.end()
    tail = paragraph[start:].strip()
    if tail:
        parts.append(tail)
    return parts

"""Text helpers of the quota filling: normalization, sentence splitting, the corpus duplicate key (standard library only).

The sentence splitter and the sentence filter follow the ones of the open English POS corpus harvest (conservative,
abbreviation-aware, deterministic).
"""
import re

ABBREVIATIONS = {"mr.", "mrs.", "ms.", "dr.", "st.", "mt.", "prof.", "gen.", "col.", "capt.", "lieut.", "rev.", "hon.",
                 "jr.", "sr.", "no.", "vs.", "etc.", "e.g.", "i.e.", "a.m.", "p.m.", "messrs.", "gov.", "sgt.", "lt.",
                 "cf.", "inc.", "co.", "ltd.", "ft.", "approx."}
LIGATURES = str.maketrans({"\ufb01": "fi", "\ufb02": "fl", "\ufb00": "ff", "\ufb03": "ffi", "\ufb04": "ffl"})
SENT_END_RE = re.compile(r"""([.?!]+["')\]]*)\s+(?=["'(\[]*[A-Z0-9])""")
BAD_CHARS = re.compile(r"[\ufffd_\[\]{}|*<>=~^\\@#\x00-\x1f]")
CAPTION = re.compile(r"^\W*(Figure|Fig\.|Table|Chapter|Source|See|Note|Photo|Credit|Image)\b", re.I)
WORD_RE = re.compile(r"[A-Za-z]+")


def normalize_text(text):
    """Plain ASCII quotes and dashes, single spaces."""
    text = (text.replace("\u2018", "'").replace("\u2019", "'").replace("\u201c", '"').replace("\u201d", '"')
            .replace("\u00a0", " ").replace("\u2014", "--").replace("\u2013", "--").replace("\u2026", "..."))
    return re.sub(r"\s+", " ", text).strip()


def split_sentences(paragraph):
    parts, start = [], 0
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


def acceptable(text, min_words=6, max_words=34):
    """The sentence filter: one clean, complete sentence of 6-34 words."""
    words = text.split()
    if not min_words <= len(words) <= max_words:
        return False
    if not re.match(r"""^["'(]?[A-Z]""", text) or not re.search(r"""[.?!]["')]?$""", text):
        return False
    if BAD_CHARS.search(text) or re.search(r"(?i)https?:|www\.|\.com\b|\.org\b|\.gov\b", text) or CAPTION.match(text):
        return False
    if text.count('"') % 2 or text.count("(") != text.count(")"):
        return False
    if re.search(r'[,;:][A-Za-z"]', text):
        return False
    numeric = sum(1 for t in words if re.fullmatch(r"[\d.,:/%$-]*\d[\d.,:/%$-]*", t))
    if numeric > 3 or sum(c.isdigit() for c in text) > 0.1 * len(text.replace(" ", "")):
        return False
    letters = [c for c in text if c.isalpha()]
    if letters and sum(c.isupper() for c in letters) > 0.3 * len(letters):
        return False
    if re.search(r"[A-Z][a-z]+(?: [A-Z][a-z]+){4,}", text):
        return False
    if re.search(r"(?:\b\w ){4,}\w\b", text):
        return False
    return True


def key_of(text):
    """The duplicate key of the builder (`Corpus.Normalize`): letters and digits lower-cased, runs of others one space."""
    out, space = [], False
    for ch in text:
        if ch.isalnum():
            if space and out:
                out.append(" ")
            space = False
            out.append(ch.lower())
        else:
            space = True
    return "".join(out)


def strip_braces(text):
    return text.replace("{", "").replace("}", "")


def jaccard_tokens(text):
    return frozenset(re.findall(r"[a-z]+", text.lower()))


def jaccard(a, b):
    if not a or not b:
        return 0.0
    return len(a & b) / len(a | b)

"""UD -> WordSense-lexicon convention map (evaluation only).

The corpus follows the conventions of the WordSense lexicon (GUIDELINE.md, Section 9). A UD reference tag that follows a
different convention is mapped to ours before the comparison, so that the comparison measures annotation errors and not
convention differences. The rules (applied to the UD reference row; nothing else is changed):

1. possessive determiner:  UPOS PRON with Poss=Yes and relation nmod:poss (my, his, their, whose, ...)  ->  DET
   (WordSense's own running-text evaluation uses the same mapping: a possessive pronoun is a determiner; the lexicon has only a
   determiner analysis for my, his, their and whose).
2. copula `be`:           UPOS AUX, lemma be, relation cop  ->  VERB
   (the lexicon's verb `be` carries the copula, existence, location and identity senses; the auxiliary analysis is the helper).
   A `be` that helps another verb (aux, aux:pass) stays AUX; an existential `be` is already VERB in the reference.
3. phrasal-verb particle:  UPOS ADP with relation compound:prt  ->  ADV
   (the lexicon's adverb analyses of up, out, off, down carry the directional and completive senses).
4. preposition before a gerund or noun phrase, or a word with no conjunction analysis:
   UPOS SCONJ and (the lexicon has no conjunction analysis for the word, or the relation is mark and the head is a gerund)  ->  ADP
   (of, with, by, about have no conjunction analysis in the lexicon; a gerund phrase is not a clause with its own subject).

`lexicon_analyses` reads the form tables of the WordSense repository read-only (data/form/*.tsv; the part of speech of an
analysis is the suffix of its entry id or the pos column) and returns, for each requested spelling, the set of lexicon parts of speech.
"""
import os


def lexicon_analyses(data_dir, words):
    """spelling (lower case) -> set of lexicon pos letters (n v a r u p c i d x)."""
    wanted = {w.lower() for w in words}
    out = {w: set() for w in wanted}
    form_dir = os.path.join(data_dir, "form")
    for name in os.listdir(form_dir):
        if not name.endswith(".tsv"):
            continue
        with open(os.path.join(form_dir, name), encoding="utf-8") as f:
            next(f, None)
            for line in f:
                cells = line.rstrip("\n").split("\t")
                spelling = cells[0].lower()
                if spelling in wanted:
                    entry = cells[2]
                    pos = entry.rsplit("-", 1)[-1] if entry else (cells[7] if len(cells) > 7 else "")
                    if pos:
                        out[spelling].add(pos)
    return out


def map_reference(rows, lexicon):
    """rows: dicts with form, lemma, upos, feats, deprel, head_feats. Returns (mapped upos list, counts per rule)."""
    counts = {"possessive_det": 0, "copula_be_verb": 0, "particle_adv": 0, "preposition_adp": 0}
    mapped = []
    for r in rows:
        upos = r["upos"]
        if upos == "PRON" and "Poss=Yes" in r["feats"] and r["deprel"] == "nmod:poss":
            upos = "DET"
            counts["possessive_det"] += 1
        elif upos == "AUX" and r["lemma"].lower() == "be" and r["deprel"] == "cop":
            upos = "VERB"
            counts["copula_be_verb"] += 1
        elif upos == "ADP" and r["deprel"] == "compound:prt":
            upos = "ADV"
            counts["particle_adv"] += 1
        elif upos == "SCONJ" and (("c" not in lexicon.get(r["form"].lower(), set()) and lexicon.get(r["form"].lower())) or (r["deprel"] == "mark" and "VerbForm=Ger" in r["head_feats"])):
            upos = "ADP"
            counts["preposition_adp"] += 1
        mapped.append(upos)
    return mapped, counts

"""Tests of the validation and evaluation scripts of the open corpus (standard library only).

  python -m unittest discover -s tools/open-corpus/tests
"""
import json
import os
import sys
import tempfile
import unittest

sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "scripts"))
import evaluate  # noqa: E402
import validate  # noqa: E402


def block(sent_id, feats):
    """Build a one-token block of a data file; sent_id is None for a block without the identifier line."""
    lines = [] if sent_id is None else [f"# sent_id = {sent_id}"]
    lines += ["# source_id = src1", "# text = cats"]
    lines.append("\t".join(["1", "cats", "cat", "NOUN", "NOUN", feats, "unanimous"]))
    return "\n".join(lines) + "\n"


class ValidateTests(unittest.TestCase):
    """Checks of validate.check_part on small files."""

    def run_part(self, text):
        """Validate a train file with the given text and return the errors."""
        with tempfile.TemporaryDirectory() as folder:
            with open(os.path.join(folder, "train.tsv"), "w", encoding="utf-8", newline="\n") as f:
                f.write(text)
            errors = []
            validate.check_part("representative", folder, ("train",), lambda src: "train", {"src1"}, errors, set(), set())
            return errors

    def test_comment_only_block_is_the_file_header(self):
        """A block of comment lines is a header and is not an error."""
        errors = self.run_part("# corpus = test\n\n" + block("s1", "Number=Plur"))
        self.assertFalse([e for e in errors if "no `# sent_id`" in e])

    def test_block_with_tokens_and_no_sentence_id_is_an_error(self):
        """A block that has token lines but no sent_id line must not pass as a header."""
        errors = self.run_part("# corpus = test\n\n" + block(None, "Number=Plur"))
        self.assertTrue(any("no `# sent_id`" in e for e in errors), errors)

    def test_repeated_feature_name_is_an_error(self):
        """Two values of one feature (Number=Plur|Number=Sing) are rejected."""
        errors = self.run_part(block("s1", "Number=Plur|Number=Sing"))
        self.assertTrue(any("repeated feature name" in e for e in errors), errors)

    def test_single_feature_is_accepted(self):
        """One value of a feature is not a repeated name."""
        errors = self.run_part(block("s1", "Number=Plur"))
        self.assertFalse([e for e in errors if "repeated feature name" in e])


class EvaluateTests(unittest.TestCase):
    """Checks of the agreement statistics of evaluate.py."""

    def test_kappa_of_no_pairs_is_none(self):
        """Nothing compared gives no kappa and no division by zero."""
        self.assertIsNone(evaluate.kappa([]))
        self.assertIsNone(evaluate.rounded(evaluate.kappa([]), 4))

    def test_kappa_of_full_agreement_is_one(self):
        """Two annotators that always agree have a kappa of 1."""
        self.assertEqual(1.0, evaluate.kappa([("NOUN", "NOUN"), ("VERB", "VERB")]))

    def test_corpus_without_second_annotation_gives_statistics(self):
        """A corpus of unchecked tokens (no second tag) reaches the statistics, with null agreement."""
        header = "uid\tidx\tform\tlemma\tupos\tcollapsed\tfeats\tstatus\tprimary_upos\tsecond_upos\n"
        row = "S1\t0\tcats\tcat\tNOUN\tNOUN\tNumber=Plur\tunchecked\tNOUN\t\n"
        with tempfile.TemporaryDirectory() as folder:
            with open(os.path.join(folder, "final.tsv"), "w", encoding="utf-8", newline="\n") as f:
                f.write(header + row)
            out = os.path.join(folder, "scores.json")
            argv = sys.argv
            sys.argv = ["evaluate.py", "--workdir", folder, "--out", out]
            try:
                evaluate.main()
            finally:
                sys.argv = argv
            with open(out, encoding="utf-8") as f:
                result = json.load(f)
        self.assertEqual(0, result["tokens_with_second_annotation"])
        self.assertIsNone(result["agreement"]["upos_kappa"])
        self.assertIsNone(result["agreement"]["upos_percent"])


if __name__ == "__main__":
    unittest.main()

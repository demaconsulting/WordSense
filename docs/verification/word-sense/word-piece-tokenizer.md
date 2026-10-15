# WordPieceTokenizer Verification

## Verification Approach

Tests in `WordPieceTokenizerTests.cs` compare the tokenizer with a fixture of 176 cases generated
from the reference Hugging Face tokenizer for the shipped vocabulary, and test small hand-built
vocabularies. No mocking is needed.

## Test Scenarios

- **WordPieceTokenizer_Tokenize_Fixture_MatchesReferenceTokenizer**: ids and offsets equal the
  reference for every case (accents, emoji, CJK, punctuation, long words, apostrophes).
- **WordPieceTokenizer_Tokenize_SmallVocabulary_SplitsIntoWordPieces**: greedy longest match with `##`.
- **WordPieceTokenizer_Tokenize_EmptyText_ReturnsNoTokens**: empty text gives no tokens.
- **WordPieceTokenizer_TokenizeWords_ReturnsPieceRangePerWord**, **..._NoWords_ReturnsNoTokens**: piece range per word.
- **WordPieceTokenizer_BuildWindow_ShortText_ContainsWholeText**: short input is framed by CLS/SEP.
- **WordPieceTokenizer_BuildWindow_LongText_KeepsTargetInsideWindow**: long input is windowed.

## Requirements Coverage

`WordSense-Tokenizer-Parity`, `WordSense-Tokenizer-Spans` and `WordSense-Tokenizer-Window`.

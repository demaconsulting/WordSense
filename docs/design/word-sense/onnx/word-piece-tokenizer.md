### WordPieceTokenizer Unit Design

![Onnx Structure](OnnxView.svg)

#### Purpose

`WordPieceTokenizer` converts text to the word-piece ids a BERT-family encoder expects, in a managed implementation
that matches the reference tokenizer.

#### Data Model

An internal immutable class built from a vocabulary file; it is safe for concurrent use. The vocabulary shall contain
`[UNK]`, `[CLS]` and `[SEP]`. `TokenizedText` holds the ids, the UTF-16 offsets and, per input word, the range of its
tokens; it can build the encoder input window around a target word.

#### Key Methods

- `FromFile(path, lowerCase)`: loads the vocabulary.
- `Tokenize(text)` and `TokenizeWords(words)`: clean control characters, space out CJK characters, split on
  whitespace and punctuation, and apply greedy longest-match WordPiece with the `##` continuation prefix; a word
  longer than 100 characters becomes `[UNK]`. With `lowerCase` (uncased models) the text is lower-cased and accents
  are stripped; otherwise (cased models) the text is kept as is.
- `TokenizedText.BuildWindow(cls, sep, maxLength, targetFirst, targetEnd)`: builds the model input of at most the
  maximum length with the special tokens, centered on the target tokens when the text is too long.

#### Error Handling

A vocabulary file that cannot be read raises an `IOException` (for example `FileNotFoundException`); a vocabulary without
the required special tokens throws `ArgumentException`; null arguments throw `ArgumentNullException`.

#### Dependencies

The .NET base class library only.

#### Callers

`OnnxEncoder`.

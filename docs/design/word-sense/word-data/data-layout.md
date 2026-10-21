### DataLayout Unit Design

![WordData Structure](WordDataView.svg)

#### Purpose

`DataLayout` defines where each row lives and how a table file looks, for the importer that writes them and the
validator that checks them.

#### Data Model

- File key: the NFC lemma lower-cased in ASCII, first letter; two letters when the first letter is in the split plan;
  `_` when the lemma does not start with an ASCII letter (digits, apostrophes and non-ASCII letters included).
  A lemma of one letter in a split letter, or whose second character is not an ASCII letter, uses the key of the letter
  and `_` (`b_` for the split letter `b`); `FileFor("b", ['b'])` is `b_`. Case folding is ASCII-only on purpose, so the
  key never depends on culture.
- Order: strings compare by UTF-8 bytes, which differs from UTF-16 ordinal order for characters outside the Basic
  Multilingual Plane; integer columns compare numerically.

#### Key Methods

- `FileFor(lemma, splitLetters)`: the file key of a lemma.
- `IsValidKey(key, splitLetters)`: whether the key can result from `FileFor` under the plan.
- `CompareUtf8(a, b)`: the byte order.
- `CheckCell(value)`: rejects a tab, CR or LF.
- `WriteTable(path, header, rows)`: writes LF line ends, no byte-order mark, the header first. The content goes to a
  temporary file in the same folder, which replaces the target (`File.Move` with overwrite) only after every row was
  written.
- `PartitionResolver`: finds the lemma of each row of a partitioned table through its foreign keys.

#### Error Handling

An invalid cell or a write to a folder that cannot be created throws; the write is atomic: a failure leaves an existing
file unchanged and deletes the temporary file, so a table is never left partly written.

#### Dependencies

The .NET base class library.

#### Callers

`OewnImporter`, `DataValidator`.

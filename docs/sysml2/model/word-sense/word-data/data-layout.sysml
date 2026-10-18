package WordSense {
    part def DataLayout {
        doc /* The physical layout of the tables: the file of a lemma (FileFor), the UTF-8 byte order of strings and rows, and the deterministic table writer. */

        comment sourceRef /* Source: tools/WordSense.Builder/Data/DataLayout.cs, tools/WordSense.Builder/Data/PartitionResolver.cs */
        comment testRef /* Test: test/WordSense.Builder.Tests/Data/DataLayoutTests.cs */
        comment designRef /* Design: docs/design/word-sense/word-data/data-layout.md */
        comment verificationRef /* Verification: docs/verification/word-sense/word-data/data-layout.md */
        comment reqRef /* Requirements: docs/reqstream/word-sense/word-data/data-layout.yaml */
    }
}

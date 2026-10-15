package WordSense {
    doc
    /*
     * WordSense is a .NET library template demonstrating DEMA Consulting
     * best practices for structuring, documenting, and testing a DotNet library.
     */

    part def WordSenseSystem {
        doc /* The WordSense system as a whole. */

        comment testRef /* Test: test/DemaConsulting.WordSense.Tests/WordSenseTests.cs */
        comment designRef /* Design: docs/design/word-sense.md */
        comment verificationRef /* Verification: docs/verification/word-sense.md */
        comment reqRef /* Requirements: docs/reqstream/word-sense.yaml */

        part demo : Demo;
    }
}

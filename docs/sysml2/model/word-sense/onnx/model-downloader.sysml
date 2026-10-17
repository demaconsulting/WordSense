package WordSense {
    part def ModelDownloader {
        doc /* Obtains and verifies the files of a model: reuses valid files in the per-user store, otherwise downloads from the mirror and then the GitHub LFS copy, checking every file against its pinned SHA-256. */

        comment sourceRef /* Source: src/DemaConsulting.WordSense.Onnx/ModelDownloader.cs */
        comment testRef /* Test: test/DemaConsulting.WordSense.Onnx.Tests/ModelDownloaderTests.cs */
        comment designRef /* Design: docs/design/word-sense/onnx/model-downloader.md */
        comment verificationRef /* Verification: docs/verification/word-sense/onnx/model-downloader.md */
        comment reqRef /* Requirements: docs/reqstream/word-sense/onnx/model-downloader.yaml */
    }
}

using System.Buffers.Binary;
using System.Security.Cryptography;

namespace DemaConsulting.WordSense.Onnx.Tests;

/// <summary>
///     Writes a tiny BERT-style ONNX graph with layer taps, and a vocabulary that matches it, so that the layered encoder
///     can be tested without a model file.
/// </summary>
/// <remarks>
///     Every output is <c>token id × constant</c> for a constant vector of its own, so the pooled vector of a word is
///     known without running a transformer: the hidden state of the layer <c>n</c> at a token is the token id times
///     <see cref="Constant"/>(<c>n</c>). The graph has the inputs of a BERT model and the outputs
///     <c>last_hidden_state</c> (layer 12) and <c>hidden_n</c> for the requested taps.
/// </remarks>
internal static class TappedGraph
{
    /// <summary>
    ///     The hidden size of the graph.
    /// </summary>
    public const int HiddenSize = 3;

    /// <summary>
    ///     The layer that the graph exposes as <c>last_hidden_state</c>.
    /// </summary>
    public const int LastLayer = 12;

    /// <summary>
    ///     The vocabulary: ids 0 to 7 are <c>[PAD] [UNK] [CLS] [SEP] alpha beta gamma ##s</c>.
    /// </summary>
    public static readonly string[] Vocabulary = ["[PAD]", "[UNK]", "[CLS]", "[SEP]", "alpha", "beta", "gamma", "##s"];

    /// <summary>
    ///     Gets the constant vector of a layer: the hidden state of the layer is the token id times this vector.
    /// </summary>
    /// <param name="layer">The layer.</param>
    /// <returns>The vector.</returns>
    public static float[] Constant(int layer) => [layer * 0.5f, (layer * 0.25f) + 1f, 2f - (layer * 0.125f)];

    /// <summary>
    ///     Writes the graph and the vocabulary into a new directory.
    /// </summary>
    /// <param name="taps">The layers below <see cref="LastLayer"/> that the graph exposes as <c>hidden_n</c>.</param>
    /// <returns>The directory; the files are <c>model-int8-static.onnx</c> and <c>vocab.txt</c>.</returns>
    public static string Write(params int[] taps)
    {
        var directory = Path.Join(Path.GetTempPath(), "tapped-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        File.WriteAllBytes(Path.Join(directory, "model-int8-static.onnx"), Build(taps));
        File.WriteAllLines(Path.Join(directory, "vocab.txt"), Vocabulary);
        return directory;
    }

    /// <summary>
    ///     Computes the SHA-256 of a file as lower-case hexadecimal text.
    /// </summary>
    /// <param name="path">The file.</param>
    /// <returns>The digest.</returns>
    public static string Sha256(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();

    /// <summary>
    ///     Builds the bytes of the ONNX model (opset 13).
    /// </summary>
    /// <param name="taps">The layers below <see cref="LastLayer"/> that the graph exposes as <c>hidden_n</c>.</param>
    /// <returns>The serialized <c>ModelProto</c>.</returns>
    public static byte[] Build(params int[] taps)
    {
        var outputs = new List<(string Name, int Layer)> { ("last_hidden_state", LastLayer) };
        outputs.AddRange(taps.Select(t => ($"hidden_{t}", t)));

        // ids_f = Cast(input_ids, float); ids_u = Unsqueeze(ids_f, [2]); every output = ids_u * constant
        var graph = new List<byte>();
        Message(graph, 1, Node("Cast", ["input_ids"], ["ids_f"], Attribute("to", 1)));
        Message(graph, 1, Node("Unsqueeze", ["ids_f", "axes"], ["ids_u"]));
        foreach (var (name, _) in outputs)
        {
            Message(graph, 1, Node("Mul", ["ids_u", "c_" + name], [name]));
        }

        Text(graph, 2, "tapped");
        Message(graph, 5, Int64Tensor("axes", [2]));
        foreach (var (name, layer) in outputs)
        {
            Message(graph, 5, FloatTensor("c_" + name, Constant(layer)));
        }

        foreach (var input in new[] { "input_ids", "attention_mask", "token_type_ids" })
        {
            Message(graph, 11, ValueInfo(input, 7, "batch", "sequence", null));
        }

        foreach (var (name, _) in outputs)
        {
            Message(graph, 12, ValueInfo(name, 1, "batch", "sequence", HiddenSize));
        }

        var model = new List<byte>();
        Varint(model, 1, 8);
        var opset = new List<byte>();
        Varint(opset, 2, 13);
        Message(model, 8, opset);
        Message(model, 7, graph);
        return [.. model];
    }

    private static List<byte> Node(string op, string[] inputs, string[] outputs, List<byte>? attribute = null)
    {
        var node = new List<byte>();
        foreach (var input in inputs)
        {
            Text(node, 1, input);
        }

        foreach (var output in outputs)
        {
            Text(node, 2, output);
        }

        Text(node, 4, op);
        if (attribute is not null)
        {
            Message(node, 5, attribute);
        }

        return node;
    }

    private static List<byte> Attribute(string name, long value)
    {
        // AttributeProto: name = 1, i = 3, type = 20 (2 = INT)
        var attribute = new List<byte>();
        Text(attribute, 1, name);
        Varint(attribute, 3, value);
        Varint(attribute, 20, 2);
        return attribute;
    }

    private static List<byte> FloatTensor(string name, float[] values)
    {
        // TensorProto: dims = 1, data_type = 2 (1 = FLOAT), name = 8, raw_data = 9
        var tensor = new List<byte>();
        Varint(tensor, 1, 1);
        Varint(tensor, 1, 1);
        Varint(tensor, 1, values.Length);
        Varint(tensor, 2, 1);
        Text(tensor, 8, name);
        var raw = new byte[values.Length * 4];
        for (var i = 0; i < values.Length; i++)
        {
            BinaryPrimitives.WriteSingleLittleEndian(raw.AsSpan(i * 4), values[i]);
        }

        Bytes(tensor, 9, raw);
        return tensor;
    }

    private static List<byte> Int64Tensor(string name, long[] values)
    {
        // data_type 7 = INT64
        var tensor = new List<byte>();
        Varint(tensor, 1, values.Length);
        Varint(tensor, 2, 7);
        Text(tensor, 8, name);
        var raw = new byte[values.Length * 8];
        for (var i = 0; i < values.Length; i++)
        {
            BinaryPrimitives.WriteInt64LittleEndian(raw.AsSpan(i * 8), values[i]);
        }

        Bytes(tensor, 9, raw);
        return tensor;
    }

    private static List<byte> ValueInfo(string name, int elementType, string batch, string sequence, int? last)
    {
        // ValueInfoProto: name = 1, type = 2 { tensor_type = 1 { elem_type = 1, shape = 2 { dim = 1 { dim_value = 1 | dim_param = 2 } } } }
        var shape = new List<byte>();
        Message(shape, 1, ParamDim(batch));
        Message(shape, 1, ParamDim(sequence));
        if (last is { } size)
        {
            var dim = new List<byte>();
            Varint(dim, 1, size);
            Message(shape, 1, dim);
        }

        var tensorType = new List<byte>();
        Varint(tensorType, 1, elementType);
        Message(tensorType, 2, shape);
        var type = new List<byte>();
        Message(type, 1, tensorType);
        var info = new List<byte>();
        Text(info, 1, name);
        Message(info, 2, type);
        return info;
    }

    private static List<byte> ParamDim(string name)
    {
        var dim = new List<byte>();
        Text(dim, 2, name);
        return dim;
    }

    private static void Varint(List<byte> bytes, int field, long value)
    {
        RawVarint(bytes, (ulong)field << 3);
        RawVarint(bytes, (ulong)value);
    }

    private static void Text(List<byte> bytes, int field, string value) => Bytes(bytes, field, System.Text.Encoding.UTF8.GetBytes(value));

    private static void Message(List<byte> bytes, int field, List<byte> message) => Bytes(bytes, field, [.. message]);

    private static void Bytes(List<byte> bytes, int field, byte[] payload)
    {
        RawVarint(bytes, ((ulong)field << 3) | 2);
        RawVarint(bytes, (ulong)payload.Length);
        bytes.AddRange(payload);
    }

    private static void RawVarint(List<byte> bytes, ulong value)
    {
        while (value >= 0x80)
        {
            bytes.Add((byte)(value | 0x80));
            value >>= 7;
        }

        bytes.Add((byte)value);
    }
}

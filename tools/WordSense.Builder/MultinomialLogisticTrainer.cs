namespace WordSense.Builder;

/// <summary>
///     The pooled encoder vectors of every token of a set of sentences, with the gold class of each token.
/// </summary>
/// <remarks>
///     The tokens of all sentences are stored one after another; a token's previous and next neighbor are the adjacent
///     tokens of the same sentence. A token without word pieces has no vector (its row is zeros and
///     <see cref="HasVector"/> is false), which is what the part-of-speech head sees for it.
/// </remarks>
/// <param name="Dimension">The hidden size of the encoder.</param>
/// <param name="Vectors">The vectors, <c>Count × Dimension</c> values, row-major.</param>
/// <param name="Sentence">The index of the sentence of each token.</param>
/// <param name="Gold">The gold class of each token (0 to 9, the parts of speech), or -1 for a token that is not a word class.</param>
/// <param name="HasVector">Whether each token has a vector.</param>
internal sealed record PosTokenTable(int Dimension, float[] Vectors, int[] Sentence, int[] Gold, bool[] HasVector)
{
    /// <summary>
    ///     Gets the number of tokens.
    /// </summary>
    public int Count => Sentence.Length;

    /// <summary>
    ///     Tells whether a token has a previous token in its sentence.
    /// </summary>
    /// <param name="token">The token index.</param>
    /// <returns>True when the previous token of the table is in the same sentence.</returns>
    public bool HasPrevious(int token) => token > 0 && Sentence[token - 1] == Sentence[token];

    /// <summary>
    ///     Tells whether a token has a next token in its sentence.
    /// </summary>
    /// <param name="token">The token index.</param>
    /// <returns>True when the next token of the table is in the same sentence.</returns>
    public bool HasNext(int token) => token + 1 < Count && Sentence[token + 1] == Sentence[token];

    /// <summary>
    ///     Lists the tokens a head is trained on: a token with a vector and a gold class.
    /// </summary>
    /// <returns>The token indexes in increasing order.</returns>
    public int[] TrainingRows()
    {
        var rows = new List<int>();
        for (var t = 0; t < Count; t++)
        {
            if (HasVector[t] && Gold[t] >= 0)
            {
                rows.Add(t);
            }
        }

        return [.. rows];
    }
}

/// <summary>
///     The settings of a fit.
/// </summary>
/// <param name="C">The inverse regularization strength: the objective is the mean loss plus <c>‖W‖² / (2 C n)</c>.</param>
/// <param name="GradientTolerance">The largest absolute gradient component at which the fit stops.</param>
/// <param name="MaxIterations">The iteration cap.</param>
/// <param name="Parallelism">The number of threads; the result does not depend on it.</param>
internal sealed record PosFitOptions(double C, double GradientTolerance = 1e-4, int MaxIterations = 300, int Parallelism = 0);

/// <summary>
///     A fitted part-of-speech head.
/// </summary>
/// <param name="Weights">The weights with the standardization folded in: one row per class of <c>3 × Dimension</c> values (previous word, word, next word), for the raw pooled vectors.</param>
/// <param name="Biases">One bias per class, with the standardization folded in.</param>
/// <param name="Iterations">The number of iterations.</param>
/// <param name="Evaluations">The number of evaluations of the objective.</param>
/// <param name="Converged">True when the fit reached the tolerance; false when it stopped at the iteration cap.</param>
/// <param name="Objective">The value of the objective at the optimum.</param>
internal sealed record PosFit(float[] Weights, float[] Biases, int Iterations, int Evaluations, bool Converged, double Objective);

/// <summary>
///     Fits a multinomial logistic regression with L2 regularization on the vectors of a word and its two neighbors.
/// </summary>
/// <remarks>
///     <para>
///     The features are the vectors of the previous word, the word and the next word (zeros beyond the sentence edge),
///     standardized per feature over the training tokens; the objective is the mean negative log-likelihood plus
///     <c>‖W‖² / (2 C n)</c> with an unpenalized intercept, which is the scaling of the usual implementations, so a given
///     <c>C</c> means the same regularization. The minimizer is <see cref="Lbfgs"/> in double precision, started at zero.
///     </para>
///     <para>
///     The result is deterministic, whatever the number of threads: the work is cut into fixed chunks of tokens, every
///     chunk is summed in a fixed order and the chunk sums are added in chunk order. The standardization is folded into
///     the returned weights, so the head works on the raw pooled vectors.
///     </para>
/// </remarks>
internal static class MultinomialLogisticTrainer
{
    /// <summary>
    ///     The number of classes of the head: the parts of speech.
    /// </summary>
    public const int Classes = 10;

    private const int ChunkSize = 1024;

    /// <summary>
    ///     Fits the head.
    /// </summary>
    /// <param name="tokens">The tokens of the training sentences.</param>
    /// <param name="options">The settings.</param>
    /// <returns>The fitted head.</returns>
    /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
    /// <exception cref="ArgumentException">Thrown when the table has no training token or a gold class is outside the classes.</exception>
    public static PosFit Fit(PosTokenTable tokens, PosFitOptions options)
    {
        ArgumentNullException.ThrowIfNull(tokens);
        ArgumentNullException.ThrowIfNull(options);
        if (!(options.C > 0) || !double.IsFinite(options.C))
        {
            throw new ArgumentException("The regularization strength C must be a positive number.", nameof(options));
        }

        var rows = tokens.TrainingRows();
        if (rows.Length == 0)
        {
            throw new ArgumentException("The table has no training token.", nameof(tokens));
        }

        if (rows.Any(r => tokens.Gold[r] >= Classes))
        {
            throw new ArgumentException("A gold class is outside the parts of speech.", nameof(tokens));
        }

        var (mean, std) = Standardization(tokens, rows);
        var objective = new Objective(tokens, rows, options, mean, std);
        var parameters = objective.ParameterCount;
        var result = Lbfgs.Minimize(objective.Evaluate, new double[parameters], options.GradientTolerance, options.MaxIterations);
        var (weights, biases) = Fold(result.X, mean, std, tokens.Dimension);
        return new PosFit(weights, biases, result.Iterations, result.Evaluations, result.Converged, result.Value);
    }

    /// <summary>
    ///     Computes the mean and the standard deviation of every feature over the training tokens, with zeros for a
    ///     neighbor beyond the sentence edge.
    /// </summary>
    /// <param name="tokens">The tokens.</param>
    /// <param name="rows">The training tokens.</param>
    /// <returns>The mean and the standard deviation of the <c>3 × Dimension</c> features.</returns>
    private static (double[] Mean, double[] Std) Standardization(PosTokenTable tokens, int[] rows)
    {
        var d = tokens.Dimension;
        var mean = new double[3 * d];
        var std = new double[3 * d];
        var v = tokens.Vectors;

        // Two passes in a fixed order: the mean, then the mean squared deviation (a missing neighbor contributes zeros)
        foreach (var r in rows)
        {
            Add(mean, 0, tokens.HasPrevious(r) ? r - 1 : -1);
            Add(mean, d, r);
            Add(mean, 2 * d, tokens.HasNext(r) ? r + 1 : -1);
        }

        for (var q = 0; q < mean.Length; q++)
        {
            mean[q] /= rows.Length;
        }

        foreach (var r in rows)
        {
            Square(std, mean, 0, tokens.HasPrevious(r) ? r - 1 : -1);
            Square(std, mean, d, r);
            Square(std, mean, 2 * d, tokens.HasNext(r) ? r + 1 : -1);
        }

        for (var q = 0; q < std.Length; q++)
        {
            std[q] = Math.Sqrt(std[q] / rows.Length) + 1e-6;
        }

        return (mean, std);

        void Add(double[] sum, int offset, int token)
        {
            if (token < 0)
            {
                return;
            }

            for (var j = 0; j < d; j++)
            {
                sum[offset + j] += v[(token * d) + j];
            }
        }

        void Square(double[] sum, double[] center, int offset, int token)
        {
            for (var j = 0; j < d; j++)
            {
                var x = (token < 0 ? 0.0 : v[(token * d) + j]) - center[offset + j];
                sum[offset + j] += x * x;
            }
        }
    }

    /// <summary>
    ///     Folds the standardization into the weights and the intercepts.
    /// </summary>
    /// <param name="theta">The optimum: the weights of the standardized features, then the intercepts.</param>
    /// <param name="mean">The mean of every feature.</param>
    /// <param name="std">The standard deviation of every feature.</param>
    /// <param name="d">The hidden size.</param>
    /// <returns>The weights and the intercepts for the raw vectors, rounded to 32 bits.</returns>
    private static (float[] Weights, float[] Biases) Fold(double[] theta, double[] mean, double[] std, int d)
    {
        var f3 = 3 * d;
        var weights = new float[Classes * f3];
        var biases = new float[Classes];
        for (var c = 0; c < Classes; c++)
        {
            var bias = theta[(Classes * f3) + c];
            for (var q = 0; q < f3; q++)
            {
                var w = theta[(c * f3) + q];
                weights[(c * f3) + q] = (float)(w / std[q]);
                bias -= w * mean[q] / std[q];
            }

            biases[c] = (float)bias;
        }

        return (weights, biases);
    }

    /// <summary>
    ///     The regularized negative log-likelihood and its gradient over the standardized features.
    /// </summary>
    private sealed class Objective
    {
        private readonly PosTokenTable _tokens;
        private readonly int[] _rows;
        private readonly int[] _label;
        private readonly double[] _mean;
        private readonly double[] _std;
        private readonly double _c;
        private readonly int _d;
        private readonly int _f3;
        private readonly int _chunks;
        private readonly ParallelOptions _parallel;
        private readonly double[] _projected;
        private readonly double[] _residual;
        private readonly double[][] _partial;
        private readonly double[] _u;

        public Objective(PosTokenTable tokens, int[] rows, PosFitOptions options, double[] mean, double[] std)
        {
            _tokens = tokens;
            _rows = rows;
            _mean = mean;
            _std = std;
            _c = options.C;
            _d = tokens.Dimension;
            _f3 = 3 * _d;
            _chunks = (tokens.Count + ChunkSize - 1) / ChunkSize;
            _parallel = new ParallelOptions { MaxDegreeOfParallelism = options.Parallelism > 0 ? options.Parallelism : Environment.ProcessorCount };
            _label = new int[tokens.Count];
            Array.Fill(_label, -1);
            foreach (var r in rows)
            {
                _label[r] = tokens.Gold[r];
            }

            _projected = new double[tokens.Count * 3 * Classes];
            _residual = new double[tokens.Count * Classes];
            _partial = new double[_chunks][];
            _u = new double[Classes * _f3];
        }

        public int ParameterCount => (Classes * _f3) + Classes;

        public double Evaluate(double[] theta, double[] gradient)
        {
            var d = _d;
            var f3 = _f3;
            var n = _tokens.Count;
            var rowCount = _rows.Length;
            var vectors = _tokens.Vectors;

            // Weights on the raw vectors, and the constant that the centering of the features contributes
            var offset = new double[Classes];
            for (var c = 0; c < Classes; c++)
            {
                for (var q = 0; q < f3; q++)
                {
                    var u = theta[(c * f3) + q] / _std[q];
                    _u[(c * f3) + q] = u;
                    offset[c] += u * _mean[q];
                }
            }

            // Project every token on the three blocks of every class
            Parallel.For(0, _chunks, _parallel, chunk =>
            {
                var end = Math.Min(n, (chunk + 1) * ChunkSize);
                for (var t = chunk * ChunkSize; t < end; t++)
                {
                    if (!_tokens.HasVector[t])
                    {
                        Array.Clear(_projected, t * 3 * Classes, 3 * Classes);
                        continue;
                    }

                    for (var c = 0; c < Classes; c++)
                    {
                        for (var block = 0; block < 3; block++)
                        {
                            _projected[(t * 3 * Classes) + (block * Classes) + c] = Dot(vectors, t * d, _u, (c * f3) + (block * d), d);
                        }
                    }
                }
            });

            // Loss and residuals of the training tokens, in token order
            Array.Clear(_residual);
            var gradBias = new double[Classes];
            var loss = 0.0;
            var z = new double[Classes];
            foreach (var t in _rows)
            {
                for (var c = 0; c < Classes; c++)
                {
                    var s = theta[(Classes * f3) + c] - offset[c];
                    s += _tokens.HasPrevious(t) ? _projected[((t - 1) * 3 * Classes) + c] : 0.0;
                    s += _projected[(t * 3 * Classes) + Classes + c];
                    s += _tokens.HasNext(t) ? _projected[((t + 1) * 3 * Classes) + (2 * Classes) + c] : 0.0;
                    z[c] = s;
                }

                var max = z.Max();
                var total = 0.0;
                for (var c = 0; c < Classes; c++)
                {
                    total += Math.Exp(z[c] - max);
                }

                var logTotal = max + Math.Log(total);
                var label = _label[t];
                loss += logTotal - z[label];
                for (var c = 0; c < Classes; c++)
                {
                    var g = (Math.Exp(z[c] - logTotal) - (c == label ? 1.0 : 0.0)) / rowCount;
                    _residual[(t * Classes) + c] = g;
                    gradBias[c] += g;
                }
            }

            // Sum the residual-weighted vectors per block, chunk by chunk
            Parallel.For(0, _chunks, _parallel, chunk =>
            {
                var sum = _partial[chunk] ??= new double[Classes * f3];
                Array.Clear(sum);
                var end = Math.Min(n, (chunk + 1) * ChunkSize);
                for (var t = chunk * ChunkSize; t < end; t++)
                {
                    if (!_tokens.HasVector[t])
                    {
                        continue;
                    }

                    // The token is the word of its own row, the previous word of the next row and the next word of the previous row
                    if (_label[t] >= 0)
                    {
                        AddScaled(sum, d, 1, t, t);
                    }

                    if (t + 1 < n && _tokens.HasPrevious(t + 1) && _label[t + 1] >= 0)
                    {
                        AddScaled(sum, d, 0, t, t + 1);
                    }

                    if (t > 0 && _tokens.HasNext(t - 1) && _label[t - 1] >= 0)
                    {
                        AddScaled(sum, d, 2, t, t - 1);
                    }
                }
            });

            // Gradient over the standardized weights, plus the penalty
            var penalty = 0.0;
            var scale = 1.0 / (_c * rowCount);
            Parallel.For(0, Classes, _parallel, c =>
            {
                for (var q = 0; q < f3; q++)
                {
                    var a = 0.0;
                    for (var chunk = 0; chunk < _chunks; chunk++)
                    {
                        a += _partial[chunk][(c * f3) + q];
                    }

                    gradient[(c * f3) + q] = ((a - (_mean[q] * gradBias[c])) / _std[q]) + (theta[(c * f3) + q] * scale);
                }
            });

            for (var i = 0; i < Classes * f3; i++)
            {
                penalty += theta[i] * theta[i];
            }

            for (var c = 0; c < Classes; c++)
            {
                gradient[(Classes * f3) + c] = gradBias[c];
            }

            return (loss / rowCount) + (0.5 * scale * penalty);
        }

        private void AddScaled(double[] sum, int d, int block, int token, int row)
        {
            var vectors = _tokens.Vectors;
            for (var c = 0; c < Classes; c++)
            {
                var g = _residual[(row * Classes) + c];
                var at = (c * _f3) + (block * d);
                var from = token * d;
                for (var j = 0; j < d; j++)
                {
                    sum[at + j] += g * vectors[from + j];
                }
            }
        }

        /// <summary>
        ///     Computes a dot product in a fixed order: four partial sums over the positions modulo four, added as
        ///     <c>(s0 + s1) + (s2 + s3)</c>, then the remainder.
        /// </summary>
        private static double Dot(float[] vector, int vectorOffset, double[] weights, int weightOffset, int length)
        {
            double s0 = 0, s1 = 0, s2 = 0, s3 = 0;
            var j = 0;
            for (; j + 3 < length; j += 4)
            {
                s0 += vector[vectorOffset + j] * weights[weightOffset + j];
                s1 += vector[vectorOffset + j + 1] * weights[weightOffset + j + 1];
                s2 += vector[vectorOffset + j + 2] * weights[weightOffset + j + 2];
                s3 += vector[vectorOffset + j + 3] * weights[weightOffset + j + 3];
            }

            var sum = (s0 + s1) + (s2 + s3);
            for (; j < length; j++)
            {
                sum += vector[vectorOffset + j] * weights[weightOffset + j];
            }

            return sum;
        }
    }
}

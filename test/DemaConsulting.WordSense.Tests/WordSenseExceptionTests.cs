namespace DemaConsulting.WordSense.Tests;

/// <summary>
///     Tests for the typed exceptions of the core package: <see cref="WordSenseException"/>,
///     <see cref="ModelUnavailableException"/>, <see cref="ModelIntegrityException"/> and
///     <see cref="DownloadConfigurationException"/>.
/// </summary>
public class WordSenseExceptionTests
{
    /// <summary>
    ///     Creates each exception type with its constructors, through the public constructors of the type.
    /// </summary>
    public static TheoryData<Type> ExceptionTypes =>
    [
        typeof(ModelUnavailableException),
        typeof(ModelIntegrityException),
        typeof(DownloadConfigurationException),
    ];

    /// <summary>
    ///     The base class is abstract and public, so only the typed exceptions are thrown, and it derives from
    ///     <see cref="Exception"/>.
    /// </summary>
    [Fact]
    public void WordSenseExceptions_WordSenseException_IsAbstractPublicException()
    {
        // Arrange
        var type = typeof(WordSenseException);

        // Act / Assert
        Assert.True(type.IsAbstract);
        Assert.True(type.IsPublic);
        Assert.Equal(typeof(Exception), type.BaseType);
    }

    /// <summary>
    ///     Each typed exception is a public sealed class that derives from the base class, and none of them is an
    ///     <see cref="ArgumentException"/> (not even the download configuration exception).
    /// </summary>
    /// <param name="type">The exception type.</param>
    [Theory]
    [MemberData(nameof(ExceptionTypes))]
    public void WordSenseExceptions_Hierarchy_DerivesFromWordSenseException(Type type)
    {
        // Arrange / Act
        var isWordSenseException = typeof(WordSenseException).IsAssignableFrom(type);
        var isArgumentException = typeof(ArgumentException).IsAssignableFrom(type);

        // Assert
        Assert.True(type.IsPublic);
        Assert.True(type.IsSealed);
        Assert.Equal(typeof(WordSenseException), type.BaseType);
        Assert.True(isWordSenseException);
        Assert.False(isArgumentException);
    }

    /// <summary>
    ///     Each typed exception has the three standard constructors, and nothing else.
    /// </summary>
    /// <param name="type">The exception type.</param>
    [Theory]
    [MemberData(nameof(ExceptionTypes))]
    public void WordSenseExceptions_Constructors_AreTheThreeStandardOnes(Type type)
    {
        // Arrange / Act
        var signatures = type.GetConstructors().Select(c => c.GetParameters().Select(p => p.ParameterType).ToArray()).ToList();

        // Assert
        Assert.Equal(3, signatures.Count);
        Assert.Contains(signatures, s => s.Length == 0);
        Assert.Contains(signatures, s => s.SequenceEqual([typeof(string)]));
        Assert.Contains(signatures, s => s.SequenceEqual([typeof(string), typeof(Exception)]));
    }

    /// <summary>
    ///     The constructors keep the message and the original exception as the inner exception.
    /// </summary>
    /// <param name="type">The exception type.</param>
    [Theory]
    [MemberData(nameof(ExceptionTypes))]
    public void WordSenseExceptions_Constructors_KeepMessageAndInnerException(Type type)
    {
        // Arrange
        var inner = new InvalidDataException("original");

        // Act
        var empty = (WordSenseException)Activator.CreateInstance(type)!;
        var withMessage = (WordSenseException)Activator.CreateInstance(type, "message")!;
        var withInner = (WordSenseException)Activator.CreateInstance(type, "message", inner)!;

        // Assert
        Assert.NotNull(empty.Message);
        Assert.Null(empty.InnerException);
        Assert.Equal("message", withMessage.Message);
        Assert.Null(withMessage.InnerException);
        Assert.Equal("message", withInner.Message);
        Assert.Same(inner, withInner.InnerException);
    }

    /// <summary>
    ///     Catching the base class catches every typed exception, so a caller can handle all of them in one place.
    /// </summary>
    [Fact]
    public void WordSenseExceptions_CatchBase_CatchesEveryTypedException()
    {
        // Arrange
        Exception[] thrown =
        [
            new ModelUnavailableException("a"),
            new ModelIntegrityException("b"),
            new DownloadConfigurationException("c"),
        ];

        // Act: each exception is caught by the one handler of the base class
        var caught = thrown.Select(CatchAsWordSenseException).ToList();

        // Assert
        Assert.Equal(["a", "b", "c"], caught);
    }

    private static string CatchAsWordSenseException(Exception exception)
    {
        try
        {
            throw exception;
        }
        catch (WordSenseException ex)
        {
            return ex.Message;
        }
    }
}

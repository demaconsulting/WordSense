using System.Net;

namespace DemaConsulting.WordSense.Tests;

/// <summary>
///     Tests for <see cref="DownloadMirror"/>.
/// </summary>
public class DownloadMirrorTests
{
    private static Func<string, string?> Env(Dictionary<string, string> values)
    {
        return name => values.GetValueOrDefault(name);
    }

    /// <summary>
    ///     A plain https mirror is accepted.
    /// </summary>
    [Fact]
    public void DownloadMirror_Constructor_HttpsBase_IsAccepted()
    {
        var mirror = new DownloadMirror(new Uri("https://mirror.example/models"));

        Assert.Equal("https://mirror.example/models", mirror.BaseUri.ToString());
        Assert.Null(mirror.Credentials);
        Assert.Null(mirror.BearerToken);
    }

    /// <summary>
    ///     Invalid base addresses are rejected.
    /// </summary>
    [Theory]
    [InlineData("ftp://mirror.example/m")]
    [InlineData("https://mirror.example/m?x=1")]
    [InlineData("https://mirror.example/m#frag")]
    [InlineData("https://user:pw@mirror.example/m")]
    public void DownloadMirror_Constructor_InvalidBase_Throws(string uri)
    {
        Assert.Throws<ArgumentException>(() => new DownloadMirror(new Uri(uri)));
    }

    /// <summary>
    ///     A relative address is rejected.
    /// </summary>
    [Fact]
    public void DownloadMirror_Constructor_RelativeBase_Throws()
    {
        Assert.Throws<ArgumentException>(() => new DownloadMirror(new Uri("models", UriKind.Relative)));
        Assert.Throws<ArgumentNullException>(() => new DownloadMirror(null!));
    }

    /// <summary>
    ///     Credentials and bearer token are exclusive, and a bearer token must not be blank.
    /// </summary>
    [Fact]
    public void DownloadMirror_Constructor_BadAuthentication_Throws()
    {
        var uri = new Uri("https://mirror.example/m");

        Assert.Throws<ArgumentException>(() => new DownloadMirror(uri, new NetworkCredential("u", "p"), "token"));
        Assert.Throws<ArgumentException>(() => new DownloadMirror(uri, null, "  "));
    }

    /// <summary>
    ///     Secrets may only travel over https unless the host is loopback.
    /// </summary>
    [Fact]
    public void DownloadMirror_Constructor_CredentialsOverPlainHttp_OnlyLoopbackAllowed()
    {
        Assert.Throws<ArgumentException>(() => new DownloadMirror(new Uri("http://mirror.example/m"), null, "token"));
        Assert.Throws<ArgumentException>(() => new DownloadMirror(new Uri("http://mirror.example/m"), new NetworkCredential("u", "p")));

        var local = new DownloadMirror(new Uri("http://127.0.0.1:1/m"), null, "token");

        Assert.Equal("token", local.BearerToken);
    }

    /// <summary>
    ///     The text form never reveals secrets.
    /// </summary>
    [Fact]
    public void DownloadMirror_ToString_RedactsSecrets()
    {
        var mirror = new DownloadMirror(new Uri("https://mirror.example/m"), new NetworkCredential("user", "hunter2"));
        var token = new DownloadMirror(new Uri("https://mirror.example/m"), null, "secret-token");

        Assert.DoesNotContain("hunter2", mirror.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("secret-token", token.ToString(), StringComparison.Ordinal);
    }

    /// <summary>
    ///     No URL means no mirror.
    /// </summary>
    [Fact]
    public void DownloadMirror_FromEnvironment_NoUrl_ReturnsNull()
    {
        Assert.Null(DownloadMirror.FromEnvironment(Env([])));
        Assert.Null(DownloadMirror.FromEnvironment(Env(new() { [DownloadMirror.UrlEnvironmentVariable] = "  " })));
    }

    /// <summary>
    ///     The documented variables configure URL, Basic credentials and bearer token.
    /// </summary>
    [Fact]
    public void DownloadMirror_FromEnvironment_AllVariables_BuildsMirror()
    {
        var basic = DownloadMirror.FromEnvironment(Env(new()
        {
            ["WORDSENSE_MIRROR_URL"] = "https://mirror.example/m",
            ["WORDSENSE_MIRROR_USER"] = "user",
            ["WORDSENSE_MIRROR_PASSWORD"] = "pw",
        }));
        var bearer = DownloadMirror.FromEnvironment(Env(new()
        {
            ["WORDSENSE_MIRROR_URL"] = "https://mirror.example/m",
            ["WORDSENSE_MIRROR_BEARER_TOKEN"] = "tok",
        }));

        Assert.Equal("user", basic!.Credentials!.UserName);
        Assert.Equal("pw", basic.Credentials.Password);
        Assert.Equal("tok", bearer!.BearerToken);
    }

    /// <summary>
    ///     Credentials without a URL, half a credential and a bad URL are configuration errors.
    /// </summary>
    [Fact]
    public void DownloadMirror_FromEnvironment_InvalidConfiguration_Throws()
    {
        Assert.Throws<ArgumentException>(() => DownloadMirror.FromEnvironment(Env(new() { ["WORDSENSE_MIRROR_USER"] = "u" })));
        Assert.Throws<ArgumentException>(() => DownloadMirror.FromEnvironment(Env(new()
        {
            ["WORDSENSE_MIRROR_URL"] = "https://mirror.example/m",
            ["WORDSENSE_MIRROR_USER"] = "u",
        })));
        Assert.Throws<ArgumentException>(() => DownloadMirror.FromEnvironment(Env(new() { ["WORDSENSE_MIRROR_URL"] = "not a url" })));
        Assert.Throws<ArgumentException>(() => DownloadMirror.FromEnvironment(Env(new() { ["WORDSENSE_MIRROR_URL"] = "ftp://x/y" })));
        Assert.Throws<ArgumentNullException>(() => DownloadMirror.FromEnvironment(null!));
    }
}

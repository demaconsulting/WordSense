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
    ///     A base address that carries user information is rejected, whatever the other arguments are.
    /// </summary>
    [Fact]
    public void DownloadMirror_Constructor_UserInfoInBase_Throws()
    {
        var withUser = new Uri("https://user@mirror.example/m");
        var withUserAndPassword = new Uri("https://user:pw@mirror.example/m");

        Assert.Throws<ArgumentException>(() => new DownloadMirror(withUser));
        Assert.Throws<ArgumentException>(() => new DownloadMirror(withUserAndPassword));
        Assert.Throws<ArgumentException>(() => new DownloadMirror(withUserAndPassword, null, "token"));
    }

    /// <summary>
    ///     The text form never reveals secrets.
    /// </summary>
    [Fact]
    public void DownloadMirror_ToString_RedactsSecrets()
    {
        var mirror = new DownloadMirror(new Uri("https://mirror.example/m"), new NetworkCredential("user", "hunter2"));
        var token = new DownloadMirror(new Uri("https://mirror.example/m"), null, "secret-token");
        var none = new DownloadMirror(new Uri("https://mirror.example/m"));

        Assert.DoesNotContain("hunter2", mirror.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("user", mirror.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("secret-token", token.ToString(), StringComparison.Ordinal);
        Assert.Contains("Credentials = ***", mirror.ToString(), StringComparison.Ordinal);
        Assert.Contains("BearerToken = null", mirror.ToString(), StringComparison.Ordinal);
        Assert.Contains("BearerToken = ***", token.ToString(), StringComparison.Ordinal);
        Assert.Contains("Credentials = null", token.ToString(), StringComparison.Ordinal);
        Assert.Contains("https://mirror.example/m", mirror.ToString(), StringComparison.Ordinal);
        Assert.Contains("Credentials = null", none.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("***", none.ToString(), StringComparison.Ordinal);
    }

    /// <summary>
    ///     A whitespace-only value counts as unset for the user, password and bearer-token variables.
    /// </summary>
    [Fact]
    public void DownloadMirror_FromEnvironment_WhitespaceOnlyCredentials_AreUnset()
    {
        var mirror = DownloadMirror.FromEnvironment(Env(new()
        {
            ["WORDSENSE_MIRROR_URL"] = "https://mirror.example/m",
            ["WORDSENSE_MIRROR_USER"] = " \t ",
            ["WORDSENSE_MIRROR_PASSWORD"] = "  ",
            ["WORDSENSE_MIRROR_BEARER_TOKEN"] = " ",
        }));
        var withoutUrl = DownloadMirror.FromEnvironment(Env(new()
        {
            ["WORDSENSE_MIRROR_USER"] = " ",
            ["WORDSENSE_MIRROR_PASSWORD"] = " ",
            ["WORDSENSE_MIRROR_BEARER_TOKEN"] = " ",
        }));

        Assert.NotNull(mirror);
        Assert.Null(mirror.Credentials);
        Assert.Null(mirror.BearerToken);
        Assert.Null(withoutUrl);

        // A blank user with a real password leaves a password without a user, which is an error
        Assert.Throws<ArgumentException>(() => DownloadMirror.FromEnvironment(Env(new()
        {
            ["WORDSENSE_MIRROR_URL"] = "https://mirror.example/m",
            ["WORDSENSE_MIRROR_USER"] = " ",
            ["WORDSENSE_MIRROR_PASSWORD"] = "pw",
        })));
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
    ///     A bearer token without a URL, and a password without a user, are configuration errors.
    /// </summary>
    [Fact]
    public void DownloadMirror_FromEnvironment_BearerWithoutUrlOrPasswordWithoutUser_Throws()
    {
        Assert.Throws<ArgumentException>(() => DownloadMirror.FromEnvironment(Env(new() { ["WORDSENSE_MIRROR_BEARER_TOKEN"] = "tok" })));
        Assert.Throws<ArgumentException>(() => DownloadMirror.FromEnvironment(Env(new()
        {
            ["WORDSENSE_MIRROR_URL"] = "https://mirror.example/m",
            ["WORDSENSE_MIRROR_PASSWORD"] = "pw",
        })));
    }

    /// <summary>
    ///     The names localhost and the IPv6 loopback literal count as loopback for plain http secrets.
    /// </summary>
    /// <param name="uri">The plain http mirror address.</param>
    [Theory]
    [InlineData("http://localhost:8080/m")]
    [InlineData("http://LOCALHOST/m")]
    [InlineData("http://[::1]:8080/m")]
    public void DownloadMirror_Constructor_LoopbackNames_AllowSecretsOverHttp(string uri)
    {
        var mirror = new DownloadMirror(new Uri(uri), null, "token");

        Assert.Equal("token", mirror.BearerToken);
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

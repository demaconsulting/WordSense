using System.Net;
using System.Net.Sockets;

namespace DemaConsulting.WordSense.Tests;

/// <summary>
///     A minimal loopback HTTP server used to exercise the downloader without any network access.
/// </summary>
internal sealed class LocalServer : IDisposable
{
    private readonly HttpListener _listener = new();
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _loop;
    private readonly object _gate = new();
    private readonly List<(string Path, string? Authorization)> _requests = [];

    /// <summary>
    ///     Starts a server on a free loopback port.
    /// </summary>
    /// <param name="handler">Handles each request; it must complete the response.</param>
    public LocalServer(Func<HttpListenerContext, Task> handler)
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();

        BaseUri = new Uri($"http://127.0.0.1:{port}");
        _listener.Prefixes.Add(BaseUri.AbsoluteUri);
        _listener.Start();
        _loop = Task.Run(async () =>
        {
            while (!_stop.IsCancellationRequested)
            {
                HttpListenerContext context;
                try
                {
                    context = await _listener.GetContextAsync();
                }
                catch (Exception ex) when (ex is HttpListenerException or ObjectDisposedException or InvalidOperationException)
                {
                    return;
                }

                lock (_gate)
                {
                    _requests.Add((context.Request.Url!.AbsolutePath, context.Request.Headers["Authorization"]));
                }

                _ = Task.Run(async () =>
                {
                    try
                    {
                        await handler(context);
                    }
                    catch (Exception ex) when (ex is HttpListenerException or IOException or ObjectDisposedException or OperationCanceledException)
                    {
                        // The client went away (cancellation or timeout); nothing to do
                    }
                    finally
                    {
                        try
                        {
                            context.Response.Close();
                        }
                        catch (Exception ex) when (ex is HttpListenerException or ObjectDisposedException)
                        {
                            // Already closed
                        }
                    }
                }, CancellationToken.None);
            }
        }, CancellationToken.None);
    }

    /// <summary>
    ///     Gets the base address.
    /// </summary>
    public Uri BaseUri { get; }

    /// <summary>
    ///     Gets a snapshot of the requests received so far.
    /// </summary>
    public IReadOnlyList<(string Path, string? Authorization)> Requests
    {
        get
        {
            lock (_gate)
            {
                return _requests.ToArray();
            }
        }
    }

    /// <summary>
    ///     Writes a file as the response body.
    /// </summary>
    /// <param name="context">The request context.</param>
    /// <param name="path">The file to send.</param>
    /// <returns>A task that completes when the body is written.</returns>
    public static async Task SendFileAsync(HttpListenerContext context, string path)
    {
        context.Response.StatusCode = 200;
        await using var file = File.OpenRead(path);
        context.Response.ContentLength64 = file.Length;
        await file.CopyToAsync(context.Response.OutputStream);
    }

    /// <summary>
    ///     Writes a status code and a short body.
    /// </summary>
    /// <param name="context">The request context.</param>
    /// <param name="status">The status code.</param>
    /// <param name="body">The body text.</param>
    /// <returns>A task that completes when the body is written.</returns>
    public static async Task SendAsync(HttpListenerContext context, int status, string body)
    {
        context.Response.StatusCode = status;
        var bytes = System.Text.Encoding.UTF8.GetBytes(body);
        context.Response.ContentLength64 = bytes.Length;
        await context.Response.OutputStream.WriteAsync(bytes);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _stop.Cancel();
        _listener.Close();
        _loop.Wait(TimeSpan.FromSeconds(5), CancellationToken.None);
        _stop.Dispose();
    }
}

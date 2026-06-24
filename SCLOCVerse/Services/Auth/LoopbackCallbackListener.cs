using SCLOCVerse.Interfaces;
using System;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web;

namespace SCLOCVerse.Services.Auth
{
    /// <summary>
    /// Слухає OAuth callback на випадковому loopback порту.
    /// Повертає callback URL з authorization code.
    /// </summary>
    public sealed class LoopbackCallbackListener : ILoopbackCallbackListener, IDisposable
    {
        private readonly SemaphoreSlim _lock = new(1, 1);
        private HttpListener? _listener;
        private int _port;
        private CancellationTokenSource? _cts;
        private TaskCompletionSource<Uri?>? _callbackTcs;
        private bool _disposed;

        public async Task<Uri> StartAsync(CancellationToken cancellationToken = default)
        {
            await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (_listener != null)
                    throw new InvalidOperationException("Listener is already running.");

                _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                _callbackTcs = new TaskCompletionSource<Uri?>(TaskCreationOptions.RunContinuationsAsynchronously);

                _port = FindAvailablePort();
                var prefix = $"http://127.0.0.1:{_port}/auth/callback/";

                _listener = new HttpListener();
                _listener.Prefixes.Add(prefix);
                _listener.Start();

                _ = ListenAsync(_cts.Token, _callbackTcs);

                return new Uri(prefix);
            }
            finally
            {
                _lock.Release();
            }
        }

        public async Task<Uri?> WaitForCallbackAsync(CancellationToken cancellationToken = default)
        {
            var tcs = _callbackTcs;
            if (tcs == null)
                throw new InvalidOperationException("Listener has not been started.");

            using var registration = cancellationToken.Register(() => tcs.TrySetResult(null));
            return await tcs.Task.ConfigureAwait(false);
        }

        public async Task StopAsync()
        {
            await _lock.WaitAsync().ConfigureAwait(false);
            try
            {
                _cts?.Cancel();
                _cts?.Dispose();
                _cts = null;

                _callbackTcs?.TrySetResult(null);
                _callbackTcs = null;

                if (_listener != null)
                {
                    _listener.Stop();
                    _listener.Close();
                    _listener = null;
                }
            }
            finally
            {
                _lock.Release();
            }
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            StopAsync().GetAwaiter().GetResult();
            _lock.Dispose();
        }

        private async Task ListenAsync(CancellationToken cancellationToken, TaskCompletionSource<Uri?> tcs)
        {
            HttpListener? listener;

            lock (_lock)
            {
                listener = _listener;
                if (listener == null || tcs != _callbackTcs)
                    return;
            }

            try
            {
                var context = await listener.GetContextAsync().ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();

                var requestUrl = context.Request.Url;
                var responseHtml = BuildResponseHtml(requestUrl);

                var buffer = Encoding.UTF8.GetBytes(responseHtml);
                context.Response.ContentType = "text/html; charset=utf-8";
                context.Response.ContentLength64 = buffer.Length;
                await context.Response.OutputStream.WriteAsync(buffer, 0, buffer.Length, cancellationToken).ConfigureAwait(false);
                context.Response.Close();

                tcs.TrySetResult(requestUrl);
            }
            catch (OperationCanceledException)
            {
                tcs.TrySetResult(null);
            }
            catch (ObjectDisposedException)
            {
                tcs.TrySetResult(null);
            }
            catch (Exception)
            {
                tcs.TrySetResult(null);
            }
        }

        private static string BuildResponseHtml(Uri? requestUrl)
        {
            if (requestUrl == null)
                return BuildErrorHtml("Невідома помилка");

            var code = GetQueryParameter(requestUrl, "code");
            var errorCode = GetQueryParameter(requestUrl, "error");
            var errorDescription = GetQueryParameter(requestUrl, "error_description");

            if (!string.IsNullOrWhiteSpace(code))
                return BuildSuccessHtml();

            if (string.Equals(errorCode, "access_denied", StringComparison.OrdinalIgnoreCase))
                return BuildCancelledHtml();

            return BuildErrorHtml(errorDescription ?? errorCode ?? "Невідома помилка");
        }

        private static string? GetQueryParameter(Uri url, string key)
        {
            var query = HttpUtility.ParseQueryString(url.Query);
            var value = query.Get(key);
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }

        private static int FindAvailablePort()
        {
            var random = new Random();
            const int minPort = 49152;
            const int maxPort = 65535;
            const int maxAttempts = 20;

            for (int i = 0; i < maxAttempts; i++)
            {
                var port = random.Next(minPort, maxPort + 1);
                if (IsPortAvailable(port))
                    return port;
            }

            throw new InvalidOperationException("Unable to find an available loopback port.");
        }

        private static bool IsPortAvailable(int port)
        {
            var listener = new HttpListener();
            listener.Prefixes.Add($"http://127.0.0.1:{port}/auth/callback/");

            try
            {
                listener.Start();
                listener.Stop();
                return true;
            }
            catch (HttpListenerException)
            {
                return false;
            }
            finally
            {
                listener.Close();
            }
        }

        private static string BuildSuccessHtml()
        {
            return @"<!DOCTYPE html>
<html>
<head>
<meta charset='utf-8'>
<title>SCLOC-Verse — Вхід</title>
<style>
body { font-family: Arial, sans-serif; background: #0f2c3e; color: #fff; text-align: center; padding-top: 80px; }
h1 { color: #6db9f8; }
p { opacity: 0.8; }
</style>
</head>
<body>
<h1>Вхід успішний</h1>
<p>Можете закрити це вікно та повернутися до SCLOC-Verse.</p>
</body>
</html>";
        }

        private static string BuildCancelledHtml()
        {
            return @"<!DOCTYPE html>
<html>
<head>
<meta charset='utf-8'>
<title>SCLOC-Verse — Вхід</title>
<style>
body { font-family: Arial, sans-serif; background: #0f2c3e; color: #fff; text-align: center; padding-top: 80px; }
h1 { color: #6db9f8; }
p { opacity: 0.8; }
</style>
</head>
<body>
<h1>Вхід скасовано</h1>
<p>Можете закрити це вікно та повернутися до SCLOC-Verse.</p>
</body>
</html>";
        }

        private static string BuildErrorHtml(string error)
        {
            var encoded = WebUtility.HtmlEncode(error);
            return $@"<!DOCTYPE html>
<html>
<head>
<meta charset='utf-8'>
<title>SCLOC-Verse — Помилка входу</title>
<style>
body {{ font-family: Arial, sans-serif; background: #2a0f0f; color: #fff; text-align: center; padding-top: 80px; }}
h1 {{ color: #ff6b6b; }}
p {{ opacity: 0.8; }}
</style>
</head>
<body>
<h1>Помилка входу</h1>
<p>{encoded}</p>
</body>
</html>";
        }
    }
}

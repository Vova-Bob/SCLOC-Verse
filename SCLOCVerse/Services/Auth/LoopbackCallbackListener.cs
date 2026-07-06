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
    /// Повертає callback URL з authorization code та брендовану HTML-сторінку статусу.
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

                var response = context.Response;
                response.ContentType = "text/html; charset=utf-8";
                response.ContentLength64 = buffer.Length;
                response.KeepAlive = false;
                response.Headers.Add("Cache-Control", "no-store, no-cache, must-revalidate, max-age=0");
                response.Headers.Add("Pragma", "no-cache");
                response.Headers.Add("Expires", "0");
                response.Headers.Add("Referrer-Policy", "no-referrer");
                response.Headers.Add("X-Content-Type-Options", "nosniff");
                response.Headers.Add("X-Frame-Options", "DENY");
                response.Headers.Add("Content-Security-Policy",
                    "default-src 'none'; style-src 'unsafe-inline'; img-src data: svg-sprite; frame-ancestors 'none'; base-uri 'none'; form-action 'none'");

                await response.OutputStream.WriteAsync(buffer, 0, buffer.Length, cancellationToken).ConfigureAwait(false);
                response.Close();

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
            return BuildHtml(
                title: "SCLOC-Verse — Авторизацію завершено",
                statusTitle: "Авторизацію завершено",
                statusMessage: "Вхід до SCLOC-Verse виконано успішно." +
                    "<br><br><strong class=\"action\">Можете закрити цю вкладку браузера.</strong>" +
                    "<br>Якщо застосунок не відкрився — поверніться до SCLOC-Verse вручну.",
                statusKind: "success");
        }

        private static string BuildCancelledHtml()
        {
            return BuildHtml(
                title: "SCLOC-Verse — Вхід скасовано",
                statusTitle: "Вхід скасовано",
                statusMessage: "Авторизацію через Discord було скасовано. Можете закрити цю вкладку браузера.",
                statusKind: "cancelled");
        }

        private static string BuildErrorHtml(string error)
        {
            var encoded = WebUtility.HtmlEncode(error);
            return BuildHtml(
                title: "SCLOC-Verse — Помилка входу",
                statusTitle: "Помилка входу",
                statusMessage: encoded,
                statusKind: "error");
        }

        private static string BuildHtml(string title, string statusTitle, string statusMessage, string statusKind)
        {
            var icon = statusKind switch
            {
                "success" => @"<svg class=""status-icon status-icon-large"" viewBox=""0 0 24 24"" aria-hidden=""true""><path fill=""currentColor"" d=""M12 2C6.48 2 2 6.48 2 12s4.48 10 10 10 10-4.48 10-10S17.52 2 12 2zm-1 15l-4-4 1.41-1.41L11 14.17l6.59-6.59L19 9l-8 8z""/></svg>",
                "cancelled" => @"<svg class=""status-icon"" viewBox=""0 0 24 24"" aria-hidden=""true""><path fill=""currentColor"" d=""M12 2C6.48 2 2 6.48 2 12s4.48 10 10 10 10-4.48 10-10S17.52 2 12 2zm5 13.59L15.59 17 12 13.41 8.41 17 7 15.59 10.59 12 7 8.41 8.41 7 12 10.59 15.59 7 17 8.41 13.41 12 17 15.59z""/></svg>",
                _ => @"<svg class=""status-icon"" viewBox=""0 0 24 24"" aria-hidden=""true""><path fill=""currentColor"" d=""M12 2C6.48 2 2 6.48 2 12s4.48 10 10 10 10-4.48 10-10S17.52 2 12 2zm1 15h-2v-2h2v2zm0-4h-2V7h2v6z""/></svg>"
            };

            var titleColor = statusKind == "error" ? "var(--error)" : "var(--text)";
            var iconColor = statusKind == "error" ? "var(--error)" : "var(--glow)";
            var favicon = BuildInlineSvgFavicon();
            var logo = $"<img class=\"logo\" src=\"{OAuthHtmlAssets.LogoDataUri.Value}\" alt=\"SCLOC-Verse\" aria-label=\"SCLOC-Verse logo\">";

            var html = new StringBuilder();
            html.AppendLine("<!DOCTYPE html>");
            html.AppendLine("<html lang=\"uk\">");
            html.AppendLine("<head>");
            html.AppendLine("<meta charset=\"utf-8\">");
            html.AppendLine("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">");
            html.Append("<title>").Append(WebUtility.HtmlEncode(title)).AppendLine("</title>");
            html.Append("<link rel=\"icon\" type=\"image/svg+xml\" href=\"").Append(favicon).AppendLine("\">");
            html.AppendLine("<style>");
            html.AppendLine(":root {");
            html.AppendLine("  --bg: #0A1D29;");
            html.AppendLine("  --card-bg: rgba(15, 36, 50, 0.82);");
            html.AppendLine("  --border: rgba(109, 185, 248, 0.35);");
            html.AppendLine("  --glow: #6DB9F8;");
            html.AppendLine("  --text: #FFFFFF;");
            html.AppendLine("  --text-muted: rgba(255, 255, 255, 0.72);");
            html.AppendLine("  --error: #FF6B6B;");
            html.AppendLine("  --font: 'Segoe UI', Arial, sans-serif;");
            html.AppendLine("}");
            html.AppendLine("* { box-sizing: border-box; margin: 0; padding: 0; }");
            html.AppendLine("html, body { height: 100%; }");
            html.AppendLine("body {");
            html.AppendLine("  font-family: var(--font);");
            html.AppendLine("  background: var(--bg);");
            html.AppendLine("  color: var(--text);");
            html.AppendLine("  display: flex;");
            html.AppendLine("  align-items: center;");
            html.AppendLine("  justify-content: center;");
            html.AppendLine("  min-height: 100vh;");
            html.AppendLine("  padding: 24px;");
            html.AppendLine("  -webkit-font-smoothing: antialiased;");
            html.AppendLine("}");
            html.AppendLine(".card {");
            html.AppendLine("  width: 100%;");
            html.AppendLine("  max-width: 420px;");
            html.AppendLine("  background: var(--card-bg);");
            html.AppendLine("  border: 1px solid var(--border);");
            html.AppendLine("  border-radius: 16px;");
            html.AppendLine("  padding: 40px 32px 32px;");
            html.AppendLine("  text-align: center;");
            html.AppendLine("  box-shadow: 0 0 40px rgba(109, 185, 248, 0.12), inset 0 1px 0 rgba(255,255,255,0.06);");
            html.AppendLine("  backdrop-filter: blur(10px);");
            html.AppendLine("}");
            html.AppendLine(".logo {");
            html.AppendLine("  width: 132px;");
            html.AppendLine("  height: 132px;");
            html.AppendLine("  margin: 0 auto 24px;");
            html.AppendLine("  display: block;");
            html.AppendLine("  object-fit: contain;");
            html.AppendLine("  filter: drop-shadow(0 0 18px rgba(109, 185, 248, 0.45));");
            html.AppendLine("}");
            html.AppendLine(".status-icon {");
            html.AppendLine("  width: 56px;");
            html.AppendLine("  height: 56px;");
            html.AppendLine("  margin: 0 auto 18px;");
            html.Append("  color: ").Append(iconColor).AppendLine(";");
            html.AppendLine("}");
            html.AppendLine(".status-icon-large {");
            html.AppendLine("  width: 80px;");
            html.AppendLine("  height: 80px;");
            html.AppendLine("  margin: 0 auto 24px;");
            html.AppendLine("  filter: drop-shadow(0 0 18px rgba(109, 185, 248, 0.45));");
            html.AppendLine("}");
            html.AppendLine("h1 {");
            html.AppendLine("  font-size: 22px;");
            html.AppendLine("  font-weight: 600;");
            html.AppendLine("  margin-bottom: 12px;");
            html.Append("  color: ").Append(titleColor).AppendLine(";");
            html.AppendLine("}");
            html.AppendLine(".message {");
            html.AppendLine("  font-size: 15px;");
            html.AppendLine("  line-height: 1.55;");
            html.AppendLine("  color: var(--text-muted);");
            html.AppendLine("}");
            html.AppendLine(".message strong {");
            html.AppendLine("  color: var(--text);");
            html.AppendLine("  font-weight: 600;");
            html.AppendLine("}");
            html.AppendLine(".message .action {");
            html.AppendLine("  color: var(--glow);");
            html.AppendLine("  font-weight: 600;");
            html.AppendLine("}");
            html.AppendLine(".message .action::before {");
            html.AppendLine("  content: '👉 ';");
            html.AppendLine("}");
            html.AppendLine("@media (max-width: 480px) {");
            html.AppendLine("  .card { padding: 32px 24px; }");
            html.AppendLine("  h1 { font-size: 20px; }");
            html.AppendLine("  .status-icon-large { width: 64px; height: 64px; }");
            html.AppendLine("}");
            html.AppendLine("@media (prefers-reduced-motion: reduce) {");
            html.AppendLine("  .status-icon { animation: none; }");
            html.AppendLine("}");
            html.AppendLine("</style>");
            html.AppendLine("</head>");
            html.AppendLine("<body>");
            html.AppendLine("<div class=\"card\">");
            html.AppendLine(logo);
            html.AppendLine(icon);
            html.Append("<h1>").Append(WebUtility.HtmlEncode(statusTitle)).AppendLine("</h1>");
            html.Append("<p class=\"message\">").Append(statusMessage).AppendLine("</p>");
            html.AppendLine("</div>");
            html.AppendLine("</body>");
            html.AppendLine("</html>");

            return html.ToString();
        }

        private static string BuildInlineSvgFavicon()
        {
            // SVG-іконка у форматі data: URI — нуль зовнішніх запитів.
            var svg = "<svg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 32 32'>" +
                "<rect width='32' height='32' rx='6' fill='%230A1D29'/>" +
                "<path d='M16 4 L28 28 H4 Z' fill='none' stroke='%236DB9F8' stroke-width='2.5' stroke-linejoin='round'/>" +
                "<circle cx='16' cy='14' r='3' fill='%236DB9F8'/>" +
                "</svg>";
            return "data:image/svg+xml;utf8," + svg;
        }


    }
}

using Newtonsoft.Json;
using SCLOCVerse.Helpers;
using SCLOCVerse.Interfaces;
using SCLOCVerse.Models.ApplicationInstance;
using System;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SCLOCVerse.Services.ApplicationInstance
{
    /// <summary>
    /// Реалізація єдиного екземпляра через Mutex + Named Pipe.
    ///
    /// Перший процес:
    ///   - створює Mutex (IsFirstInstance == true)
    ///   - StartServerAsync піднімає NamedPipeServerStream у фоновому потоці
    ///   - при отриманні команди → подія CommandReceived (у потік старту)
    ///
    /// Другий процес:
    ///   - Mutex уже зайнятий (IsFirstInstance == false)
    ///   - SignalExistingInstanceAsync підключається клієнтом, шле JSON-команду
    ///   - повертає true при успіху, false при таймауті/помилці
    /// </summary>
    public class ApplicationInstanceService : IApplicationInstanceService
    {
        private readonly Mutex _mutex;
        private readonly bool _ownsMutex;
        private readonly bool _isFirstInstance;

        private NamedPipeServerStream? _serverStream;
        private CancellationTokenSource? _serverCts;
        private Task? _serverLoopTask;
        private bool _disposed;

        public ApplicationInstanceService()
        {
            // createdNew == true → ми перший, власник Mutex.
            // createdNew == false → Mutex уже існує, ми другий процес.
            _mutex = new Mutex(initiallyOwned: true, UpdateConstants.SingleInstanceMutexName, out _ownsMutex);
            _isFirstInstance = _ownsMutex;
        }

        public bool IsFirstInstance => _isFirstInstance;

        public event EventHandler<InstanceCommand>? CommandReceived;

        /// <summary>
        /// Активувати головне вікно поточного процесу. Універсальний метод — не знає
        /// про джерело події (Toast / URL / Tray через інший маршрут). Використовує
        /// вже існуючий маршрут CommandReceived з Kind=Show, що його обробляє
        /// MainWindow (Dispatcher.BeginInvoke(Show + Activate)).
        /// </summary>
        public void ShowMainWindow()
        {
            CommandReceived?.Invoke(this, new InstanceCommand { Kind = InstanceCommandKind.Show });
        }

        /// <summary>
        /// Показати головне вікно та перейти на вкладку Assistant (L.I.A).
        /// Викликається при кліку на OS Toast з source=lia (Етап F).
        /// </summary>
        public void ShowLiaAssistant()
        {
            CommandReceived?.Invoke(this, new InstanceCommand { Kind = InstanceCommandKind.ShowLiaAssistant });
        }

        public Task StartServerAsync(CancellationToken cancellationToken = default)
        {
            if (!_isFirstInstance)
                throw new InvalidOperationException(
                    "StartServerAsync може викликати лише перший екземпляр (IsFirstInstance == true).");

            if (_serverStream != null)
                return Task.CompletedTask; // Ідемпотентний: сервер уже запущений.

            _serverCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            // Сервер працює у фоновому потоці — не блокуємо UI.
            _serverLoopTask = Task.Run(() => ServerLoopAsync(_serverCts.Token), _serverCts.Token);

            return Task.CompletedTask;
        }

        /// <summary>
        /// Цикл прийому підключень. Кожне підключення обробляється послідовно
        /// (один клієнт за раз — стандартний патерн для single-instance IPC,
        /// як у ShareX/PowerToys).
        /// </summary>
        private async Task ServerLoopAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                // Кожну ітерацію створюємо новий server-instance: Named Pipe дозволяє
                // кілька серверів з одним іменем, але ми тримаємо один активний.
                _serverStream = new NamedPipeServerStream(
                    UpdateConstants.SingleInstancePipeName,
                    PipeDirection.InOut,
                    maxNumberOfServerInstances: 1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous);

                try
                {
                    await _serverStream.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);

                    var command = await ReadCommandAsync(_serverStream, cancellationToken).ConfigureAwait(false);
                    if (command != null)
                    {
                        // Підтверджуємо клієнту, що команда прийнята.
                        await WriteAckAsync(_serverStream, cancellationToken).ConfigureAwait(false);

                        // Маршалінг події в потік, що викликав StartServerAsync,
                        // виконує сам підписник (через Dispatcher) — тут лишаємо синхронне
                        // підняття події, бо Pipe-потік не повинен знати про UI.
                        CommandReceived?.Invoke(this, command);
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception)
                {
                    // Одинична помилка обробки клієнта не повинна валити сервер.
                    // Наступна ітерація підніме новий stream.
                }
                finally
                {
                    _serverStream.Dispose();
                    _serverStream = null;
                }
            }
        }

        public async Task<bool> SignalExistingInstanceAsync(
            InstanceCommand command,
            CancellationToken cancellationToken = default)
        {
            if (_isFirstInstance)
                throw new InvalidOperationException(
                    "SignalExistingInstanceAsync має викликати лише другий процес.");

            using var client = new NamedPipeClientStream(
                ".",
                UpdateConstants.SingleInstancePipeName,
                PipeDirection.InOut,
                PipeOptions.Asynchronous);

            try
            {
                await client.ConnectAsync(
                    (int)UpdateConstants.SingleInstanceConnectTimeout.TotalMilliseconds,
                    cancellationToken).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                return false;
            }
            catch (IOException)
            {
                // Перший процес не підняв pipe (краш або ще не готовий).
                return false;
            }

            // Серіалізуємо команду в JSON (UTF-8) — AGENTS.md §P0.
            var json = JsonConvert.SerializeObject(command);
            var jsonBytes = Encoding.UTF8.GetBytes(json);

            try
            {
                await WriteFrameAsync(client, jsonBytes, cancellationToken).ConfigureAwait(false);

                // Чекаємо підтвердження від сервера з таймаутом.
                using var responseCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                responseCts.CancelAfter(UpdateConstants.SingleInstanceResponseTimeout);
                return await ReadAckAsync(client, responseCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return false;
            }
            catch (IOException)
            {
                return false;
            }
        }

        // ----- Frame helpers -----
        // Протокол: [4 байти довжини][payload байти]. Довжина — big-endian int32.
        // Це дозволяє уникнути плутанини з читанням потоку й не покладатись на
        // роздільники. AGENTS.md §P0: payload завжди UTF-8.

        private static async Task WriteFrameAsync(Stream stream, byte[] payload, CancellationToken ct)
        {
            var lengthHeader = BitConverter.GetBytes(payload.Length); // little-endian на Windows
            await stream.WriteAsync(lengthHeader, 0, 4, ct).ConfigureAwait(false);
            await stream.WriteAsync(payload, 0, payload.Length, ct).ConfigureAwait(false);
            await stream.FlushAsync(ct).ConfigureAwait(false);
        }

        private static async Task<byte[]?> ReadFrameAsync(Stream stream, CancellationToken ct)
        {
            var header = new byte[4];
            var read = await ReadExactAsync(stream, header, 4, ct).ConfigureAwait(false);
            if (read < 4)
                return null;

            var length = BitConverter.ToInt32(header, 0);
            if (length <= 0 || length > 1_000_000) // Захист від некоректної довжини (max 1 MB).
                return null;

            var payload = new byte[length];
            read = await ReadExactAsync(stream, payload, length, ct).ConfigureAwait(false);
            return read < length ? null : payload;
        }

        private static async Task<int> ReadExactAsync(Stream stream, byte[] buffer, int count, CancellationToken ct)
        {
            var total = 0;
            while (total < count)
            {
                var read = await stream.ReadAsync(buffer, total, count - total, ct).ConfigureAwait(false);
                if (read == 0)
                    break; // З'єднання закрите.
                total += read;
            }
            return total;
        }

        private static async Task<InstanceCommand?> ReadCommandAsync(NamedPipeServerStream stream, CancellationToken ct)
        {
            var frame = await ReadFrameAsync(stream, ct).ConfigureAwait(false);
            if (frame == null)
                return null;

            var json = Encoding.UTF8.GetString(frame);
            return JsonConvert.DeserializeObject<InstanceCommand>(json);
        }

        private static Task WriteAckAsync(Stream stream, CancellationToken ct)
            => WriteFrameAsync(stream, Encoding.UTF8.GetBytes("OK"), ct);

        private static async Task<bool> ReadAckAsync(Stream stream, CancellationToken ct)
        {
            var frame = await ReadFrameAsync(stream, ct).ConfigureAwait(false);
            if (frame == null)
                return false;
            var ack = Encoding.UTF8.GetString(frame);
            return ack == "OK";
        }

        public async ValueTask DisposeAsync()
        {
            if (_disposed)
                return;
            _disposed = true;

            // Зупиняємо pipe-сервер: скасовуємо токен → WaitForConnection кине
            // OperationCanceledException → цикл завершиться.
            if (_serverCts != null)
            {
                try { _serverCts.Cancel(); } catch { /* ignore */ }
                if (_serverLoopTask != null)
                {
                    try { await _serverLoopTask.ConfigureAwait(false); } catch { /* ignore */ }
                }
                _serverCts.Dispose();
                _serverCts = null;
            }

            _serverStream?.Dispose();
            _serverStream = null;

            // Звільняємо Mutex лише якщо ми його власник (перший процес).
            if (_ownsMutex)
            {
                try { _mutex.ReleaseMutex(); } catch { /* ignore */ }
            }
            _mutex.Dispose();
        }
    }
}

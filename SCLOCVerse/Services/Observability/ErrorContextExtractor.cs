using SCLOCVerse.Models.LiaModels;
using SCLOCVerse.Models.Observability;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;

namespace SCLOCVerse.Services.Observability
{
    /// <summary>
    /// Єдиний класифікатор помилок платформи (Конституція, Стаття 12 — Single Ingestion Contract).
    /// Перетворює виняток на структурований <see cref="TelemetryContext"/> через інспекцію
    /// ТИПУ та ВЛАСТИВОСТЕЙ об'єкта — а не аналіз тексту повідомлення.
    /// </summary>
    /// <remarks>
    /// Універсальний: Auth (Slice 2), Installation (Slice 3), Updater (Slice 4), LIA (Slice 5)
    /// використовують цей самий компонент. Окремих парсерів помилок не створювати.
    ///
    /// Заповнюються лише ІСНУЮЧІ поля: http_status, supabase_code, source, hresult, exception_type,
    /// error_message. detail — лише для того, що не має окремої колонки.
    ///
    /// Version-resilient: HTTP-статус і тіло відповіді Supabase SDK читаються через reflection
    /// (до властивостей Response/StatusCode/Content), тому не прив'язані до конкретної версії
    /// RestSharp/Supabase. Жоден шлях не кидає (Стаття 1).
    /// </remarks>
    public static class ErrorContextExtractor
    {
        /// <summary>
        /// Створює гарантовано валідний контекст для ручних Failed-подій (без exception).
        /// Для умовних перевірок, де виняток не кидається (напр. null OAuth URL, відсутній code).
        /// Гарантує заповнення Source та ExceptionType (Стаття 13 — Failed обов'язково має signal).
        /// </summary>
        public static TelemetryContext Create(string source, string exceptionType, string? errorMessage = null)
        {
            return new TelemetryContext
            {
                Source = source,
                ExceptionType = exceptionType,
                ErrorMessage = errorMessage
            };
        }

        /// <summary>Будує структурований контекст помилки з винятку (з обходом inner-ланцюга).</summary>
        public static TelemetryContext? Extract(Exception? exception)
        {
            if (exception is null)
                return null;

            try
            {
                var ctx = new TelemetryContext
                {
                    ExceptionType = exception.GetType().Name,
                    ErrorMessage = exception.Message
                };

                // Обходимо inner-ланцюг: найінформативніший виняток може бути вкладеним
                // (напр. HttpRequestException усередині Supabase-обгортки).
                for (var current = exception; current != null; current = current.InnerException)
                {
                    if (!ctx.HttpStatus.HasValue)
                        ctx.HttpStatus = TryGetHttpStatus(current);

                    if (string.IsNullOrEmpty(ctx.SupabaseCode))
                        ctx.SupabaseCode = TryGetSupabaseCode(current);

                    if (string.IsNullOrEmpty(ctx.Hresult))
                        ctx.Hresult = TryGetHresult(current);

                    var src = ClassifySource(current);
                    // Конкретніше джерело пріоритетніше за 'CLR'.
                    if (string.IsNullOrEmpty(ctx.Source) || (ctx.Source == "CLR" && src != "CLR"))
                        ctx.Source = src;
                }

                if (string.IsNullOrEmpty(ctx.Source))
                    ctx.Source = ClassifySource(exception);

                // Для LiaInstallException: заповнює detail forensic-контекстом + signal_name.
                ApplyLiaForensic(ctx, exception);

                return ctx;
            }
            catch (Exception ex)
            {
                // Стаття 1: ніколи не кидає. Повертаємо мінімальний контекст.
                Debug.WriteLine($"[Telemetry] ErrorContextExtractor failed: {ex.Message}");
                return new TelemetryContext
                {
                    ExceptionType = exception.GetType().Name,
                    ErrorMessage = exception.Message,
                    Source = "CLR"
                };
            }
        }

        // Джерело — за простором імен типу (структуровано, не за текстом).
        private static string ClassifySource(Exception ex)
        {
            var ns = ex.GetType().Namespace ?? string.Empty;
            if (ns.StartsWith("Supabase.Gotrue", StringComparison.Ordinal)) return "Supabase.Gotrue";
            if (ns.StartsWith("Supabase.Postgrest", StringComparison.Ordinal)) return "Supabase.Postgrest";
            if (ns.StartsWith("Supabase", StringComparison.Ordinal)) return "Supabase";
            if (ex is HttpRequestException or TaskCanceledException or TimeoutException) return "Network";
            if (ex is LiaInstallException) return "PowerShell";
            if (ex is COMException or Win32Exception) return "COM";
            return "CLR";
        }

        private static int? TryGetHttpStatus(Exception ex)
        {
            // .NET 5+: HttpRequestException.StatusCode — типізовано.
            if (ex is HttpRequestException hre && hre.StatusCode.HasValue)
                return (int)hre.StatusCode;

            // Supabase-винятки: Response.StatusCode (RestSharp/HttpResponse) — через reflection.
            var response = ReadProperty<object>(ex, "Response");
            if (response != null)
            {
                if (ToHttpStatus(ReadProperty<object>(response, "StatusCode")) is { } rs)
                    return rs;
            }

            // Прямий StatusCode на самому винятку.
            if (ToHttpStatus(ReadProperty<object>(ex, "StatusCode")) is { } ds)
                return ds;

            return null;
        }

        // Postgrest-помилки несуть code у тілі JSON (напр. 42501). Парсимо structured JSON, не regex.
        private static string? TryGetSupabaseCode(Exception ex)
        {
            if (!string.Equals(ClassifySource(ex), "Supabase.Postgrest", StringComparison.Ordinal))
                return null;

            var response = ReadProperty<object>(ex, "Response");
            var content = ReadProperty<string>(response, "Content") ?? ReadProperty<string>(response, "Body");
            if (string.IsNullOrWhiteSpace(content))
                return null;

            try
            {
                using var doc = JsonDocument.Parse(content);
                if (doc.RootElement.TryGetProperty("code", out var codeEl) && codeEl.ValueKind == JsonValueKind.String)
                    return codeEl.GetString();
            }
            catch
            {
                // Тіло не JSON або без code — best-effort.
            }

            return null;
        }

        // HRESULT лише для справжніх COM/Win32-кодів або L.I.A (PowerShell-forensic).
        private static string? TryGetHresult(Exception ex)
        {
            if (ex is LiaInstallException lia && !string.IsNullOrEmpty(lia.Hresult))
                return lia.Hresult;

            if (ex is COMException or Win32Exception)
            {
                var hr = unchecked((uint)ex.HResult);
                if (hr != 0)
                    return "0x" + hr.ToString("X8");
            }

            return null;
        }

        private static int? ToHttpStatus(object? value) => value switch
        {
            HttpStatusCode http => (int)http,
            int i => i,
            _ => null
        };

        // Для LiaInstallException: переносить forensic-властивості (PowerShell-phase, cert,
        // activity_id, appx_log) у detail + встановлює signal_name через HResultCatalog.
        private static void ApplyLiaForensic(TelemetryContext ctx, Exception ex)
        {
            if (ex is not LiaInstallException lia)
                return;

            ctx.Detail ??= new Dictionary<string, object?>();

            if (!string.IsNullOrEmpty(lia.Phase))
                ctx.Detail["phase"] = lia.Phase;
            ctx.Detail["retry_count"] = 0;
            ctx.Detail["powershell_exit_code"] = lia.ExitCode;

            if (!string.IsNullOrEmpty(lia.InstallerType))
                ctx.Detail["installer_type"] = lia.InstallerType;
            if (lia.CertificatePresent.HasValue)
                ctx.Detail["certificate_present"] = lia.CertificatePresent;
            if (!string.IsNullOrEmpty(lia.CertificateSubject))
                ctx.Detail["certificate_subject"] = lia.CertificateSubject;
            if (!string.IsNullOrEmpty(lia.CertificateThumbprint))
                ctx.Detail["certificate_thumbprint"] = lia.CertificateThumbprint;
            if (!string.IsNullOrEmpty(lia.ActivityId))
                ctx.Detail["activity_id"] = lia.ActivityId;

            // AppX Event Viewer dump — розгорнутий stack deployment-помилки. Локалізаційно-незалежний
            // на рівні кодів подій (EventID). Дозволяє Control Center діагностувати навіть якщо
            // $_.Exception.Message пошкоджено mojibake через OEM-кодування консолі.
            if (!string.IsNullOrEmpty(lia.AppxLog))
                ctx.Detail["appx_log"] = lia.AppxLog;

            // Структуроване символьне імʼя (не текстовий аналіз) → detail.signal_name.
            var symbol = HResultCatalog.ResolveSymbol(lia.Hresult);
            if (!string.IsNullOrEmpty(symbol))
                ctx.Detail["signal_name"] = symbol;
        }

        // Reflection-читання публічної властивості (null-safe, без кидків).
        private static TValue? ReadProperty<TValue>(object? obj, string name)
        {
            if (obj is null)
                return default;

            var prop = obj.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
            if (prop is null)
                return default;

            try
            {
                return (TValue?)prop.GetValue(obj);
            }
            catch
            {
                return default;
            }
        }
    }
}

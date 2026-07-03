using System;
using System.Collections.Generic;

namespace SCLOCVerse.Services.Observability
{
    /// <summary>
    /// Структурована таблиця відомих HRESULT → символьне імʼя (signal).
    /// Відображення за hex-кодом, не аналіз тексту повідомлення.
    /// </summary>
    public static class HResultCatalog
    {
        private static readonly Dictionary<string, string> Symbols = new(StringComparer.OrdinalIgnoreCase)
        {
            // Довіра сертифікату
            ["0x800B0109"] = "CERT_E_UNTRUSTEDROOT",
            ["0x800B010D"] = "CERT_E_EXPIRED",
            ["0x800B0110"] = "CERT_E_WRONG_USAGE",
            // Add-AppxPackage / MSIX-deployment
            ["0x80073CF0"] = "ERROR_INSTALL_OPEN_PACKAGE_FAILED",
            ["0x80073CF1"] = "ERROR_INSTALL_PACKAGE_NOT_FOUND",
            ["0x80073CF2"] = "ERROR_INSTALL_INVALID_PACKAGE",
            ["0x80073CF3"] = "ERROR_INSTALL_INSTALL_FAILED",
            ["0x80073CF4"] = "ERROR_INSTALL_OUT_OF_DISKSPACE",
            ["0x80073CF5"] = "ERROR_INSTALL_NETWORK_FAILURE",
            ["0x80073D02"] = "ERROR_INSTALL_STATE_DEPLOYMENT_FAILED",
            ["0x80073D05"] = "ERROR_INSTALL_EXTERNAL_FAILURE",
            ["0x80073D06"] = "ERROR_INSTALL_RESOLVE_DEPENDENCY_FAILED",
            ["0x80073D19"] = "ERROR_INSTALL_CANCEL",
            ["0x800F0918"] = "ERROR_INSTALL_OWNER_NOT_TRUSTED"
        };

        /// <summary>Повертає символьне імʼя для відомого HRESULT, або null.</summary>
        public static string? ResolveSymbol(string? hresult)
        {
            if (string.IsNullOrWhiteSpace(hresult))
                return null;

            return Symbols.TryGetValue(hresult, out var symbol) ? symbol : null;
        }
    }
}
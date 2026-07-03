using System.Text.RegularExpressions;

namespace SCLOCVerse.Services.Observability
{
    /// <summary>
    /// Єдине вузьке горло санітизації PII (Конституція, Стаття 4 — Zero PII).
    /// Жодна подія не потрапляє в чергу, минаючи цей клас.
    /// </summary>
    /// <remarks>
    /// Санітизація виконується ДО черги на клієнті (не на сервері).
    /// Slice 1: Application.Start не несе PII, але chokepoint існує з першої події
    /// і готовий для error/crash-подій наступних слайсів.
    /// </remarks>
    public static class PrivacySanitizer
    {
        // C:\Users\&lt;name&gt;\...  ->  C:\Users\*\...
        private static readonly Regex WindowsUserPath =
            new(@"([A-Za-z]:\\Users\\)[^\\]+", RegexOptions.Compiled);

        // \\server\Users\&lt;name&gt;\...  ->  \\server\Users\*\...
        private static readonly Regex UncUserPath =
            new(@"(\\\\[^\\]+\\Users\\)[^\\]+", RegexOptions.Compiled);

        // Authorization: Bearer ...
        private static readonly Regex BearerToken =
            new(@"(?i)bearer\s+[A-Za-z0-9\-._~+/=]+", RegexOptions.Compiled);

        // JWT (header.payload.signature, обидві частини починаються з eyJ).
        private static readonly Regex Jwt =
            new(@"eyJ[A-Za-z0-9_\-]+\.eyJ[A-Za-z0-9_\-]+\.[A-Za-z0-9_\-]+", RegexOptions.Compiled);

        /// <summary>Санітарить довільний рядок: маскує шляхи з іменем користувача, токени, JWT.</summary>
        public static string? Sanitize(string? value)
        {
            if (string.IsNullOrEmpty(value))
                return value;

            var v = WindowsUserPath.Replace(value, "${1}*");
            v = UncUserPath.Replace(v, "${1}*");
            v = BearerToken.Replace(v, "Bearer ***");
            v = Jwt.Replace(v, "***JWT***");
            return v;
        }
    }
}

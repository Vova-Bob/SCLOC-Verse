using System;
using System.IO;
using System.Text;
using System.Windows;

namespace SCLOCVerse.Services.Auth
{
    /// <summary>
    /// Брендовані ресурси для OAuth callback HTML-сторінки.
    /// Завантажує логотип із WPF-ресурсів і кешує як Data URI.
    /// Усі методи повертають готові до вбудовування в HTML значення.
    /// </summary>
    internal static class OAuthHtmlAssets
    {
        /// <summary>
        /// Логотип SCLOC-Verse у форматі data:image/png;base64,... для HTML img src.
        /// Якщо ресурс недоступний — повертає inline SVG fallback.
        /// </summary>
        public static readonly Lazy<string> LogoDataUri = new Lazy<string>(BuildLogoDataUri);

        private static string BuildLogoDataUri()
        {
            try
            {
                var uri = new Uri("pack://application:,,,/Images/sclocverse_logo.png", UriKind.Absolute);
                var streamInfo = Application.GetResourceStream(uri);

                if (streamInfo?.Stream == null)
                    return BuildInlineSvgLogo();

                using var stream = streamInfo.Stream;
                using var memoryStream = new MemoryStream();
                stream.CopyTo(memoryStream);
                var bytes = memoryStream.ToArray();
                var base64 = Convert.ToBase64String(bytes);

                return $"data:image/png;base64,{base64}";
            }
            catch
            {
                // Будь-який збій при роботі з ресурсами не повинен ламати OAuth.
                return BuildInlineSvgLogo();
            }
        }

        /// <summary>
        /// Аварійний inline SVG-логотип, якщо PNG-ресурс недоступний.
        /// </summary>
        private static string BuildInlineSvgLogo()
        {
            return "data:image/svg+xml;utf8,<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 200 200\" aria-label=\"SCLOC-Verse logo\">" +
                "<defs>" +
                "<linearGradient id=\"g\" x1=\"0%\" y1=\"0%\" x2=\"100%\" y2=\"100%\">" +
                "<stop offset=\"0%\" stop-color=\"%236DB9F8\"/>" +
                "<stop offset=\"100%\" stop-color=\"%232D9CFF\"/>" +
                "</linearGradient>" +
                "</defs>" +
                "<path d=\"M100 10 L180 55 V145 L100 190 L20 145 V55 Z\" fill=\"none\" stroke=\"url(%23g)\" stroke-width=\"6\" stroke-linejoin=\"round\"/>" +
                "<path d=\"M100 40 L145 65 V115 L100 140 L55 115 V65 Z\" fill=\"none\" stroke=\"%236DB9F8\" stroke-width=\"3\" stroke-linejoin=\"round\" opacity=\"0.7\"/>" +
                "<circle cx=\"100\" cy=\"100\" r=\"12\" fill=\"%236DB9F8\"/>" +
                "<path d=\"M100 88 V40 M100 112 V160 M88 100 H40 M112 100 H160\" stroke=\"%236DB9F8\" stroke-width=\"3\" stroke-linecap=\"round\" opacity=\"0.55\"/>" +
                "</svg>";
        }
    }
}

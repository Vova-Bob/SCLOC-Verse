using SCLOCVerse.Interfaces;
using SCLOCVerse.Models.OcrPlatform;

namespace SCLOCVerse.Services.OcrPlatform.Validation
{
    /// <summary>
    /// Базова реалізація <see cref="IResultValidator"/> (T5.1: Confidence Filter).
    /// Consensus Buffer (T5.2), Field Lock (T5.3) будуть додані в наступних задачах.
    ///
    /// Confidence Filter — відкидає matches з confidence &lt; options.MinConfidence.
    /// </summary>
    public sealed class ResultValidator : IResultValidator
    {
        /// <inheritdoc />
        public OcrResult Validate(string regionId, OcrResult result, long? cropFingerprint, OcrOptions options)
        {
            ArgumentNullException.ThrowIfNull(regionId);
            ArgumentNullException.ThrowIfNull(result);
            ArgumentNullException.ThrowIfNull(options);

            // T5.1: Confidence Filter — відкидає low-confidence matches.
            var filteredMatches = result.Matches
                .Where(m => m.Confidence >= options.MinConfidence)
                .ToList();

            // T5.2: Consensus Buffer — буде додано (majority vote 5 кадрів).
            // T5.3: Field Lock — буде додано (skip OCR if crop не змінюється).

            return new OcrResult { Matches = filteredMatches };
        }
    }
}

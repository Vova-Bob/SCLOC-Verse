using SCLOCVerse.Models.HangarTimer;

namespace SCLOCVerse.Services.HangarTimer
{
    /// <summary>
    /// Калькулятор циклу Executive Hangar. Використовується і оверлеєм, і карткою.
    /// </summary>
    public static class HangarCycleCalculator
    {
        public const int RedPhaseSeconds = 2 * 60 * 60;     // 2 години
        public const int GreenPhaseSeconds = 1 * 60 * 60;   // 1 година
        public const int BlackPhaseSeconds = 5 * 60;        // 5 хвилин
        public const int TotalCycleSeconds = RedPhaseSeconds + GreenPhaseSeconds + BlackPhaseSeconds;
        public const int LedCount = 5;

        /// <summary>
        /// Обчислює поточний стан циклу на основі часу старту.
        /// </summary>
        public static HangarCycleInfo Compute(long cycleStartMs)
        {
            long nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            int elapsedSeconds = (int)Math.Floor((nowMs - cycleStartMs) / 1000.0);
            if (elapsedSeconds < 0) elapsedSeconds = 0;
            int cyclePos = Mod(elapsedSeconds, TotalCycleSeconds);

            var ledStates = new HangarLightState[LedCount];
            for (int i = 0; i < ledStates.Length; i++)
                ledStates[i] = HangarLightState.Black;

            string statusMessage;
            string statusLine;
            string timerText;
            int activeLedCount = 0;

            if (cyclePos < RedPhaseSeconds)
            {
                int interval = RedPhaseSeconds / LedCount;
                for (int i = 0; i < LedCount; i++)
                {
                    bool active = cyclePos >= (i + 1) * interval;
                    ledStates[i] = active ? HangarLightState.Green : HangarLightState.Red;
                    if (active) activeLedCount = i + 1;
                }

                statusMessage = "Ангар зачинено";
                statusLine = "Відкриття через " + FormatHHMMSS(RedPhaseSeconds - cyclePos);
                timerText = FormatHHMMSS(RedPhaseSeconds - cyclePos);
            }
            else if (cyclePos < RedPhaseSeconds + GreenPhaseSeconds)
            {
                int timeSinceGreen = cyclePos - RedPhaseSeconds;
                int interval = GreenPhaseSeconds / LedCount;
                for (int i = 0; i < LedCount; i++)
                {
                    bool inactive = timeSinceGreen >= (5 - i) * interval;
                    ledStates[i] = inactive ? HangarLightState.Black : HangarLightState.Green;
                    if (!inactive) activeLedCount = 5 - i;
                }

                statusMessage = "Ангар відкрито";
                statusLine = "Перезапуск через " + FormatHHMMSS(GreenPhaseSeconds - timeSinceGreen);
                timerText = FormatHHMMSS(GreenPhaseSeconds - timeSinceGreen);
            }
            else
            {
                int sinceBlack = cyclePos - RedPhaseSeconds - GreenPhaseSeconds;
                for (int i = 0; i < LedCount; i++)
                    ledStates[i] = HangarLightState.Black;

                statusMessage = "Ангар перезавантажується";
                statusLine = "Перезапуск через " + FormatHHMMSS(BlackPhaseSeconds - sinceBlack);
                timerText = FormatHHMMSS(BlackPhaseSeconds - sinceBlack);
                activeLedCount = 0;
            }

            return new HangarCycleInfo
            {
                Phase = cyclePos < RedPhaseSeconds ? HangarCyclePhase.Closed
                      : cyclePos < RedPhaseSeconds + GreenPhaseSeconds ? HangarCyclePhase.Open
                      : HangarCyclePhase.Resetting,
                TimerText = timerText,
                StatusMessage = statusMessage,
                StatusLine = statusLine,
                LedStates = ledStates,
                ActiveLedCount = activeLedCount
            };
        }

        private static int Mod(int a, int m)
        {
            return (a % m + m) % m;
        }

        private static string FormatHHMMSS(int seconds)
        {
            int h = seconds / 3600;
            int m = (seconds % 3600) / 60;
            int s = seconds % 60;
            return $"{h:00}:{m:00}:{s:00}";
        }
    }
}

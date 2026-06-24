using SCLOCVerse.Interfaces;
using SCLOCVerse.Models.Auth;
using Supabase;
using Supabase.Gotrue;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace SCLOCVerse.Services.Auth
{
    /// <summary>
    /// Синхронізує список Discord-гільдій користувача у таблицю public.user_discord_guilds.
    /// Зберігає лише id + назву сервера для читабельної статистики в адмін-панелі.
    /// </summary>
    public sealed class DiscordGuildSyncService : IDiscordGuildSyncService
    {
        private const string DiscordBaseUrl = "https://discord.com/api";
        private const int PageSize = 200;
        private const int MaxPages = 5; // захист від нескінченної пагінації (до 1000 серверів)

        private static readonly HttpClient HttpClient = CreateHttpClient();

        private readonly Supabase.Client _supabase;

        public DiscordGuildSyncService(ISupabaseClientFactory clientFactory)
        {
            _supabase = clientFactory?.CreateClient() ?? throw new ArgumentNullException(nameof(clientFactory));
        }

        public async Task SyncUserGuildsAsync(Session session, CancellationToken cancellationToken = default)
        {
            if (session == null)
                return;

            var providerToken = session.ProviderToken;
            if (string.IsNullOrWhiteSpace(providerToken))
            {
                // Сесія без scope guilds або з протермінованим provider token — синхронізацію пропускаємо.
                return;
            }

            var user = session.User;
            var userIdString = user?.Id?.ToString();
            if (!Guid.TryParse(userIdString, out var userId))
                return;

            var guilds = await FetchGuildsAsync(providerToken, cancellationToken).ConfigureAwait(false);
            if (guilds == null)
                return;

            var now = DateTimeOffset.UtcNow;
            var rows = new List<UserDiscordGuild>(guilds.Count);
            foreach (var guild in guilds)
            {
                if (string.IsNullOrWhiteSpace(guild.Id))
                    continue;

                rows.Add(new UserDiscordGuild
                {
                    Id = Guid.NewGuid(),
                    UserId = userId,
                    DiscordGuildId = guild.Id,
                    GuildName = guild.Name,
                    SyncedAt = now
                });
            }

            // Повне оновлення: видаляємо старі рядки користувача й вставляємо актуальний список.
            // Owner-only RLS гарантує, що торкаємося лише власних рядків.
            // Значення фільтра передаємо рядком — Postgrest не приймає Guid як критерій.
            await _supabase
                .From<UserDiscordGuild>()
                .Filter("user_id", Supabase.Postgrest.Constants.Operator.Equals, userId.ToString())
                .Delete(cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            if (rows.Count > 0)
            {
                await _supabase
                    .From<UserDiscordGuild>()
                    .Insert(rows, null, cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Отримує актуальний список гільдій користувача з Discord API з пагінацією.
        /// Повертає null, якщо запит неможливий (відсутній scope / протермінований token / мережевий збій).
        /// </summary>
        private static async Task<List<DiscordGuildDto>?> FetchGuildsAsync(string providerToken, CancellationToken cancellationToken)
        {
            var all = new List<DiscordGuildDto>();

            try
            {
                string? after = null;
                for (var page = 0; page < MaxPages; page++)
                {
                    var url = $"{DiscordBaseUrl}/users/@me/guilds?limit={PageSize}";
                    if (!string.IsNullOrEmpty(after))
                        url += $"&after={after}";

                    using var request = new HttpRequestMessage(HttpMethod.Get, url);
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", providerToken);

                    using var response = await HttpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);

                    // 401/403 = відсутній scope guilds або протермінований provider token: мовчазно пропускаємо.
                    if (response.StatusCode == HttpStatusCode.Unauthorized ||
                        response.StatusCode == HttpStatusCode.Forbidden)
                    {
                        return null;
                    }

                    response.EnsureSuccessStatusCode();

                    await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                    var chunk = await JsonSerializer
                        .DeserializeAsync<List<DiscordGuildDto>>(stream, cancellationToken: cancellationToken)
                        .ConfigureAwait(false);

                    if (chunk == null || chunk.Count == 0)
                        break;

                    all.AddRange(chunk);

                    // Discord сортує за спаданням id; пагінація вперед через after=останній id.
                    after = chunk[^1].Id;
                    if (chunk.Count < PageSize)
                        break;
                }

                return all;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception)
            {
                // Мережевий збій Discord — пропускаємо синхронізацію, не ламаючи логін.
                return null;
            }
        }

        private static HttpClient CreateHttpClient()
        {
            var client = new HttpClient();
            client.DefaultRequestHeaders.UserAgent.ParseAdd("SCLOC-Verse");
            return client;
        }

        private sealed class DiscordGuildDto
        {
            [JsonPropertyName("id")]
            public string Id { get; set; } = string.Empty;

            [JsonPropertyName("name")]
            public string Name { get; set; } = string.Empty;
        }
    }
}

using System.Net.Http.Json;
using System.Text.Json;

namespace DribKrok.Telegram;

public sealed class TelegramFailure(int retryAfter = 3) : Exception("Telegram request failed")
{
    public int RetryAfter { get; } = Math.Clamp(retryAfter, 1, 60);
}

public sealed class TelegramApi(HttpClient http, string token)
{
    public async Task<JsonElement> Call(string method, object body, CancellationToken ct)
    {
        // Never log the request URI or exception: the URI contains the bot token.
        try
        {
            using var response = await http.PostAsJsonAsync($"https://api.telegram.org/bot{token}/{method}", body, ct);
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            var root = json.RootElement;
            if (!response.IsSuccessStatusCode || !root.TryGetProperty("ok", out var ok) || !ok.GetBoolean())
            {
                int retry = 3;
                if (root.TryGetProperty("parameters", out var p) && p.TryGetProperty("retry_after", out var r) && r.TryGetInt32(out int seconds)) retry = seconds;
                throw new TelegramFailure(retry);
            }
            return root.GetProperty("result").Clone();
        }
        catch (Exception e) when (e is HttpRequestException or JsonException or KeyNotFoundException or InvalidOperationException || e is TaskCanceledException && !ct.IsCancellationRequested)
        { throw new TelegramFailure(); }
    }
    public Task<JsonElement> Send(long chatId, Reply reply, CancellationToken ct) => Call("sendMessage",
        new { chat_id = chatId, text = reply.Text, reply_markup = reply.Buttons == null ? null : new { inline_keyboard = reply.Buttons } }, ct);
}

using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using DribKrok.Core;
using DribKrok.Telegram;

Console.OutputEncoding = Encoding.UTF8;
// run.cmd sets the repository as working directory. Existing environment wins.
if (File.Exists(".env")) foreach (var line in File.ReadLines(".env"))
{
    var entry = line.Trim();
    if (entry.StartsWith('#') || !entry.Contains('=')) continue;
    var pair = entry.Split('=', 2); var name = pair[0].Trim();
    if (name is not ("TELEGRAM_BOT_TOKEN" or "OPENAI_API_KEY" or "OPENAI_MODEL")) continue;
    if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable(name))) Environment.SetEnvironmentVariable(name, pair[1].Trim().Trim('"', '\''));
}
var token = Environment.GetEnvironmentVariable("TELEGRAM_BOT_TOKEN") ?? "";
if (!Regex.IsMatch(token, @"^\d+:[A-Za-z0-9_-]{20,}$"))
{
    Console.WriteLine("Створи бота через @BotFather → /newbot. Скопіюй .env.example у .env і запиши TELEGRAM_BOT_TOKEN. Докладніше: README.md.");
    return 1;
}
using var stop = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; stop.Cancel(); };
using var telegramHttp = new HttpClient { Timeout = TimeSpan.FromSeconds(40) };
using var aiHttp = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
var api = new TelegramApi(telegramHttp, token);
var bot = new Bot(new CoachClient(new LocalModel(), aiHttp, Environment.GetEnvironmentVariable("OPENAI_API_KEY"), Environment.GetEnvironmentVariable("OPENAI_MODEL")));
try
{
    await api.Call("getMe", new { }, stop.Token);
    var webhook = await api.Call("getWebhookInfo", new { }, stop.Token);
    if (!string.IsNullOrEmpty(webhook.GetProperty("url").GetString()))
    {
        Console.WriteLine("У бота вже налаштований webhook. Використай окремого бота або видали webhook у попередньому застосунку. Налаштування не змінено.");
        return 2;
    }
    Console.WriteLine("ДрібКрок запущено. Відкрий свого бота в Telegram і надішли /start. Зупинка: Ctrl+C. Запускай лише одну копію.");
    long offset = 0;
    while (!stop.IsCancellationRequested)
    {
        try
        {
            bot.Expire(DateTime.UtcNow);
            var updates = await api.Call("getUpdates", new { offset, timeout = 25, limit = 20, allowed_updates = new[] { "message", "callback_query" } }, stop.Token);
            foreach (var update in updates.EnumerateArray())
            {
                long id = update.GetProperty("update_id").GetInt64();
                if (id < offset) continue;
                // Commit offset before handling: a failed send must not apply an answer twice.
                offset = id + 1;
                try
                {
                    bool callback = update.TryGetProperty("callback_query", out var query);
                    JsonElement message;
                    string text;
                    if (callback)
                    {
                        await api.Call("answerCallbackQuery", new { callback_query_id = query.GetProperty("id").GetString() }, stop.Token);
                        if (!query.TryGetProperty("message", out message) || !query.TryGetProperty("data", out var data)) continue;
                        text = data.GetString() ?? "";
                    }
                    else
                    {
                        if (!update.TryGetProperty("message", out message)) continue;
                        if (message.TryGetProperty("from", out var sender) && sender.TryGetProperty("is_bot", out var isBot) && isBot.GetBoolean()) continue;
                        text = message.TryGetProperty("text", out var t) ? t.GetString() ?? "" : "/help";
                    }
                    var chat = message.GetProperty("chat");
                    long chatId = chat.GetProperty("id").GetInt64();
                    var reply = await bot.Handle(chatId, chat.GetProperty("type").GetString() == "private", text, callback, stop.Token);
                    if (reply != null) await api.Send(chatId, reply, stop.Token);
                }
                catch (TelegramFailure e) { Console.WriteLine("Не вдалося надіслати відповідь. У Telegram можна повторити /current."); await Task.Delay(TimeSpan.FromSeconds(e.RetryAfter), stop.Token); }
                catch (Exception e) when (e is JsonException or KeyNotFoundException or InvalidOperationException) { Console.WriteLine("Пропущено непідтримуване оновлення Telegram."); }
            }
        }
        catch (TelegramFailure e) { Console.WriteLine("Telegram тимчасово недоступний. Перевір мережу, токен і чи немає іншої копії бота. Повторюю підключення."); await Task.Delay(TimeSpan.FromSeconds(e.RetryAfter), stop.Token); }
    }
}
catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
catch (TelegramFailure) { Console.WriteLine("Не вдалося підключитися до Telegram. Перевір токен, мережу та README.md. Токен не виводиться в журнал."); return 3; }
return 0;

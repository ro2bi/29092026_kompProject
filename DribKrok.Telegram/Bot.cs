using System.Text.Json;
using DribKrok.Core;

namespace DribKrok.Telegram;

public record Button(string text, string callback_data);
public record Reply(string Text, Button[][]? Buttons = null);

// Used sequentially by the polling loop. Student state exists only in memory.
public sealed class Bot(CoachClient coach)
{
    private sealed class Session
    {
        public Lesson? Lesson;
        public bool Consent;
        public bool ConsentPending;
        public string Id = Guid.NewGuid().ToString("N")[..8];
        public DateTime Seen = DateTime.UtcNow;
    }
    private readonly Dictionary<long, Session> sessions = [];
    public int SessionCount => sessions.Count;
    public void Expire(DateTime now)
    {
        foreach (var id in sessions.Where(x => now - x.Value.Seen > TimeSpan.FromMinutes(30)).Select(x => x.Key).ToArray()) sessions.Remove(id);
    }
    private static Reply Levels() => new("Обери рівень. Це тренування додавання дробів, а не повна програма класу.",
        Curriculum.Levels.Select(x => new[] { new Button(x.ToString(), $"grade:{x.Grade}") }).ToArray());
    public const string Privacy = "Бот зберігає ID чату, прогрес і вибір ШІ лише в пам’яті, до 30 хвилин бездіяльності або перезапуску. /forget видаляє цей стан. Повідомлення проходять через Telegram; ця команда не видаляє історію Telegram. Не надсилай ім’я, контакти, паролі чи ключі. За замовчуванням пояснення аналізує локальна модель. Після /ai_on текст пояснення передається OpenAI (без ID чату); store=false не є гарантією відсутності службового зберігання провайдером. /ai_off вимикає наступні передачі.";
    public async Task<Reply?> Handle(long chatId, bool isPrivate, string text, bool callback = false, CancellationToken ct = default)
    {
        if (!isPrivate) return null;
        Expire(DateTime.UtcNow);
        if (text == "/forget") { sessions.Remove(chatId); return new("Прогрес і згоду на API видалено з пам’яті бота. Історія Telegram залишається. /start — почати знову."); }
        if (!sessions.TryGetValue(chatId, out var s))
        {
            if (sessions.Count >= 1000) return new("Бот зараз зайнятий. Спробуй пізніше.");
            sessions[chatId] = s = new();
        }
        s.Seen = DateTime.UtcNow;
        if (callback && text is "consent:yes" or "consent:no")
        {
            if (!s.ConsentPending) return new("Запит згоди застарів. /ai_on — налаштувати знову.");
            s.ConsentPending = false;
            return Consent(chatId, text == "consent:yes");
        }
        if (callback && text.StartsWith("grade:") && int.TryParse(text[6..], out var grade) && grade is >= 5 and <= 9)
        {
            s.Lesson = new(grade, Random.Shared.Next()); s.Id = Guid.NewGuid().ToString("N")[..8];
            return Render(s);
        }
        if (callback)
        {
            var parts = text.Split(':');
            if (parts.Length != 4 || s.Lesson == null || parts[0] != "step" || parts[1] != s.Id || parts[2] != s.Lesson.Revision.ToString())
                return new("Ця кнопка застаріла. /current — актуальне завдання.");
            text = "/" + parts[3];
        }
        switch (text.Split(' ')[0].Split('@')[0])
        {
            case "/start": case "/level": case "/new": return Levels();
            case "/privacy": return new(Privacy);
            case "/help": case "/rules": return new("ДрібКрок — тренер додавання дробів для рівнів 5–9 класів. Дві вправи покрокові, третя самостійна.\n\nНадсилай відповідь; пояснення можна додати після |, наприклад: 12 | шукаю спільне кратне. У самостійній вправі пояснення обов’язкове. Це приклад формату, не відповідь до твого завдання.\n\n/level — обрати клас\n/current — завдання\n/hint — підказка\n/next — наступна вправа\n/progress — план повторення\n/privacy — дані та API\n/ai_on — дозволити OpenAI\n/ai_off — локальна модель\n/forget — стерти прогрес\n\nОбчислення перевіряє код. ШІ лише визначає тип пояснення та може помилятися. Не надсилай персональні дані.");
            case "/ai_on":
                if (!coach.Configured) return new("OpenAI не налаштовано власником бота. Працює локальна модель.");
                s.ConsentPending = true;
                return new(Privacy + "\n\nДозволити передавати твої наступні пояснення OpenAI?", [[new("Так, дозволяю", "consent:yes"), new("Ні", "consent:no")]]);
            case "/ai_off": s.Consent = false; s.ConsentPending = false; return new("Передачу пояснень OpenAI вимкнено. Працює локальна модель.");
        }
        if (text.StartsWith('/'))
        {
            if (s.Lesson == null) return Levels();
            switch(text.Split(' ')[0].Split('@')[0])
            {
                case "/hint": s.Lesson.Hint(); break;
                case "/next": s.Lesson.Next(); break;
                case "/current": break;
                case "/progress": return new($"Правильних кроків: {s.Lesson.CorrectSteps}/7. Спроб: {s.Lesson.Attempts}. Підказок: {s.Lesson.Hints}.\n\n" + string.Join("\n", s.Lesson.Plan()));
                default: return new("Невідома команда. /help — інструкція.");
            }
            return Render(s);
        }
        if (s.Lesson == null) return Levels();
        if (text.Length > 600) return new("Скороти відповідь до 100 символів, а пояснення — до 500.");
        var input = text.Split(['|', '\n'], 2, StringSplitOptions.TrimEntries);
        var answer = input[0]; var reasoning = input.Length == 2 ? input[1] : "";
        if (input.Length == 1 && text.Any(char.IsLetter)) { answer = ""; reasoning = text; }
        if (answer.Length > 100 || reasoning.Length > 500) return new("Скороти відповідь до 100 символів, а пояснення — до 500.");
        if (!s.Lesson.Complete)
        {
            var prediction = await coach.Predict(reasoning, s.Consent, ct);
            ct.ThrowIfCancellationRequested();
            s.Lesson.Answer(answer, reasoning, prediction);
        }
        return Render(s);
    }
    public Reply Consent(long chatId, bool allow)
    {
        if (sessions.TryGetValue(chatId, out var s)) { s.Consent = allow && coach.Configured; s.Seen = DateTime.UtcNow; return new(s.Consent ? "Дозвіл збережено до завершення сесії. /ai_off — вимкнути." : "Працює локальна модель."); }
        return new("Сесія завершилась. /start — почати.");
    }
    private static Reply Render(Session s)
    {
        var l = s.Lesson!;
        if (l.Finished) return new("Тренування завершено!\n\n" + string.Join("\n", l.Plan()) + "\n\n/new — нові завдання");
        string key = $"step:{s.Id}:{l.Revision}:";
        return new($"{l.Grade} клас · Вправа {l.Round + 1}/3\n{l.Exercise.Context}\n{l.Exercise.Expression}\n\n{l.Message}\n\n{l.Question}\n\nВідповідь | пояснення\nШІ: {l.ModelSource}",
            l.Complete ? [[new("Далі →", key + "next")]] : [[new("Підказка", key + "hint"), new("Мій прогрес", key + "progress")]]);
    }
}

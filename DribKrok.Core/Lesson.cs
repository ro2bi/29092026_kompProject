namespace DribKrok.Core;

public record Exercise(int A, int B, int C, int D, string Context)
{
    public string Expression => $"{A}/{B} + " + (C < 0 ? $"({C}/{D})" : $"{C}/{D}");
    public int Lcm => B / MathTools.Gcd(B, D) * D;
}

public static class MathTools
{
    public static int Gcd(int a, int b) { while (b != 0) (a, b) = (b, a % b); return Math.Abs(a); }
    public static bool TryFraction(string text, out int n, out int d)
    {
        n = 0; d = 1;
        var parts = text.Trim().Split('/');
        return parts.Length is 1 or 2 && int.TryParse(parts[0], out n) &&
            (parts.Length == 1 || int.TryParse(parts[1], out d)) &&
            n is >= -10000 and <= 10000 && d is > 0 and <= 10000;
    }
    public static bool Equivalent(Exercise e, int n, int d) => (long)n * e.B * e.D == (long)d * (e.A * e.D + e.C * e.B);
}

public sealed class Lesson
{
    private readonly Exercise[] route;
    public int Grade { get; }
    public string LevelDescription => Curriculum.Get(Grade).Description;
    public Lesson(int grade = 6, int? seed = null)
    {
        Grade = grade;
        route = Curriculum.Route(grade, seed);
        Exercise = route[0];
        Message = grade == 5
            ? "Знаменники вже однакові. Яке число залишиться знаменником суми?"
            : "Почнімо з однакового розміру частинок. Яке число ділиться на обидва знаменники без остачі?";
    }
    public readonly SemaphoreSlim Gate = new(1, 1);
    public DateTime LastSeen { get; set; } = DateTime.UtcNow;
    public int Revision { get; private set; }
    public int Round { get; private set; }
    public int Stage { get; private set; }
    public int Denominator { get; private set; }
    public int Attempts { get; private set; }
    public int Hints { get; private set; }
    public int CorrectSteps { get; private set; }
    public bool Finished { get; private set; }
    public bool Transfer => Round == 2;
    public bool Complete => Finished || (Transfer ? Stage == 1 : Stage == 3);
    public string Message { get; private set; } = "Почнімо з однакового розміру частинок. Яке число ділиться на обидва знаменники без остачі?";
    public string Tone { get; private set; } = "neutral";
    public string ModelSource { get; set; } = "Ще не використано";
    public string ModelLabel { get; private set; } = "";
    public Dictionary<string, int> Gaps { get; } = new();
    public List<string> Work { get; } = new();
    public Exercise Exercise { get; private set; }

    public string Question => Finished ? "Тренування завершено" : Complete ? "Вправу розв’язано!" : Transfer
        ? "Самостійна перевірка: запиши суму дробом та коротко поясни свій спосіб."
        : Stage switch { 0 => "Який спільний знаменник обереш?", 1 => $"Зведи обидва дроби до знаменника {Denominator}. Запиши два нові чисельники через пробіл.", _ => "Додай отримані дроби. Яка сума? Запиши дріб, наприклад 3/5." };

    public object View() => new {
        grade = Grade, levelDescription = LevelDescription, revision = Revision, round = Round, stage = Stage, denominator = Denominator, transfer = Transfer,
        complete = Complete, finished = Finished, exercise = new { Exercise.A, Exercise.B, Exercise.C, Exercise.D, Exercise.Expression, Exercise.Context },
        question = Question, message = Message, tone = Tone, attempts = Attempts, hints = Hints,
        correctSteps = CorrectSteps, work = Work.ToArray(), modelSource = ModelSource, modelLabel = ModelLabel,
        plan = Plan(), gaps = Gaps.Select(x => new { label = GapName(x.Key), count = x.Value }).ToArray()
    };

    public string[] Plan()
    {
        List<string> plan = [];
        if (Gaps.ContainsKey("add_denominators")) plan.Add("Повтори: знаменник описує розмір частини. Порівняй половини та третини на смужках.");
        if (Gaps.ContainsKey("unscaled_numerator")) plan.Add("Потренуй рівні дроби: множ чисельник і знаменник на те саме число.");
        if (Gaps.ContainsKey("arithmetic")) plan.Add("Перевір додавання чисельників після зведення до спільного знаменника.");
        if (Gaps.ContainsKey("sign_error")) plan.Add("Повтори знаки: для різних знаків віднімай модулі та залишай знак більшого модуля; для однакових — додавай модулі й зберігай знак.");
        if (plan.Count == 0) plan.Add("Закріпи додавання дробів із різними знаменниками та поясни свій спосіб іншій людині.");
        plan.Add(Transfer && Complete ? "Спробуй наступного дня ще одну подібну задачу без підказок." : "Наприкінці виконай окрему вправу без покрокових підказок.");
        return plan.ToArray();
    }

    public static string GapName(string label) => label switch {
        "add_denominators" => "Додавання знаменників", "unscaled_numerator" => "Незмінений чисельник",
        "arithmetic" => "Обчислення суми", "common_denominator" => "Спільний знаменник",
        "answer_request" => "Запит готової відповіді", "sign_error" => "Знаки чисел", _ => "Потрібне уточнення" };

    private void Gap(string label) => Gaps[label] = Gaps.GetValueOrDefault(label) + 1;
    public void Answer(string answer, string reasoning, Prediction prediction)
    {
        if (Complete) return;
        Revision++; Attempts++; Tone = "retry"; ModelLabel = GapName(prediction.Label);
        ModelSource = prediction.Source;
        var e = Exercise;
        bool valid = false;
        string? evidence = null;
        if (!Transfer && Stage == 0)
        {
            valid = int.TryParse(answer.Trim(), out int d) && d is > 0 and <= 120 && d % e.B == 0 && d % e.D == 0;
            if (valid) { Denominator = d; Work.Add($"Спільний знаменник: {d}"); }
            else if (d == e.B + e.D) evidence = "add_denominators";
        }
        else if (!Transfer && Stage == 1)
        {
            var parts = answer.Trim().Split([' ', ',', ';'], StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 2 && int.TryParse(parts[0], out int a) && int.TryParse(parts[1], out int c))
            {
                valid = a == e.A * (Denominator / e.B) && c == e.C * (Denominator / e.D);
                if (valid) Work.Add($"Твоє зведення: {a}/{Denominator} + {c}/{Denominator}");
                else if ((Denominator != e.B && a == e.A) || (Denominator != e.D && c == e.C)) evidence = "unscaled_numerator";
            }
        }
        else if (MathTools.TryFraction(answer, out int n, out int d))
        {
            valid = MathTools.Equivalent(e, n, d);
            if (!valid && (long)n * (e.B + e.D) == (long)d * (e.A + e.C)) evidence = "add_denominators";
            else if (!valid && (e.A < 0 || e.C < 0) &&
                (MathTools.Equivalent(e, -n, d) || (long)n * e.B * e.D == (long)d * (Math.Abs(e.A) * e.D + Math.Abs(e.C) * e.B))) evidence = "sign_error";
            else if (!valid) evidence = "arithmetic";
        }
        if (valid && Transfer && reasoning.Trim().Length < 10)
        {
            Message = "Обчислення правильне. Тепер додай коротке пояснення свого способу (щонайменше 10 символів), щоб завершити вправу.";
            Tone = "neutral";
            return;
        }
        if (valid)
        {
            CorrectSteps++; Stage++; Tone = "success";
            if (Complete) { Work.Add($"Твоя відповідь: {answer.Trim()}"); Message = Transfer
                ? "Правильно! Ти самостійно розв’язав нову задачу. Обчислення перевірено кодом; пояснення не оцінюється як шкільна оцінка."
                : "Правильно! Ти самостійно дійшов до відповіді. Перенесімо цей спосіб на іншу задачу."; }
            else Message = "Цей крок правильний — перевірено обчисленням. " + Question;
            return;
        }
        if (Transfer)
        {
            if (evidence != null) Gap(evidence);
            Message = "Поки що сума не збігається. Перевір свій спосіб і спробуй ще раз. Це самостійна вправа, тому покрокових підказок тут немає.";
            return;
        }
        // An AI suggestion never advances a step or overrides arithmetic.
        string label = evidence ?? prediction.Label;
        if (label is "add_denominators" or "unscaled_numerator" or "arithmetic" or "sign_error") Gap(label);
        Message = label switch {
            "answer_request" => "Готовий результат не підкажу, але допоможу зробити наступний крок. " + Question,
            "add_denominators" => "Схоже, ти додаєш знаменники. Подумай: половина й третина — частинки однакового розміру? Як зробити їх однаковими?",
            "unscaled_numerator" => "Якщо кожну частинку поділити на дрібніші, їх стане більше. На скільки помножив знаменник? Що тоді потрібно зробити з чисельником?",
            "arithmetic" => "Після зведення розмір частинок однаковий. Які числа тепер треба додати, а яке залишити?",
            "sign_error" => "Перевір знаки чисельників. За однакових знаків додавай модулі й зберігай знак; за різних — відніми менший модуль від більшого. Який знак матиме сума?",
            _ => "Поки що цей крок не збігається з перевіркою. " + Question
        };
    }

    public void Hint()
    {
        if (Complete) return;
        Revision++;
        if (Transfer) { Message = "Спробуй завершити самостійно. Після вправи отримаєш план повторення."; return; }
        Hints++; Tone = "neutral";
        Message = Stage switch {
            0 => Hints % 2 == 1 ? "Випиши кілька кратних кожного знаменника. Яке число зустрічається в обох списках?" : "Кратні числа отримують множенням на 1, 2, 3… Знайди спільне число, не більше 120.",
            1 => "Поділи обраний спільний знаменник на початковий. Це множник і для чисельника. Зроби так для кожного дробу.",
            _ => Exercise.A < 0 || Exercise.C < 0
                ? "Додай нові чисельники з урахуванням знаків. За різних знаків порівняй модулі; спільний знаменник залиш."
                : "Додай нові чисельники, а спільний знаменник залиш. За бажанням скороти результат."
        };
    }

    public void Next()
    {
        if (!Complete || Finished) return;
        Revision++;
        if (Round == 2) { Finished = true; return; }
        Round++; Stage = 0; Denominator = 0; Work.Clear(); Tone = "neutral";
        Exercise = Round == 1 && Gaps.ContainsKey("unscaled_numerator")
            ? Curriculum.Remediation(Grade, route[0], route[2], route[1]) : route[Round];
        Message = Transfer ? "Тепер нова задача без підказок. Спробуй застосувати свій спосіб самостійно." : "Закріпимо спосіб на іншому прикладі. " + Question;
    }
}

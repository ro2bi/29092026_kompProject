namespace DribKrok.Core;

public record LearningLevel(int Grade, string Title, string Description, Exercise[] Exercises)
{
    public override string ToString() => $"{Grade} клас — {Title}";
}

// Grade is an approximate difficulty guide, not a claim to cover the whole curriculum.
public static class Curriculum
{
    public static IReadOnlyList<LearningLevel> Levels { get; } = new LearningLevel[]
    {
        new(5, "Прості дроби", "Додатні дроби з однаковими знаменниками.", [
            E(1,5,2,5, "Оля прочитала 1/5 книжки вранці та 2/5 увечері. Яку частину книжки прочитано?"),
            E(1,8,3,8), E(2,7,3,7), E(1,6,1,6), E(3,10,4,10), E(2,9,4,9), E(3,8,1,8), E(1,4,1,4)
        ]),
        new(6, "Різні знаменники", "Спільний знаменник і рівносильні дроби.", [
            E(1,3,1,4, "Оля пройшла 1/3 маршруту вранці та 1/4 — увечері. Яку частину маршруту вона пройшла за день?"),
            E(1,4,1,6, "Марта прочитала 1/4 книжки в суботу та 1/6 у неділю. Яку частину книжки прочитано?"),
            E(2,5,1,3), E(1,2,1,6), E(2,3,1,6), E(1,4,3,8), E(1,5,3,10), E(2,7,1,2)
        ]),
        new(7, "Зведення та скорочення", "Більші знаменники; сума може бути більшою за одиницю.", [
            E(5,6,3,8), E(7,10,2,15), E(5,12,7,18), E(3,4,5,8),
            E(7,9,5,6), E(5,8,7,12), E(4,5,7,10), E(11,15,7,20)
        ]),
        new(8, "Дроби зі знаками", "Додавання додатних і від’ємних дробів.", [
            E(-1,3,1,2, "Температура була −1/3 °C і підвищилася на 1/2 °C. Якою вона стала?"),
            E(3,4,-1,6), E(-2,5,1,3), E(-3,8,-1,4),
            E(1,6,-2,3), E(-3,5,3,10), E(5,6,-3,4), E(-1,2,1,2)
        ]),
        new(9, "Складніші знакові дроби", "Кілька перетворень, скорочення, додатна, від’ємна або нульова сума.", [
            E(-7,12,5,18), E(11,15,-7,20), E(-5,8,-7,12), E(7,9,-5,6),
            E(-11,14,3,7), E(13,20,-7,15), E(-9,10,11,15), E(-7,12,14,24)
        ])
    };

    private static Exercise E(int a, int b, int c, int d, string? context = null)
    {
        string right = c < 0 ? $"({c}/{d})" : $"{c}/{d}";
        return new(a,b,c,d,context ?? $"Обчисли {a}/{b} + {right}. Поясни, як перетворюєш дроби та додаєш чисельники.");
    }

    public static LearningLevel Get(int grade) => Levels.FirstOrDefault(x => x.Grade == grade)
        ?? throw new ArgumentOutOfRangeException(nameof(grade), "Підтримуються рівні 5–9 класів.");

    public static Exercise[] Route(int grade, int? seed)
    {
        var pool = Get(grade).Exercises.ToArray();
        if (seed.HasValue) new Random(seed.Value).Shuffle(pool);
        return pool.Take(3).ToArray();
    }

    public static Exercise Remediation(int grade, Exercise first, Exercise transfer, Exercise fallback)
    {
        return Get(grade).Exercises.FirstOrDefault(e => e != first && e != transfer &&
            e.B != e.D && (e.B % e.D == 0 || e.D % e.B == 0)) ?? fallback;
    }
}

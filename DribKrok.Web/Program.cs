using System.Collections.Concurrent;
using System.Threading.RateLimiting;
using DribKrok.Core;

// Environment variables take priority. Only the two documented names are loaded from .env.
foreach (var path in new[] { Path.Combine(Directory.GetCurrentDirectory(), ".env"), Path.Combine(Directory.GetCurrentDirectory(), "..", ".env") })
{
    if (!File.Exists(path)) continue;
    foreach (var line in File.ReadLines(path))
    {
        var pair = line.Split('=', 2);
        if (pair.Length == 2 && pair[0].Trim() is "OPENAI_API_KEY" or "OPENAI_MODEL" && string.IsNullOrEmpty(Environment.GetEnvironmentVariable(pair[0].Trim())))
            Environment.SetEnvironmentVariable(pair[0].Trim(), pair[1].Trim().Trim('"', '\''));
    }
}
var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls("http://127.0.0.1:5186");
builder.WebHost.ConfigureKestrel(o => o.Limits.MaxRequestBodySize = 4096);
builder.Logging.AddFilter("System.Net.Http.HttpClient", LogLevel.None);
builder.Services.AddSingleton<LocalModel>();
builder.Services.AddSingleton<AiCoach>();
builder.Services.AddHttpClient("ai", client => client.Timeout = TimeSpan.FromSeconds(15));
builder.Services.AddRateLimiter(options => {
    options.RejectionStatusCode = 429;
    options.AddPolicy("api", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "local", _ => new FixedWindowRateLimiterOptions {
            PermitLimit = 60, Window = TimeSpan.FromMinutes(1), QueueLimit = 0
        }));
});
var app = builder.Build();
var sessions = new ConcurrentDictionary<string, Lesson>();
using var cleanup = new Timer(_ => {
    foreach (var item in sessions)
        if (DateTime.UtcNow - item.Value.LastSeen > TimeSpan.FromMinutes(30)) sessions.TryRemove(item.Key, out var removed);
}, null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
app.Use(async (ctx, next) => {
    ctx.Response.Headers["Content-Security-Policy"] = "default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self' data:; connect-src 'self'; frame-ancestors 'none'; base-uri 'none'; form-action 'self'";
    ctx.Response.Headers["X-Content-Type-Options"] = "nosniff";
    ctx.Response.Headers["Referrer-Policy"] = "no-referrer";
    if (ctx.Request.Path.StartsWithSegments("/api")) {
        ctx.Response.Headers.CacheControl = "no-store";
        // Browser requests from another origin must not consume the owner's API credits.
        if (ctx.Request.Headers.TryGetValue("Origin", out var origin) && origin != $"{ctx.Request.Scheme}://{ctx.Request.Host}") {
            ctx.Response.StatusCode = 403; return;
        }
        if (ctx.Request.Method == "POST" && !ctx.Request.HasJsonContentType()) {
            ctx.Response.StatusCode = 415; return;
        }
    }
    await next();
});
app.UseDefaultFiles(); app.UseStaticFiles(); app.UseRateLimiter();
var api = app.MapGroup("/api").RequireRateLimiting("api");
api.MapGet("/health", (AiCoach ai) => Results.Ok(new { ok = true, apiConfigured = ai.Configured, localModel = LocalModel.Version, trainingExamples = LocalModel.Training.Values.Sum(x => x.Length) }));
api.MapPost("/start", (HttpContext ctx) => {
    if (ctx.Request.Cookies.TryGetValue("drib-session", out var old)) sessions.TryRemove(old, out var removed);
    if (sessions.Count >= 1000) return Results.Problem("Забагато активних сесій. Спробуй пізніше.", statusCode: 503);
    var id = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(24));
    var lesson = new Lesson(); sessions[id] = lesson;
    ctx.Response.Cookies.Append("drib-session", id, new CookieOptions { HttpOnly = true, SameSite = SameSiteMode.Strict, IsEssential = true, Secure = ctx.Request.IsHttps, MaxAge = TimeSpan.FromMinutes(30) });
    return Results.Ok(lesson.View());
});
Lesson? Current(HttpContext ctx) {
    if (!ctx.Request.Cookies.TryGetValue("drib-session", out var id) || !sessions.TryGetValue(id, out var lesson)) return null;
    if (DateTime.UtcNow - lesson.LastSeen > TimeSpan.FromMinutes(30)) { sessions.TryRemove(id, out var removed); return null; }
    lesson.LastSeen = DateTime.UtcNow;
    ctx.Response.Cookies.Append("drib-session", id, new CookieOptions { HttpOnly = true, SameSite = SameSiteMode.Strict, IsEssential = true, Secure = ctx.Request.IsHttps, MaxAge = TimeSpan.FromMinutes(30) });
    return lesson;
}
api.MapGet("/state", async (HttpContext ctx) => {
    var lesson = Current(ctx); if (lesson == null) return Results.NotFound();
    await lesson.Gate.WaitAsync(ctx.RequestAborted);
    try { return Results.Ok(lesson.View()); } finally { lesson.Gate.Release(); }
});
api.MapPost("/answer", async (Submission data, HttpContext ctx, AiCoach ai) => {
    var lesson = Current(ctx); if (lesson == null) return Results.NotFound();
    if (data.Answer == null || data.Reasoning == null || data.Answer.Length > 100 || data.Reasoning.Length > 500 || (string.IsNullOrWhiteSpace(data.Answer) && string.IsNullOrWhiteSpace(data.Reasoning)))
        return Results.BadRequest(new { error = "Введи відповідь або пояснення. Максимум: 100 і 500 символів." });
    await lesson.Gate.WaitAsync(ctx.RequestAborted);
    try {
        if (data.Revision != lesson.Revision) return Results.Conflict(new { error = "Крок уже змінився. Онови сторінку." });
        if (!lesson.Complete) {
            var prediction = await ai.Predict(data.Reasoning, data.ApiConsent, ctx.RequestAborted);
            lesson.Answer(data.Answer, data.Reasoning, prediction);
        }
        return Results.Ok(lesson.View());
    } finally { lesson.Gate.Release(); }
});
api.MapPost("/hint", async (RevisionRequest data, HttpContext ctx) => {
    var lesson = Current(ctx); if (lesson == null) return Results.NotFound();
    await lesson.Gate.WaitAsync(ctx.RequestAborted);
    try { if (data.Revision != lesson.Revision) return Results.Conflict(); lesson.Hint(); return Results.Ok(lesson.View()); }
    finally { lesson.Gate.Release(); }
});
api.MapPost("/next", async (RevisionRequest data, HttpContext ctx) => {
    var lesson = Current(ctx); if (lesson == null) return Results.NotFound();
    await lesson.Gate.WaitAsync(ctx.RequestAborted);
    try { if (data.Revision != lesson.Revision) return Results.Conflict(); lesson.Next(); return Results.Ok(lesson.View()); }
    finally { lesson.Gate.Release(); }
});
api.MapPost("/reset", (HttpContext ctx) => {
    if (ctx.Request.Cookies.TryGetValue("drib-session", out var id)) sessions.TryRemove(id, out var removed);
    ctx.Response.Cookies.Delete("drib-session"); return Results.Ok(new { deleted = true });
});
api.MapGet("/plan", async (HttpContext ctx) => {
    var lesson = Current(ctx); if (lesson == null) return Results.NotFound();
    await lesson.Gate.WaitAsync(ctx.RequestAborted);
    try {
        if (!lesson.Finished) return Results.Conflict();
        var text = "ДрібКрок — мій план повторення\n\n" +
            $"3 вправи · {lesson.CorrectSteps} правильних кроків · {lesson.Attempts} спроб · {lesson.Hints} підказок.\n\n" +
            string.Join("\n", lesson.Plan().Select((p,i) => $"{i+1}. {p}")) +
            "\n\nЦе рекомендації для тренування, а не шкільна оцінка.";
        return Results.File(System.Text.Encoding.UTF8.GetBytes("\uFEFF" + text), "text/plain; charset=utf-8", "DribKrok-plan.txt");
    } finally { lesson.Gate.Release(); }
});
app.Run();
public record Submission(string? Answer, string? Reasoning, bool ApiConsent, int Revision);
public record RevisionRequest(int Revision);

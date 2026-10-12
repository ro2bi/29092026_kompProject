using DribKrok.Core;

// Provider is selected on the server; credentials never reach the browser.
public sealed class AiCoach(LocalModel local, IHttpClientFactory clients, IConfiguration config)
{
    public string Provider => (config["AI_PROVIDER"] ?? "gemini").Trim().ToLowerInvariant();
    public string ProviderName => Provider switch { "gemini" => "Google Gemini", "openai" => "OpenAI", _ => "Невідомий провайдер" };
    private string? Key => config[Provider == "gemini" ? "GEMINI_API_KEY" : "OPENAI_API_KEY"];
    private string? Model => Provider == "gemini" ? config["GEMINI_MODEL"] ?? "gemini-3.5-flash-lite" : config["OPENAI_MODEL"];
    public bool Configured => Provider is "gemini" or "openai" && !string.IsNullOrWhiteSpace(Key) && !string.IsNullOrWhiteSpace(Model);
    public const string Prompt = CoachClient.Prompt;
    public async Task<Prediction> Predict(string reasoning, bool consent, CancellationToken ct)
    {
        using var client = clients.CreateClient("ai");
        return await new CoachClient(local, client, Key, Model, Provider)
            .Predict(reasoning, consent, ct);
    }
}

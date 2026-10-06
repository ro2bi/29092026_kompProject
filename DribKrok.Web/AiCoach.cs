using DribKrok.Core;

// The same API client is used by the web and Windows Forms applications.
public sealed class AiCoach(LocalModel local, IHttpClientFactory clients, IConfiguration config)
{
    public bool Configured => !string.IsNullOrWhiteSpace(config["OPENAI_API_KEY"]) && !string.IsNullOrWhiteSpace(config["OPENAI_MODEL"]);
    public const string Prompt = CoachClient.Prompt;
    public async Task<Prediction> Predict(string reasoning, bool consent, CancellationToken ct)
    {
        using var client = clients.CreateClient("ai");
        return await new CoachClient(local, client, config["OPENAI_API_KEY"], config["OPENAI_MODEL"])
            .Predict(reasoning, consent, ct);
    }
}

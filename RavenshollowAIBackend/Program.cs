using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddCors(options =>
{
    options.AddPolicy("UnityWebGL", policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

var app = builder.Build();
app.UseCors("UnityWebGL");

app.MapPost("/api/chat", async (ChatRequest request) =>
{
    string? apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");

    if (string.IsNullOrWhiteSpace(apiKey))
        return Results.Ok(new ChatResponse("The connection is not available right now."));

    if (request.messages == null || request.messages.Count == 0)
        return Results.Ok(new ChatResponse("I am not sure what you mean, Detective."));

    // Safety limits
    List<AIMessage> messages = request.messages
        .Where(m => !string.IsNullOrWhiteSpace(m.role) && !string.IsNullOrWhiteSpace(m.content))
        .TakeLast(12)
        .ToList();

    foreach (AIMessage message in messages)
    {
        if (message.content.Length > 8000)
            message.content = message.content[..8000];
    }

    using HttpClient client = new HttpClient();
    client.DefaultRequestHeaders.Authorization =
        new AuthenticationHeaderValue("Bearer", apiKey);

    string model = string.IsNullOrWhiteSpace(request.model)
        ? "gpt-5-mini"
        : request.model;

    string? answer = await AskOpenAI(client, model, messages, 1000);

    if (string.IsNullOrWhiteSpace(answer))
    {
        Console.WriteLine("Empty answer. Retrying with higher token limit...");
        answer = await AskOpenAI(client, model, messages, 1600);
    }

    if (string.IsNullOrWhiteSpace(answer))
    {
        Console.WriteLine("OpenAI still returned empty answer.");
        answer = "Forgive me, Detective. I need a moment to gather my thoughts.";
    }

    return Results.Ok(new ChatResponse(answer));
});

app.Run();

static async Task<string?> AskOpenAI(
    HttpClient client,
    string model,
    List<AIMessage> messages,
    int maxCompletionTokens)
{
    var openAiRequest = new
    {
        model = model,
        max_completion_tokens = maxCompletionTokens,
        messages = messages
    };

    string json = JsonSerializer.Serialize(openAiRequest);

    using StringContent content = new StringContent(
        json,
        Encoding.UTF8,
        "application/json"
    );

    HttpResponseMessage response = await client.PostAsync(
        "https://api.openai.com/v1/chat/completions",
        content
    );

    string responseText = await response.Content.ReadAsStringAsync();

    Console.WriteLine("OpenAI response:");
    Console.WriteLine(responseText);

    if (!response.IsSuccessStatusCode)
        return null;

    using JsonDocument document = JsonDocument.Parse(responseText);

    JsonElement choice = document.RootElement
        .GetProperty("choices")[0];

    string finishReason = choice.TryGetProperty("finish_reason", out JsonElement finish)
        ? finish.GetString() ?? ""
        : "";

    string? answer = choice
        .GetProperty("message")
        .GetProperty("content")
        .GetString();

    if (string.IsNullOrWhiteSpace(answer))
    {
        Console.WriteLine("Empty content. Finish reason: " + finishReason);
        return null;
    }

    return answer.Trim();
}

public record ChatRequest(string model, List<AIMessage> messages);

public class AIMessage
{
    public string role { get; set; } = "";
    public string content { get; set; } = "";
}

public record ChatResponse(string answer);
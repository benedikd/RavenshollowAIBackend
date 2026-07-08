using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddCors(options =>
{
    options.AddPolicy("UnityWebGL", policy =>
    {
        policy
            .AllowAnyOrigin()
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
        return Results.Problem("OPENAI_API_KEY is missing on server.");

    using HttpClient client = new HttpClient();

    client.DefaultRequestHeaders.Authorization =
        new AuthenticationHeaderValue("Bearer", apiKey);

    var openAiRequest = new
    {
        model = request.model,
        max_completion_tokens = 700,
        messages = request.messages
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

    if (!response.IsSuccessStatusCode)
        return Results.Problem(responseText);

    using JsonDocument document = JsonDocument.Parse(responseText);

    string answer = document
        .RootElement
        .GetProperty("choices")[0]
        .GetProperty("message")
        .GetProperty("content")
        .GetString() ?? "";

    return Results.Ok(new ChatResponse(answer));
});

app.Run();

public record ChatRequest(string model, List<AIMessage> messages);

public record AIMessage(string role, string content);

public record ChatResponse(string answer);

using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

/*
az login
az group create -n my-rg -l eastus
az appservice plan create -g my-rg -n myplan --is-linux --sku B1
az webapp create -g my-rg -p myplan -n my-unique-app-name --runtime "DOTNET|8.0"
az webapp config appsettings set -g my-rg -n my-unique-app-name --settings OPENAI_API_KEY="your_api_key_here"
az webapp restart -g my-rg -n my-unique-app-name
*/

namespace ScratchAiProxy;

public record ScratchChatRequest(string Prompt);
public record ScratchChatResponse(string Reply);

public class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        // CORS عشان تقدر تستدعيه من Scratch/Pishi
        builder.Services.AddCors(options =>
        {
            options.AddDefaultPolicy(policy =>
                policy.AllowAnyOrigin()
                      .AllowAnyHeader()
                      .AllowAnyMethod());
        });

        builder.Services.AddHttpClient();

        var app = builder.Build();

        app.UseCors();

        app.MapPost("/api/chat", async (
            ScratchChatRequest request,
            IHttpClientFactory httpClientFactory,
            IConfiguration config) =>
        {
            var apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY")
                        ?? config["OpenAI:ApiKey"];

            if (string.IsNullOrWhiteSpace(apiKey))
            {
                return Results.Problem("OpenAI API key is not configured.");
            }

            if (string.IsNullOrWhiteSpace(request.Prompt))
            {
                return Results.BadRequest("Prompt is required.");
            }

            var client = httpClientFactory.CreateClient();
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", apiKey);

            var openAiRequest = new
            {
                model = "gpt-4o-mini", // غيّريها حسب ما هو متاح في حسابك
                messages = new[]
                {
                    new { role = "system", content = "You are a tutor helping 6th grade students learn Scratch programming in simple Arabic." },
                    new { role = "user", content = request.Prompt }
                },
                max_tokens = 250
            };

            var url = "https://api.openai.com/v1/chat/completions";

            var json = JsonSerializer.Serialize(openAiRequest);
            var httpRequest = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };

            var response = await client.SendAsync(httpRequest);

            if (!response.IsSuccessStatusCode)
            {
                var errorText = await response.Content.ReadAsStringAsync();
                Console.WriteLine($"OpenAI error: {response.StatusCode} - {errorText}");
                return Results.Problem("Error calling OpenAI API.");
            }

            using var stream = await response.Content.ReadAsStreamAsync();
            using var doc = await JsonDocument.ParseAsync(stream);

            var root = doc.RootElement;

            string replyText = root
                .GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString() ?? string.Empty;

            var result = new ScratchChatResponse(replyText.Trim());
            return Results.Ok(result);
        });

        app.Run();
    }
}

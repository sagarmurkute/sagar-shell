using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SagarShell.Engine.Models;

namespace SagarShell.Engine.Intelligence.AIProviders;

/// <summary>
/// Pluggable AI translation provider for OpenAI or local Ollama / LM Studio endpoints.
/// </summary>
public class OpenAiCompatibleProvider : ILLMProvider
{
    private readonly HttpClient _httpClient;
    private readonly string _endpointUrl;
    private readonly string? _apiKey;
    private readonly string _modelName;

    public string ProviderName => $"LLM ({_modelName})";
    public bool IsAvailable => !string.IsNullOrEmpty(_endpointUrl);

    public OpenAiCompatibleProvider(
        string endpointUrl = "http://localhost:11434/v1/chat/completions",
        string? apiKey = null,
        string modelName = "llama3:latest",
        HttpClient? httpClient = null)
    {
        _endpointUrl = endpointUrl;
        _apiKey = apiKey;
        _modelName = modelName;
        _httpClient = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
    }

    public async Task<TranslationResult?> TranslateAsync(string naturalQuery, ShellContext? context, CancellationToken cancellationToken = default)
    {
        try
        {
            var systemPrompt = @"You are Sagar Shell's Natural Language Engine. Convert the user's English request into a valid, single-line Windows PowerShell command.
Output ONLY a JSON object with this format:
{
  ""powerShellScript"": ""<script>"",
  ""explanation"": ""<brief 1-sentence explanation>"",
  ""category"": ""FileSystem|ProcessManagement|SystemInformation|Network|WindowsServices|Navigation|GitOperations|WindowAndApp|PowerControl"",
  ""risk"": ""Safe|Low|Moderate|High|Critical""
}";

            var requestBody = new
            {
                model = _modelName,
                messages = new[]
                {
                    new { role = "system", content = systemPrompt },
                    new { role = "user", content = $"Context CWD: {context?.CurrentDirectory ?? "."}\nUser request: {naturalQuery}" }
                },
                temperature = 0.1,
                response_format = new { type = "json_object" }
            };

            var jsonContent = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");
            if (!string.IsNullOrEmpty(_apiKey))
            {
                _httpClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _apiKey);
            }

            var response = await _httpClient.PostAsync(_endpointUrl, jsonContent, cancellationToken);
            if (!response.IsSuccessStatusCode)
                return null;

            var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(responseJson);
            var content = doc.RootElement
                .GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString();

            if (string.IsNullOrEmpty(content)) return null;

            using var resDoc = JsonDocument.Parse(content);
            var root = resDoc.RootElement;
            var script = root.GetProperty("powerShellScript").GetString() ?? "";
            var explanation = root.GetProperty("explanation").GetString() ?? "";
            var catStr = root.TryGetProperty("category", out var c) ? c.GetString() : "GeneralQuery";
            var riskStr = root.TryGetProperty("risk", out var r) ? r.GetString() : "Safe";

            Enum.TryParse<IntentCategory>(catStr, true, out var category);
            Enum.TryParse<RiskLevel>(riskStr, true, out var risk);

            return TranslationResult.CreateSuccess(
                naturalQuery,
                script,
                explanation,
                risk,
                category,
                0.90
            );
        }
        catch
        {
            return null;
        }
    }
}

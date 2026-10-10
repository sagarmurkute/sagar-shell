using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SagarShell.Engine.Models;

namespace SagarShell.Engine.Intelligence.AIProviders;

/// <summary>
/// Pluggable AI translation provider for OpenAI or local Ollama / LM Studio endpoints.
/// Supports rich multi-turn conversational dialogue and context reasoning.
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
        _httpClient = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
    }

    public async Task<TranslationResult?> TranslateAsync(string naturalQuery, ShellContext? context, CancellationToken cancellationToken = default)
    {
        try
        {
            var systemPrompt = @"You are Sagar Shell's Natural Language & Conversational Intelligence Engine.
You operate as an empathetic, expert Windows systems engineer.
Understand user problems first, clarify when uncertain, remember multi-turn context, and propose safe diagnostics before taking destructive actions.

Output ONLY a JSON object with this format:
{
  ""thought"": ""<reasoning about intent and conversational context>"",
  ""conversationalMessage"": ""<what to say to user in plain English>"",
  ""powerShellScript"": ""<valid PowerShell command to run, or empty if only proposing/asking>"",
  ""isProposal"": false,
  ""requiresConfirmation"": false,
  ""impactExplanation"": ""<explanation of impact if state-modifying or destructive>"",
  ""category"": ""FileSystem|ProcessManagement|SystemInformation|Network|WindowsServices|Navigation|GitOperations|WindowAndApp|PowerControl|GeneralQuery"",
  ""risk"": ""Safe|Low|Moderate|High|Critical""
}";

            var messages = new List<object>
            {
                new { role = "system", content = systemPrompt }
            };

            // Inject conversational history if available
            if (context?.ConversationTurns != null && context.ConversationTurns.Count > 0)
            {
                foreach (var turn in context.ConversationTurns.TakeLast(5))
                {
                    messages.Add(new { role = "user", content = turn.UserMessage });
                    messages.Add(new { role = "assistant", content = turn.AssistantResponse });
                }
            }

            var contextSummary = $"Current Directory: {context?.CurrentDirectory ?? "."}\n" +
                                 $"Is Admin: {context?.IsElevated ?? false}\n" +
                                 $"Active Goal: {context?.ActiveGoal ?? "None"}\n" +
                                 $"Last Process/Entity Memory: {string.Join(", ", context?.Memory.LastReferencedProcessNames ?? new List<string>())}\n" +
                                 $"User Request: {naturalQuery}";

            messages.Add(new { role = "user", content = contextSummary });

            var requestBody = new
            {
                model = _modelName,
                messages = messages.ToArray(),
                temperature = 0.2,
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
            var script = root.TryGetProperty("powerShellScript", out var s) ? s.GetString() ?? "" : "";
            var convMsg = root.TryGetProperty("conversationalMessage", out var cm) ? cm.GetString() ?? "" : "";
            var thought = root.TryGetProperty("thought", out var th) ? th.GetString() ?? "" : "";
            var impact = root.TryGetProperty("impactExplanation", out var imp) ? imp.GetString() : null;
            var isProposal = root.TryGetProperty("isProposal", out var prop) && prop.GetBoolean();
            var reqConfirm = root.TryGetProperty("requiresConfirmation", out var rc) && rc.GetBoolean();
            var catStr = root.TryGetProperty("category", out var c) ? c.GetString() : "GeneralQuery";
            var riskStr = root.TryGetProperty("risk", out var r) ? r.GetString() : "Safe";

            Enum.TryParse<IntentCategory>(catStr, true, out var category);
            Enum.TryParse<RiskLevel>(riskStr, true, out var risk);

            var result = TranslationResult.CreateSuccess(
                naturalQuery,
                script,
                string.IsNullOrEmpty(convMsg) ? thought : convMsg,
                risk,
                category,
                0.95
            );

            result.ConversationalMessage = convMsg;
            result.IsConversationalResponse = !string.IsNullOrEmpty(convMsg);
            result.RequiresExplicitConfirmation = reqConfirm;
            result.ImpactExplanation = impact;

            if (isProposal && !string.IsNullOrEmpty(script))
            {
                result.PendingProposal = TranslationResult.CreateSuccess(naturalQuery, script, convMsg, risk, category);
            }

            return result;
        }
        catch
        {
            return null;
        }
    }
}

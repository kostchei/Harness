using System;
using System.ClientModel;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using OpenAI;
using OpenAI.Chat;

namespace Harness.Agents.Dnd
{
    /// <summary>
    /// Base class for all D&amp;D campaign agents.
    /// Drives LM Studio (or any OpenAI-compatible local server) via the
    /// OpenAI .NET SDK pointed at http://localhost:1234/v1.
    ///
    /// Tool definitions are built with <see cref="MakeTool"/> helpers and
    /// stored in the <see cref="Tools"/> list. The agentic loop in
    /// <see cref="ChatAsync"/> keeps calling the model and routing tool calls
    /// back until the model returns a plain-text response with no further calls.
    /// </summary>
    public abstract class DndAgent
    {
        // ── LM Studio connection ──────────────────────────────────────────────
        private readonly ChatClient _client;

        // ── Shared conversation history ───────────────────────────────────────
        protected readonly List<ChatMessage> History = new();

        // ── Shared SRD lookup tool ──────────────────────────────────────────
        private static readonly ChatTool SrdLookupTool = MakeTool(
            "lookup_srd",
            "Look up SRD 5e rules by topic. Returns the official reference data.",
            new Dictionary<string, object>
            {
                ["topic"] = StringProp(
                    "One of: species, backgrounds, classes, combat, monsters, progression, equipment, spells")
            },
            new List<string> { "topic" });

        protected DndAgent(string baseUrl, string modelName)
        {
            // LM Studio exposes an OpenAI-compatible endpoint; the API key can
            // be anything (LM Studio ignores it locally).
            var options = new OpenAIClientOptions
            {
                Endpoint = new Uri(baseUrl)
            };
            var openAiClient = new OpenAIClient(new ApiKeyCredential("lm-studio"), options);
            _client = openAiClient.GetChatClient(modelName);
        }

        // ── Subclass contract ─────────────────────────────────────────────────
        protected abstract string AgentName { get; }
        protected abstract string SystemPrompt { get; }

        /// <summary>
        /// Override to supply tool definitions for this agent.
        /// Uses <see cref="MakeTool"/> to build <see cref="ChatTool"/> objects.
        /// The base list includes the shared lookup_srd tool automatically.
        /// </summary>
        protected virtual List<ChatTool> Tools => new();

        // ── Public API ────────────────────────────────────────────────────────

        /// <summary>
        /// Send a user message, run the tool-call loop, and return the final
        /// assistant text.  Conversation history is kept across calls.
        /// </summary>
        public async Task<string> ChatAsync(string userMessage)
        {
            History.Add(new UserChatMessage(userMessage));

            while (true)
            {
                // Build the full message list: system + history
                var messages = new List<ChatMessage>
                {
                    new SystemChatMessage(SystemPrompt)
                };
                messages.AddRange(History);

                // Build completion options
                var completionOptions = new ChatCompletionOptions
                {
                    MaxOutputTokenCount = 2048,
                };

                // Attach agent-specific tools + the shared SRD lookup tool
                var allTools = Tools;
                if (allTools.Count > 0)
                {
                    foreach (var tool in allTools)
                        completionOptions.Tools.Add(tool);
                    completionOptions.Tools.Add(SrdLookupTool);
                    completionOptions.ToolChoice = ChatToolChoice.CreateAutoChoice();
                }

                ChatCompletion completion;
                try
                {
                    completion = await _client.CompleteChatAsync(messages, completionOptions);
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"[{AgentName}] LLM call failed: {ex.GetType().Name}: {ex.Message}");
                    if (ex.InnerException != null)
                        Console.Error.WriteLine($"  Inner: {ex.InnerException.Message}");
                    throw;
                }

                // ── Handle tool calls ──────────────────────────────────────
                if (completion.FinishReason == ChatFinishReason.ToolCalls)
                {
                    // Add assistant's tool-call message to history
                    var assistantMsg = new AssistantChatMessage(completion);
                    History.Add(assistantMsg);

                    // Execute each tool call and collect results
                    var toolResults = new List<ToolChatMessage>();
                    foreach (var toolCall in completion.ToolCalls)
                    {
                        var inputArgs = ParseToolArguments(toolCall.FunctionArguments);
                        var result    = await HandleToolCallAsync(toolCall.FunctionName, inputArgs);

                        toolResults.Add(new ToolChatMessage(toolCall.Id, result));
                    }

                    // Feed tool results back to the model
                    History.AddRange(toolResults);
                    continue; // loop — let the model continue
                }

                // ── Plain text response ────────────────────────────────────
                var responseText = ExtractText(completion);
                History.Add(new AssistantChatMessage(responseText));
                return responseText;
            }
        }

        /// <summary>Override to handle tool calls by name. Return result as a string (JSON recommended).</summary>
        protected virtual Task<string> HandleToolCallAsync(
            string toolName,
            IReadOnlyDictionary<string, JsonElement> input)
        {
            if (toolName == "lookup_srd")
                return Task.FromResult(HandleSrdLookup(input));
            return Task.FromResult($"{{\"error\": \"Unknown tool: {toolName}\"}}");
        }

        private static string HandleSrdLookup(IReadOnlyDictionary<string, JsonElement> input)
        {
            var topic = input.TryGetValue("topic", out var t) ? t.GetString() ?? "" : "";
            return topic.ToLower() switch
            {
                "species"     => SrdRules.Species,
                "backgrounds" => SrdRules.Backgrounds,
                "classes"     => SrdRules.Classes,
                "races"       => SrdRules.Races,
                "combat"      => SrdRules.Combat,
                "monsters"    => SrdRules.Monsters,
                "progression" => SrdRules.Progression,
                "equipment"   => SrdRules.Equipment,
                "spells"      => SrdRules.Spells,
                _ => $"{{\"error\": \"Unknown SRD topic: {topic}. Use one of: species, backgrounds, classes, combat, monsters, progression, equipment, spells\"}}"
            };
        }

        /// <summary>Reset conversation history (e.g. when starting a new session).</summary>
        public void ClearHistory() => History.Clear();

        // ── Helpers ───────────────────────────────────────────────────────────

        private static IReadOnlyDictionary<string, JsonElement> ParseToolArguments(BinaryData arguments)
        {
            if (arguments is null || arguments.ToMemory().IsEmpty)
                return new Dictionary<string, JsonElement>();

            try
            {
                var doc = JsonDocument.Parse(arguments);
                var result = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
                foreach (var prop in doc.RootElement.EnumerateObject())
                    result[prop.Name] = prop.Value.Clone();
                return result;
            }
            catch
            {
                return new Dictionary<string, JsonElement>();
            }
        }

        private static string ExtractText(ChatCompletion completion)
        {
            var sb = new StringBuilder();
            foreach (var part in completion.Content)
                sb.Append(part.Text);
            return sb.ToString().Trim();
        }

        // ── Tool-definition helpers ───────────────────────────────────────────

        /// <summary>
        /// Build a <see cref="ChatTool"/> with an inline JSON Schema for its parameters.
        /// </summary>
        protected static ChatTool MakeTool(
            string name,
            string description,
            Dictionary<string, object> properties,
            List<string>? required = null)
        {
            var schema = new Dictionary<string, object>
            {
                ["type"]       = "object",
                ["properties"] = properties
            };
            if (required is { Count: > 0 })
                schema["required"] = required;

            var schemaJson = JsonSerializer.Serialize(schema);
            return ChatTool.CreateFunctionTool(
                name,
                description,
                BinaryData.FromString(schemaJson));
        }

        protected static object StringProp(string description) =>
            new { type = "string", description };

        protected static object IntProp(string description) =>
            new { type = "integer", description };

        protected static object BoolProp(string description) =>
            new { type = "boolean", description };

        protected static object ArrayProp(string description, string itemType = "string") =>
            new { type = "array", description, items = new { type = itemType } };
    }
}

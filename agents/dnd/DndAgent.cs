using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using Anthropic;
using Anthropic.Models.Messages;

namespace Harness.Agents.Dnd
{
    /// <summary>
    /// Base class for all D&D campaign agents.
    /// Each agent has a specialised system prompt and a set of tools it can call.
    /// Agents communicate results back to the orchestrator via structured JSON.
    /// </summary>
    public abstract class DndAgent
    {
        protected readonly AnthropicClient Client;
        protected readonly List<MessageParam> History = new();

        private const string Model = "claude-opus-4-6";
        private const int MaxTokens = 4096;

        protected DndAgent(string apiKey)
        {
            Client = new AnthropicClient { ApiKey = apiKey };
        }

        protected abstract string AgentName { get; }
        protected abstract string SystemPrompt { get; }
        protected virtual List<ToolUnion> Tools => new();

        /// <summary>
        /// Send a message to this agent and get its response, executing any tool
        /// calls automatically until the agent reaches end_turn.
        /// </summary>
        public async Task<string> ChatAsync(string userMessage)
        {
            History.Add(new MessageParam
            {
                Role = Role.User,
                Content = userMessage
            });

            while (true)
            {
                var parameters = new MessageCreateParams
                {
                    Model = Model,
                    MaxTokens = MaxTokens,
                    System = SystemPrompt,
                    Thinking = new ThinkingConfigAdaptive(),
                    Messages = History,
                    Tools = Tools.Count > 0 ? Tools : new List<ToolUnion>()
                };

                var response = await Client.Messages.Create(parameters);

                // Collect assistant content to add to history
                var assistantContent = new List<ContentBlockParam>();
                var toolResults = new List<ContentBlockParam>();
                string finalText = "";

                foreach (var block in response.Content)
                {
                    if (block.TryPickText(out TextBlock? text))
                    {
                        finalText += text.Text;
                        assistantContent.Add(new TextBlockParam { Text = text.Text });
                    }
                    else if (block.TryPickThinking(out ThinkingBlock? thinking))
                    {
                        assistantContent.Add(new ThinkingBlockParam
                        {
                            Thinking = thinking.Thinking,
                            Signature = thinking.Signature
                        });
                    }
                    else if (block.TryPickRedactedThinking(out RedactedThinkingBlock? redacted))
                    {
                        assistantContent.Add(new RedactedThinkingBlockParam { Data = redacted.Data });
                    }
                    else if (block.TryPickToolUse(out ToolUseBlock? toolUse))
                    {
                        assistantContent.Add(new ToolUseBlockParam
                        {
                            ID = toolUse.ID,
                            Name = toolUse.Name,
                            Input = toolUse.Input
                        });

                        var result = await HandleToolCallAsync(toolUse.Name, toolUse.Input);
                        toolResults.Add(new ToolResultBlockParam
                        {
                            ToolUseID = toolUse.ID,
                            Content = result
                        });
                    }
                }

                History.Add(new MessageParam
                {
                    Role = Role.Assistant,
                    Content = assistantContent
                });

                if (toolResults.Count > 0)
                {
                    // Feed results back and loop
                    History.Add(new MessageParam
                    {
                        Role = Role.User,
                        Content = toolResults
                    });
                    continue;
                }

                return finalText;
            }
        }

        /// <summary>
        /// Override in subclasses to handle tool calls by name.
        /// Return the tool result as a string (JSON recommended).
        /// </summary>
        protected virtual Task<string> HandleToolCallAsync(
            string toolName,
            IReadOnlyDictionary<string, JsonElement> input)
        {
            return Task.FromResult($"{{\"error\": \"Unknown tool: {toolName}\"}}");
        }

        /// <summary>Reset conversation history (e.g. when starting a new session).</summary>
        public void ClearHistory() => History.Clear();

        // ─── Shared helpers ───────────────────────────────────────────────────

        protected static Tool MakeTool(string name, string description,
            Dictionary<string, object> properties,
            List<string>? required = null)
        {
            var schema = new Dictionary<string, object>
            {
                ["type"] = "object",
                ["properties"] = properties
            };
            if (required is { Count: > 0 })
                schema["required"] = required;

            return new Tool
            {
                Name = name,
                Description = description,
                InputSchema = new InputSchema
                {
                    Properties = SerializeProperties(properties),
                    Required = required ?? new List<string>()
                }
            };
        }

        private static Dictionary<string, JsonElement> SerializeProperties(
            Dictionary<string, object> props)
        {
            var result = new Dictionary<string, JsonElement>();
            foreach (var (key, value) in props)
            {
                var json = JsonSerializer.Serialize(value);
                result[key] = JsonDocument.Parse(json).RootElement.Clone();
            }
            return result;
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

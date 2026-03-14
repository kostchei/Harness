using System;
using System.IO;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Harness.Agents.Dnd
{
    public class DndServer
    {
        private static readonly JsonSerializerOptions JsonOpts =
            new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

        private readonly HttpListener _listener = new();
        private readonly string _webRoot;
        private readonly SemaphoreSlim _lock = new(1, 1);

        private CampaignOrchestrator? _orchestrator;
        private string _baseUrl;
        private string _modelName;

        public DndServer(int port, string webRoot, string baseUrl, string modelName)
        {
            _webRoot = Path.GetFullPath(webRoot);
            _baseUrl = baseUrl;
            _modelName = modelName;
            _listener.Prefixes.Add($"http://localhost:{port}/");
            _listener.Prefixes.Add($"http://127.0.0.1:{port}/");
        }

        public async Task RunAsync(CancellationToken ct)
        {
            _listener.Start();
            Console.WriteLine("Listening...");

            while (!ct.IsCancellationRequested)
            {
                var ctx = await _listener.GetContextAsync();
                _ = Task.Run(() => HandleRequest(ctx), ct);
            }
        }

        private async Task HandleRequest(HttpListenerContext ctx)
        {
            try
            {
                AddCorsHeaders(ctx.Response);

                if (ctx.Request.HttpMethod == "OPTIONS")
                {
                    ctx.Response.StatusCode = 204;
                    ctx.Response.Close();
                    return;
                }

                var path = ctx.Request.Url?.AbsolutePath ?? "/";

                if (path == "/api/dnd/health" && ctx.Request.HttpMethod == "GET")
                    await HandleHealth(ctx);
                else if (path == "/api/dnd/start" && ctx.Request.HttpMethod == "POST")
                    await HandleStart(ctx);
                else if (path == "/api/dnd/input" && ctx.Request.HttpMethod == "POST")
                    await HandleInput(ctx);
                else if (path == "/api/dnd/state" && ctx.Request.HttpMethod == "GET")
                    await HandleState(ctx);
                else if (!path.StartsWith("/api/"))
                    ServeStaticFile(ctx, path);
                else
                    await WriteJson(ctx, 404, new { ok = false, error = "Not found" });
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Request error: {ex.Message}");
                try { await WriteJson(ctx, 500, new { ok = false, error = ex.Message }); }
                catch { /* response already closed */ }
            }
        }

        private async Task HandleHealth(HttpListenerContext ctx)
        {
            await WriteJson(ctx, 200, new
            {
                ok = true,
                phase = _orchestrator?.CurrentPhase.ToString(),
                hasCharacter = _orchestrator?.Character != null,
                baseUrl = _baseUrl,
                modelName = _modelName
            });
        }

        private async Task HandleStart(HttpListenerContext ctx)
        {
            var body = await ReadJsonBody(ctx);
            var campaignName = body.TryGetProperty("campaignName", out var cn)
                ? cn.GetString() ?? "The Forgotten Keep" : "The Forgotten Keep";

            if (body.TryGetProperty("baseUrl", out var bu) && bu.GetString() is string url && url.Length > 0)
                _baseUrl = url;
            if (body.TryGetProperty("modelName", out var mn) && mn.GetString() is string model && model.Length > 0)
                _modelName = model;

            await _lock.WaitAsync();
            try
            {
                _orchestrator = new CampaignOrchestrator(_baseUrl, _modelName);
                Console.WriteLine($"Starting campaign: {campaignName}");
                var narrative = await _orchestrator.StartNewCampaignAsync(campaignName);

                await WriteJson(ctx, 200, new
                {
                    ok = true,
                    narrative,
                    phase = _orchestrator.CurrentPhase.ToString(),
                    character = _orchestrator.Character,
                    campaign = _orchestrator.Campaign
                });
            }
            finally { _lock.Release(); }
        }

        private async Task HandleInput(HttpListenerContext ctx)
        {
            if (_orchestrator == null)
                throw new InvalidOperationException("No campaign active. Call /api/dnd/start first.");

            var body = await ReadJsonBody(ctx);
            var text = body.TryGetProperty("text", out var t)
                ? t.GetString() ?? "" : "";

            if (string.IsNullOrWhiteSpace(text))
                throw new ArgumentException("'text' is required.");

            await _lock.WaitAsync();
            try
            {
                Console.WriteLine($"Player: {text}");
                var narrative = await _orchestrator.PlayerInputAsync(text);

                await WriteJson(ctx, 200, new
                {
                    ok = true,
                    narrative,
                    phase = _orchestrator.CurrentPhase.ToString(),
                    character = _orchestrator.Character,
                    campaign = _orchestrator.Campaign
                });
            }
            finally { _lock.Release(); }
        }

        private async Task HandleState(HttpListenerContext ctx)
        {
            await WriteJson(ctx, 200, new
            {
                ok = true,
                phase = _orchestrator?.CurrentPhase.ToString(),
                character = _orchestrator?.Character,
                campaign = _orchestrator?.Campaign
            });
        }

        private void ServeStaticFile(HttpListenerContext ctx, string path)
        {
            if (path == "/") path = "/dnd.html";

            var filePath = Path.GetFullPath(Path.Combine(_webRoot, path.TrimStart('/')));

            if (!filePath.StartsWith(_webRoot, StringComparison.OrdinalIgnoreCase) || !File.Exists(filePath))
            {
                ctx.Response.StatusCode = 404;
                ctx.Response.Close();
                return;
            }

            var ext = Path.GetExtension(filePath).ToLower();
            ctx.Response.ContentType = ext switch
            {
                ".html" => "text/html; charset=utf-8",
                ".css" => "text/css; charset=utf-8",
                ".js" => "application/javascript; charset=utf-8",
                ".json" => "application/json; charset=utf-8",
                ".png" => "image/png",
                ".svg" => "image/svg+xml",
                _ => "application/octet-stream"
            };

            var bytes = File.ReadAllBytes(filePath);
            ctx.Response.ContentLength64 = bytes.Length;
            ctx.Response.OutputStream.Write(bytes, 0, bytes.Length);
            ctx.Response.Close();
        }

        private static async Task<JsonElement> ReadJsonBody(HttpListenerContext ctx)
        {
            using var reader = new StreamReader(ctx.Request.InputStream, ctx.Request.ContentEncoding);
            var raw = await reader.ReadToEndAsync();
            if (string.IsNullOrWhiteSpace(raw))
                return JsonDocument.Parse("{}").RootElement;
            return JsonDocument.Parse(raw).RootElement;
        }

        private static async Task WriteJson(HttpListenerContext ctx, int status, object data)
        {
            var json = JsonSerializer.Serialize(data, JsonOpts);
            var bytes = Encoding.UTF8.GetBytes(json);
            ctx.Response.StatusCode = status;
            ctx.Response.ContentType = "application/json; charset=utf-8";
            ctx.Response.ContentLength64 = bytes.Length;
            await ctx.Response.OutputStream.WriteAsync(bytes);
            ctx.Response.Close();
        }

        private static void AddCorsHeaders(HttpListenerResponse response)
        {
            response.AddHeader("Access-Control-Allow-Origin", "*");
            response.AddHeader("Access-Control-Allow-Methods", "GET, POST, OPTIONS");
            response.AddHeader("Access-Control-Allow-Headers", "Content-Type");
        }

        public static async Task Main(string[] args)
        {
            int port = 5000;
            string webRoot = "webui";
            string baseUrl = "http://localhost:1234/v1";
            string modelName = "glm-4.7-flash-uncensored-heretic-neo-code-imatrix-max";

            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--port" when i + 1 < args.Length:
                        port = int.Parse(args[++i]); break;
                    case "--webroot" when i + 1 < args.Length:
                        webRoot = args[++i]; break;
                    case "--lm-url" when i + 1 < args.Length:
                        baseUrl = args[++i]; break;
                    case "--model" when i + 1 < args.Length:
                        modelName = args[++i]; break;
                }
            }

            Console.WriteLine($"D&D Campaign Server");
            Console.WriteLine($"  UI:       http://localhost:{port}/dnd.html");
            Console.WriteLine($"  LM Studio: {baseUrl}");
            Console.WriteLine($"  Model:    {modelName}");

            var server = new DndServer(port, webRoot, baseUrl, modelName);

            var cts = new CancellationTokenSource();
            Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

            try { await server.RunAsync(cts.Token); }
            catch (OperationCanceledException) { }

            Console.WriteLine("Server stopped.");
        }
    }
}

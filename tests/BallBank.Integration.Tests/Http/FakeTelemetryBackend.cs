using System.Collections.Concurrent;
using System.IO.Compression;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BallBank.Integration.Tests.Http;

/// <summary>
/// Application Insights and an OTLP collector on one loopback port, keeping what is sent to them, so what a host
/// exports can be read without a call leaving the machine.
/// </summary>
public sealed class FakeTelemetryBackend : IAsyncDisposable
{
    private readonly WebApplication _app;
    private readonly ConcurrentQueue<ApplicationInsightsItem> _items = new();
    private readonly ConcurrentQueue<string> _collected = new();

    private FakeTelemetryBackend()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        _app = builder.Build();
        _app.Run(Receive);
    }

    /// <summary>Where it listens, ending in a slash: an OTLP endpoint, and an Application Insights ingestion endpoint.</summary>
    public string Address { get; private set; } = "";

    /// <summary>A connection string for an Application Insights resource that is this fake. The key is made up for the occasion.</summary>
    public string ConnectionString => $"InstrumentationKey={Guid.NewGuid()};IngestionEndpoint={Address}";

    /// <summary>What Application Insights was sent, one item per log, request, dependency and metric.</summary>
    public IReadOnlyList<ApplicationInsightsItem> Items => [.. _items];

    /// <summary>The OTLP paths that were posted to (<c>/v1/traces</c>, <c>/v1/metrics</c>, <c>/v1/logs</c>), once per post.</summary>
    public IReadOnlyList<string> Collected => [.. _collected];

    public static async Task<FakeTelemetryBackend> Start()
    {
        var backend = new FakeTelemetryBackend();
        await backend._app.StartAsync();
        backend.Address = backend._app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!
            .Addresses.Single().TrimEnd('/') + "/";
        return backend;
    }

    public async ValueTask DisposeAsync()
    {
        await _app.StopAsync();
        await _app.DisposeAsync();
    }

    private async Task Receive(HttpContext context)
    {
        if (context.Request.Path != "/v2.1/track")
        {
            await context.Request.Body.CopyToAsync(Stream.Null);
            _collected.Enqueue(context.Request.Path.Value!);
            context.Response.ContentType = "application/x-protobuf";
            return;
        }

        Stream body = context.Request.Body;
        if (context.Request.Headers.ContentEncoding == "gzip")
        {
            body = new GZipStream(body, CompressionMode.Decompress);
        }

        using var reader = new StreamReader(body);
        var received = 0;
        while (await reader.ReadLineAsync() is { Length: > 0 } line)
        {
            _items.Enqueue(new ApplicationInsightsItem(JsonNode.Parse(line)!.AsObject()));
            received++;
        }

        context.Response.ContentType = "application/json";
        await context.Response.WriteAsync($$"""{"itemsReceived":{{received}},"itemsAccepted":{{received}},"errors":[]}""");
    }

    /// <summary>One thing Application Insights was told: what kind it is, and its custom dimensions.</summary>
    public sealed record ApplicationInsightsItem(JsonObject Json)
    {
        /// <summary><c>MessageData</c> (a log), <c>RequestData</c> (a server or consumer span), <c>RemoteDependencyData</c> (any other span) or <c>MetricData</c>.</summary>
        public string Type => (string)Json["data"]!["baseType"]!;

        private JsonObject Data => Json["data"]!["baseData"]!.AsObject();

        /// <summary>The log's message, the span's name, or the metric's name.</summary>
        public string? Name => Type switch
        {
            "MessageData" => (string?)Data["message"],
            "MetricData" => (string?)Data["metrics"]![0]!["name"],
            _ => (string?)Data["name"],
        };

        public string? Dimension(string name) => (string?)Data["properties"]?[name];

        /// <summary>What it says about where it was sent from and what it belongs to: <c>ai.cloud.role</c>, <c>ai.operation.id</c>.</summary>
        public string? Tag(string name) => (string?)Json["tags"]?[name];

        public override string ToString() => Json.ToJsonString();
    }
}

using System.Diagnostics;
using BallBank.Api;
using BallBank.Api.Features.Notifications;
using BallBank.Domain.Notifications;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Shouldly;

namespace BallBank.Integration.Tests.Http;

/// <summary>
/// What a host built the way every BallBank host is (<c>AddServiceDefaults</c>) sends to Application Insights and to an
/// OTLP collector, read from a fake of both on loopback. Nothing here leaves the machine.
/// </summary>
public class TelemetryExportTests
{
    private const string HandlerSpan = "SendNotification process";

    static TelemetryExportTests()
    {
        // The exporter reports on itself to Microsoft by default; a test has no business doing that.
        foreach (var setting in new[] { "APPLICATIONINSIGHTS_STATSBEAT_DISABLED", "APPLICATIONINSIGHTS_SDKSTATS_DISABLED", "APPLICATIONINSIGHTS_SDKSTATS_DISABLED_ALL" })
        {
            Environment.SetEnvironmentVariable(setting, "true");
        }
    }

    [Fact]
    public async Task With_a_connection_string_what_a_handler_records_reaches_Application_Insights_under_its_league()
    {
        await using var backend = await FakeTelemetryBackend.Start();
        var league = Guid.NewGuid();

        using (var host = BuildHost(Settings(backend, applicationInsights: true)))
        {
            await host.StartAsync();
            Record(host, league);
            await host.StopAsync();
        }

        var ours = backend.Items.Where(item => item.Dimension(TenantTelemetry.TenantId) == league.ToString()).ToList();
        ours.Select(item => (item.Type, item.Name)).ShouldBe(
            [
                ("RequestData", HandlerSpan),
                ("MessageData", LogMessage(league)),
                ("MetricData", NotificationMetric.InstrumentName),
            ],
            ignoreOrder: true);

        var counted = ours.Single(item => item.Type == "MetricData");
        counted.Dimension(NotificationMetric.Channel).ShouldBe(Channels.Sms);
        counted.Dimension(NotificationMetric.Kind).ShouldBe(NotificationKinds.Reminder);
        counted.Dimension(NotificationMetric.Outcome).ShouldBe(NotificationStatus.Sent);
    }

    [Fact]
    public async Task The_OTLP_collector_is_still_sent_to_beside_Application_Insights()
    {
        await using var backend = await FakeTelemetryBackend.Start();
        var league = Guid.NewGuid();

        using (var host = BuildHost(Settings(backend, applicationInsights: true, otlp: true)))
        {
            await host.StartAsync();
            Record(host, league);
            await host.StopAsync();
        }

        backend.Collected.ShouldContain("/v1/traces");
        backend.Collected.ShouldContain("/v1/metrics");
        backend.Items.Count(item => item.Dimension(TenantTelemetry.TenantId) == league.ToString()).ShouldBe(3);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public async Task Without_a_connection_string_nothing_is_sent_to_Application_Insights(string? connectionString)
    {
        await using var backend = await FakeTelemetryBackend.Start();
        var settings = Settings(backend, otlp: true);
        settings["APPLICATIONINSIGHTS_CONNECTION_STRING"] = connectionString;

        using (var host = BuildHost(settings))
        {
            await host.StartAsync();
            Record(host, Guid.NewGuid());
            await host.StopAsync();
        }

        backend.Collected.ShouldContain("/v1/traces");
        backend.Items.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_host_that_is_never_started_sends_what_it_recorded_once_its_telemetry_is_started()
    {
        await using var backend = await FakeTelemetryBackend.Start();
        var league = Guid.NewGuid();

        using (var host = BuildHost(Settings(backend, applicationInsights: true, otlp: true)))
        {
            using var telemetry = host.StartTelemetry();
            Record(host, league);
        }

        var ours = backend.Items.Where(item => item.Dimension(TenantTelemetry.TenantId) == league.ToString()).ToList();
        ours.Select(item => item.Type).ShouldBe(["RequestData", "MessageData", "MetricData"], ignoreOrder: true);
        backend.Collected.Distinct().ShouldBe(["/v1/traces", "/v1/metrics", "/v1/logs"], ignoreOrder: true);
    }

    private static string LogMessage(Guid league) => $"Tick for league {league}";

    private static Dictionary<string, string?> Settings(FakeTelemetryBackend backend, bool applicationInsights = false, bool otlp = false)
    {
        var settings = new Dictionary<string, string?>();
        if (applicationInsights)
        {
            settings["APPLICATIONINSIGHTS_CONNECTION_STRING"] = backend.ConnectionString;
        }

        if (otlp)
        {
            settings["OTEL_EXPORTER_OTLP_ENDPOINT"] = backend.Address;
            settings["OTEL_EXPORTER_OTLP_PROTOCOL"] = "http/protobuf";
        }

        return settings;
    }

    // No default configuration sources, so a connection string in this machine's environment cannot reach it.
    private static IHost BuildHost(Dictionary<string, string?> settings)
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { DisableDefaults = true });
        builder.Configuration.AddInMemoryCollection(settings);
        builder.AddServiceDefaults();
        return builder.Build();
    }

    private static void Record(IHost host, Guid league)
    {
        var logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger<TelemetryExportTests>();
        using var messaging = new ActivitySource("Wolverine");
        using (var handled = messaging.StartActivity(HandlerSpan, ActivityKind.Consumer))
        {
            handled.ShouldNotBeNull("the span must be sampled in");
            handled.SetTag(TenantTelemetry.TenantId, league.ToString());
            using (logger.BeginTenantScope(league.ToString()))
            {
                logger.LogInformation("Tick for league {LeagueId}", league);
            }
        }

        NotificationMetric.Record(league, new Notification { Channel = Channels.Sms, Kind = NotificationKinds.Reminder }, NotificationStatus.Sent);
    }
}

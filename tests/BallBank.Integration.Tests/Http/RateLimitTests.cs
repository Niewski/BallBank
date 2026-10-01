using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using System.Net;
using System.Net.Http.Json;
using BallBank.Api;
using BallBank.Api.Features.Membership;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace BallBank.Integration.Tests.Http;

/// <summary>
/// Request budgets (ADR-0013), on an API whose budgets are <see cref="PostgresFixture.LimitedBurst"/> requests that
/// do not come back during a test. Each test spends the budget of leagues, callers and addresses of its own.
/// </summary>
[Collection(PostgresCollection.Name)]
[Trait("Category", "Integration")]
public class RateLimitTests(PostgresFixture postgres)
{
    private const int Burst = PostgresFixture.LimitedBurst;

    [Fact]
    public async Task A_league_over_its_budget_is_refused_with_problem_details_and_when_to_come_back()
    {
        var api = await postgres.LimitedApi;
        var jacob = NewSubject();
        var leagueId = await ImportHollandHogs(api, jacob);

        var responses = await UntilRefused(api.CreateClientFor(jacob), $"/leagues/{leagueId}/members");

        // The import spent one request of the league's budget.
        responses.Select(r => r.StatusCode).ShouldBe(AnsweredThenRefused(HttpStatusCode.OK, Burst - 1));

        var refused = responses[^1];
        refused.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");
        refused.Headers.RetryAfter?.Delta.ShouldBe(TimeSpan.FromHours(1));
        var problem = await refused.Content.ReadFromJsonAsync<ProblemDetails>();
        problem.ShouldNotBeNull();
        problem.Status.ShouldBe(StatusCodes.Status429TooManyRequests);
        problem.Type.ShouldBe(RateLimiting.LimitedType);
        problem.Detail.ShouldNotBeNull().ShouldContain("3600 seconds");
    }

    [Fact]
    public async Task Another_league_is_answered_while_one_is_over_its_budget_even_for_the_same_person()
    {
        var api = await postgres.LimitedApi;
        var jacob = NewSubject();
        var spent = await ImportHollandHogs(api, jacob);
        var untouched = await ImportHollandHogs(api, jacob);
        var client = api.CreateClientFor(jacob);

        (await UntilRefused(client, $"/leagues/{spent}/members"))[^1].StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);

        (await client.GetAsync($"/leagues/{untouched}/members")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Spelling_the_league_differently_does_not_buy_another_budget()
    {
        var api = await postgres.LimitedApi;
        var jacob = NewSubject();
        var leagueId = await ImportHollandHogs(api, jacob);
        var client = api.CreateClientFor(jacob);
        var spellings = new[] { leagueId.ToString(), leagueId.ToString().ToUpperInvariant() };

        var answers = new List<HttpStatusCode>();
        for (var i = 0; i < Burst; i++)
        {
            answers.Add((await client.GetAsync($"/leagues/{spellings[i % 2]}/members")).StatusCode);
        }

        answers.ShouldBe(AnsweredThenRefused(HttpStatusCode.OK, Burst - 1));
    }

    [Fact]
    public async Task A_caller_outside_any_league_has_a_budget_of_their_own()
    {
        var api = await postgres.LimitedApi;
        var priya = NewSubject();
        var leagueId = await ImportHollandHogs(api, priya);
        var priyas = api.CreateClientFor(priya);

        var responses = await UntilRefused(priyas, "/me/leagues");

        responses.Select(r => r.StatusCode).ShouldBe(AnsweredThenRefused(HttpStatusCode.OK, Burst));
        (await priyas.GetAsync($"/leagues/{leagueId}/members")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await api.CreateClientFor(NewSubject()).GetAsync("/me/leagues")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task The_web_app_may_read_when_to_come_back()
    {
        var api = await postgres.LimitedApi;
        var jacob = NewSubject();
        var leagueId = await ImportHollandHogs(api, jacob);
        var client = api.CreateClientFor(jacob);
        client.DefaultRequestHeaders.Add("Origin", BallBankApi.WebOrigin);

        var refused = (await UntilRefused(client, $"/leagues/{leagueId}/members"))[^1];

        refused.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
        refused.Headers.GetValues("Access-Control-Allow-Origin").ShouldBe([BallBankApi.WebOrigin]);
        refused.Headers.GetValues("Access-Control-Expose-Headers").Single().ShouldContain("Retry-After");
    }

    [Fact]
    public async Task A_webhook_address_over_its_budget_is_refused_while_another_address_is_answered()
    {
        var api = await postgres.LimitedApi;
        var chatty = WebhookClientFrom(api, NewAddress());

        var responses = await UntilRefused(chatty, BallBankApi.WebhookPath);

        responses.Select(r => r.StatusCode).ShouldBe(AnsweredThenRefused(HttpStatusCode.NoContent, Burst));
        responses[^1].Headers.RetryAfter.ShouldNotBeNull();
        (await WebhookClientFrom(api, NewAddress()).GetAsync(BallBankApi.WebhookPath)).StatusCode
            .ShouldBe(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task A_webhook_address_is_budgeted_whoever_it_signs_in_as()
    {
        var api = await postgres.LimitedApi;
        var address = NewAddress();
        var anonymous = WebhookClientFrom(api, address);
        var signedIn = WebhookClientFrom(api, address);
        signedIn.DefaultRequestHeaders.Authorization = new("Bearer", api.TokenFor(NewSubject()));

        (await UntilRefused(anonymous, BallBankApi.WebhookPath))[^1].StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);

        (await signedIn.GetAsync(BallBankApi.WebhookPath)).StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task The_version_endpoint_is_never_refused()
    {
        var api = await postgres.LimitedApi;
        var client = api.CreateClient();

        for (var i = 0; i < Burst * 3; i++)
        {
            (await client.GetAsync("/v1/version")).StatusCode.ShouldBe(HttpStatusCode.OK);
        }
    }

    [Fact]
    public async Task A_refusal_is_counted_against_the_league_that_spent_its_budget()
    {
        using var rejections = new Rejections();
        var api = await postgres.LimitedApi;
        var jacob = NewSubject();
        var leagueId = await ImportHollandHogs(api, jacob);

        await UntilRefused(api.CreateClientFor(jacob), $"/leagues/{leagueId}/members");

        rejections.Seen.Where(r => r.Tenant == leagueId.ToString()).ShouldBe([(leagueId.ToString(), "league")]);
    }

    [Fact]
    public async Task A_refused_webhook_is_counted_without_a_league()
    {
        using var rejections = new Rejections();
        var api = await postgres.LimitedApi;
        var address = NewAddress();

        await UntilRefused(WebhookClientFrom(api, address), BallBankApi.WebhookPath);

        rejections.Seen.ShouldContain((null, "webhook"));
    }

    private static IEnumerable<HttpStatusCode> AnsweredThenRefused(HttpStatusCode answer, int times) =>
        Enumerable.Repeat(answer, times).Append(HttpStatusCode.TooManyRequests);

    private static async Task<List<HttpResponseMessage>> UntilRefused(HttpClient client, string path)
    {
        var responses = new List<HttpResponseMessage>();
        for (var i = 0; i < Burst * 2; i++)
        {
            var response = await client.GetAsync(path);
            responses.Add(response);
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                break;
            }
        }

        return responses;
    }

    private static async Task<Guid> ImportHollandHogs(BallBankApi api, string subject)
    {
        var leagueId = Guid.NewGuid();
        var response = await api.CreateClientFor(subject).PostAsJsonAsync(
            $"/leagues/{leagueId}/import",
            new ImportLeagueRequest(api.Sleeper.CopyOfHollandHogs(), "jacob", "Jacob"));
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        return leagueId;
    }

    private static string NewSubject() => $"test|{Guid.NewGuid():N}";

    private static IPAddress NewAddress() => new(Guid.NewGuid().ToByteArray());

    private static HttpClient WebhookClientFrom(BallBankApi api, IPAddress address)
    {
        var client = api.CreateClient();
        client.DefaultRequestHeaders.Add(BallBankApi.RemoteAddressHeader, address.ToString());
        return client;
    }

    // Listens for the rejections counter, whichever host in the process records it.
    private sealed class Rejections : IDisposable
    {
        private readonly MeterListener _listener = new();
        private readonly ConcurrentQueue<(string? Tenant, string? Partition)> _seen = new();

        public Rejections()
        {
            _listener.InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Meter.Name == RateLimitMetric.MeterName && instrument.Name == RateLimitMetric.InstrumentName)
                {
                    listener.EnableMeasurementEvents(instrument);
                }
            };
            _listener.SetMeasurementEventCallback<long>((_, _, tags, _) =>
            {
                string? tenant = null;
                string? partition = null;
                foreach (var tag in tags)
                {
                    if (tag.Key == TenantTelemetry.TenantId)
                    {
                        tenant = tag.Value?.ToString();
                    }
                    else if (tag.Key == RateLimitMetric.Partition)
                    {
                        partition = tag.Value?.ToString();
                    }
                }

                _seen.Enqueue((tenant, partition));
            });
            _listener.Start();
        }

        public IReadOnlyList<(string? Tenant, string? Partition)> Seen => _seen.ToArray();

        public void Dispose() => _listener.Dispose();
    }
}

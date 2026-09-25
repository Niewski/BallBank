using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc;

namespace BallBank.Integration.Tests.Http;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Integration")]
public class RefusalTests(PostgresFixture postgres)
{
    [Fact]
    public async Task A_refused_command_is_a_conflict_that_carries_the_reason()
    {
        var response = await postgres.Api.CreateClient().GetAsync(BallBankApi.RefusingPath);

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        problem.ShouldNotBeNull();
        problem.Detail.ShouldBe(BallBankApi.RefusalMessage);
    }
}

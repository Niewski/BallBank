using BallBank.Api;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace BallBank.Integration.Tests;

public class RetentionOptionsTests
{
    [Fact]
    public void Records_are_kept_ninety_days_unless_configured_otherwise()
    {
        var options = new RetentionOptions();

        options.Days.ShouldBe(90);
        options.Age.ShouldBe(TimeSpan.FromDays(90));
        options.IsValid.ShouldBeTrue();
    }

    [Fact]
    public void With_nothing_configured_the_registered_age_is_ninety_days()
    {
        using var services = Registered(new());

        services.GetRequiredService<IOptions<RetentionOptions>>().Value.Age.ShouldBe(TimeSpan.FromDays(90));
    }

    [Fact]
    public void The_age_is_read_from_the_Retention_section()
    {
        using var services = Registered(new() { ["Retention:Days"] = "60" });

        services.GetRequiredService<IOptions<RetentionOptions>>().Value.Age.ShouldBe(TimeSpan.FromDays(60));
    }

    [Theory]
    [InlineData(90, true)]
    [InlineData(RetentionOptions.MinimumDays, true)]
    [InlineData(RetentionOptions.MinimumDays - 1, false)]
    [InlineData(0, false)]
    [InlineData(-30, false)]
    public void An_age_shorter_than_the_floor_is_not_valid(int days, bool valid) =>
        new RetentionOptions { Days = days }.IsValid.ShouldBe(valid);

    [Fact]
    public void An_age_shorter_than_the_floor_is_refused_when_it_is_read()
    {
        using var services = Registered(new() { ["Retention:Days"] = "13" });

        var refusal = Should.Throw<OptionsValidationException>(() => services.GetRequiredService<IOptions<RetentionOptions>>().Value);

        refusal.Message.ShouldContain("Retention:Days must be at least 14");
    }

    [Fact]
    public async Task A_host_configured_with_an_age_shorter_than_the_floor_does_not_start()
    {
        var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["Retention:Days"] = "13" });
        builder.Services.AddRetention(builder.Configuration);
        using var host = builder.Build();

        var refusal = await Should.ThrowAsync<OptionsValidationException>(() => host.StartAsync());

        refusal.Message.ShouldContain("Retention:Days must be at least 14");
    }

    private static ServiceProvider Registered(Dictionary<string, string?> configuration) =>
        new ServiceCollection()
            .AddRetention(new ConfigurationBuilder().AddInMemoryCollection(configuration).Build())
            .BuildServiceProvider();
}

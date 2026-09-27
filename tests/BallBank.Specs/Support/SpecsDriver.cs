using Reqnroll;
using Reqnroll.UnitTestProvider;

namespace BallBank.Specs.Support;

/// <summary>
/// Which driver runs the treasury specs: <c>BALLBANK_SPECS_DRIVER=http</c> for the API over HTTP
/// (Docker required), anything else, or nothing, for the aggregates in memory.
/// </summary>
[Binding]
public sealed class SpecsDriver(IUnitTestRuntimeProvider runtime)
{
    public const string Variable = "BALLBANK_SPECS_DRIVER";

    public static bool IsHttp =>
        string.Equals(Environment.GetEnvironmentVariable(Variable), "http", StringComparison.OrdinalIgnoreCase);

    /// <summary>A scenario tagged <c>@http</c> is about requests themselves, so means nothing in memory.</summary>
    [BeforeScenario("http")]
    public void SkipInMemory()
    {
        if (!IsHttp)
        {
            runtime.TestIgnore($"Runs over HTTP only: set {Variable}=http.");
        }
    }

    [AfterTestRun]
    public static Task StopTheApi() => SpecsHost.StopAsync();
}

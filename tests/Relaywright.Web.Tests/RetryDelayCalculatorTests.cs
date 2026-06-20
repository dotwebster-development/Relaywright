using Relaywright.Web.Services.Queueing;
using Xunit;

namespace Relaywright.Web.Tests;

public sealed class RetryDelayCalculatorTests
{
    [Fact]
    public void UsesExponentialBackoffUntilMax()
    {
        var calculator = new RetryDelayCalculator();

        var first = calculator.Calculate(1, 60, 3600);
        var second = calculator.Calculate(2, 60, 3600);
        var third = calculator.Calculate(3, 60, 3600);

        Assert.Equal(TimeSpan.FromSeconds(60), first);
        Assert.Equal(TimeSpan.FromSeconds(120), second);
        Assert.Equal(TimeSpan.FromSeconds(240), third);
    }

    [Fact]
    public void CapsAtMaximumDelay()
    {
        var calculator = new RetryDelayCalculator();

        var result = calculator.Calculate(10, 60, 300);

        Assert.Equal(TimeSpan.FromSeconds(300), result);
    }

    [Fact]
    public void SanitizesAttemptAndDelayInputs()
    {
        var calculator = new RetryDelayCalculator();

        var result = calculator.Calculate(0, 0, 0);

        Assert.Equal(TimeSpan.FromSeconds(1), result);
    }

    [Fact]
    public void MaxDelayCannotGoBelowInitialDelay()
    {
        var calculator = new RetryDelayCalculator();

        var result = calculator.Calculate(5, 60, 10);

        Assert.Equal(TimeSpan.FromSeconds(60), result);
    }
}

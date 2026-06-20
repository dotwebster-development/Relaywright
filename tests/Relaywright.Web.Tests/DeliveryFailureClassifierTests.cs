using Relaywright.Web.Data.Entities;
using Relaywright.Web.Services.Delivery;
using Xunit;

namespace Relaywright.Web.Tests;

public sealed class DeliveryFailureClassifierTests
{
    [Fact]
    public void InvalidOperationBecomesConfigurationFailure()
    {
        var classifier = new DeliveryFailureClassifier();

        var result = classifier.Classify(new InvalidOperationException("No upstream configured."));

        Assert.True(result.IsPermanentFailure);
        Assert.Equal(DeliveryFailureCategory.Configuration, result.FailureCategory);
    }

    [Fact]
    public void TimeoutBecomesTransientFailure()
    {
        var classifier = new DeliveryFailureClassifier();

        var result = classifier.Classify(new TimeoutException("Timeout"));

        Assert.False(result.IsPermanentFailure);
        Assert.Equal(DeliveryFailureCategory.Transient, result.FailureCategory);
    }

    [Fact]
    public void HttpRequestBecomesTransientFailure()
    {
        var classifier = new DeliveryFailureClassifier();

        var result = classifier.Classify(new HttpRequestException("Token endpoint unavailable."));

        Assert.False(result.IsPermanentFailure);
        Assert.Equal(DeliveryFailureCategory.Transient, result.FailureCategory);
    }

    [Fact]
    public void FormatExceptionBecomesPermanentMessageFormatFailure()
    {
        var classifier = new DeliveryFailureClassifier();

        var result = classifier.Classify(new FormatException("Bad mailbox."));

        Assert.True(result.IsPermanentFailure);
        Assert.Equal(DeliveryFailureCategory.MessageFormat, result.FailureCategory);
    }
}

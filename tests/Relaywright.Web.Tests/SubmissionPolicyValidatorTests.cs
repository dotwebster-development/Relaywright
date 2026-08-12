using Relaywright.Web.Data.Entities;
using Relaywright.Web.Services.Security;
using Xunit;

namespace Relaywright.Web.Tests;

public sealed class SubmissionPolicyValidatorTests
{
    private readonly SubmissionPolicyValidator validator = new();

    [Fact]
    public void ValidateRejectsUnexpectedSingletonId()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => validator.Validate(
            new SubmissionPolicy { Id = 2 }));

        Assert.Equal("Submission policy ID must be 1.", exception.Message);
    }

    [Fact]
    public void ValidateRejectsInvalidSenderPattern()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => validator.Validate(
            new SubmissionPolicy
            {
                Id = 1,
                AllowedSenderAddresses = "not-a-mailbox"
            }));

        Assert.Contains("invalid sender", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ValidateProfileRejectsInvalidDomainPattern()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => validator.ValidateProfile(
            new TrustedNetwork
            {
                AllowedRecipientDomains = "@example.test"
            }));

        Assert.Contains("invalid domain", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ValidateRejectsNonPositiveLimits()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => validator.Validate(
            new SubmissionPolicy
            {
                Id = 1,
                MaxMessageSizeBytes = 0
            }));

        Assert.Contains("at least 1", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void NormalizeListTrimsSeparatorsAndRemovesCaseInsensitiveDuplicates()
    {
        var normalized = validator.NormalizeList(" Scanner@Example.test;scanner@example.test\r\n@other.test ");

        Assert.Equal(
            $"Scanner@Example.test{Environment.NewLine}@other.test",
            normalized);
    }

    [Fact]
    public void NormalizePositiveTreatsNonPositiveValuesAsUnset()
    {
        Assert.Null(validator.NormalizePositive(0L));
        Assert.Null(validator.NormalizePositive(-1));
        Assert.Equal(1024L, validator.NormalizePositive(1024L));
        Assert.Equal(5, validator.NormalizePositive(5));
    }
}

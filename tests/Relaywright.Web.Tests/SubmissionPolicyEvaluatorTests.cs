using Relaywright.Web.Data.Entities;
using Relaywright.Web.Services.Security;
using Xunit;

namespace Relaywright.Web.Tests;

public sealed class SubmissionPolicyEvaluatorTests
{
    private readonly SubmissionPolicyEvaluator evaluator = new();

    [Fact]
    public void GlobalSenderBlockIsAppliedAfterProfileBlockAndBeforeAllowLists()
    {
        var profile = new TrustedNetwork
        {
            AllowedSenderAddresses = "@example.test"
        };
        var policy = new SubmissionPolicy
        {
            BlockedSenderAddresses = "blocked@example.test",
            AllowedSenderAddresses = "@example.test",
            IsEnabled = true
        };

        var decision = evaluator.CanAcceptFrom(profile, policy, "blocked@example.test", 100);

        Assert.False(decision.Allowed);
        Assert.Equal("Sender is blocked by submission policy.", decision.Message);
    }

    [Theory]
    [InlineData("user@partner.test", false)]
    [InlineData("user@mail.partner.test", true)]
    [InlineData("user@deep.mail.partner.test", true)]
    public void WildcardDomainRetainsSubdomainOnlySemantics(string recipient, bool expectedAllowed)
    {
        var profile = new TrustedNetwork
        {
            AllowedRecipientDomains = "*.partner.test"
        };

        var decision = evaluator.CanDeliverTo(
            profile,
            new SubmissionPolicy { IsEnabled = false },
            recipient,
            1);

        Assert.Equal(expectedAllowed, decision.Allowed);
    }

    [Fact]
    public void DisabledGlobalPolicyDoesNotContributeRecipientLimit()
    {
        var decision = evaluator.CanDeliverTo(
            new TrustedNetwork { MaxRecipientsPerMessage = 3 },
            new SubmissionPolicy
            {
                MaxRecipientsPerMessage = 1,
                IsEnabled = false
            },
            "user@example.test",
            2);

        Assert.True(decision.Allowed);
    }
}

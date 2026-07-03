using Relaywright.Web.Validation;
using Xunit;

namespace Relaywright.Web.Tests;

public sealed class ValidationRulesTests
{
    [Theory]
    [InlineData("smtp.example.test")]
    [InlineData("localhost")]
    [InlineData("127.0.0.1")]
    [InlineData("::1")]
    public void HostNameOrIpAddressAcceptsExpectedValues(string value)
    {
        Assert.True(ValidationRules.IsHostNameOrIpAddress(value));
    }

    [Theory]
    [InlineData("https://smtp.example.test")]
    [InlineData("smtp.example.test:587")]
    [InlineData("bad_host.example.test")]
    [InlineData("smtp/example")]
    public void HostNameOrIpAddressRejectsUrlsPortsAndInvalidCharacters(string value)
    {
        Assert.False(ValidationRules.IsHostNameOrIpAddress(value));
    }

    [Theory]
    [InlineData("scanner@example.test")]
    [InlineData("@example.test")]
    [InlineData("*@example.test")]
    public void SenderPolicyPatternAcceptsSupportedForms(string value)
    {
        Assert.True(ValidationRules.IsSenderPolicyPattern(value));
    }

    [Theory]
    [InlineData("example.test")]
    [InlineData("*@bad_domain.test")]
    [InlineData("@https://example.test")]
    public void SenderPolicyPatternRejectsUnsupportedForms(string value)
    {
        Assert.False(ValidationRules.IsSenderPolicyPattern(value));
    }

    [Theory]
    [InlineData("example.test")]
    [InlineData("*.partner.test")]
    [InlineData("localhost")]
    public void RecipientDomainPatternAcceptsSupportedForms(string value)
    {
        Assert.True(ValidationRules.IsRecipientDomainPattern(value));
    }

    [Theory]
    [InlineData("@example.test")]
    [InlineData("https://example.test")]
    [InlineData("*.bad_domain.test")]
    public void RecipientDomainPatternRejectsUnsupportedForms(string value)
    {
        Assert.False(ValidationRules.IsRecipientDomainPattern(value));
    }

    [Theory]
    [InlineData("11111111-1111-1111-1111-111111111111")]
    [InlineData("contoso.onmicrosoft.com")]
    public void MicrosoftTenantIdAcceptsGuidOrTenantDomain(string value)
    {
        Assert.True(ValidationRules.IsMicrosoftTenantId(value));
    }

    [Fact]
    public void MicrosoftTenantIdRejectsUrls()
    {
        Assert.False(ValidationRules.IsMicrosoftTenantId("https://contoso.onmicrosoft.com"));
    }

    [Fact]
    public void ControlCharacterValidationAllowsLineBreaksOnlyWhenRequested()
    {
        Assert.True(ValidationRules.ContainsDisallowedControlCharacter("hello\u0001", allowLineBreaks: true, out _));
        Assert.True(ValidationRules.ContainsDisallowedControlCharacter("hello\n", allowLineBreaks: false, out _));
        Assert.False(ValidationRules.ContainsDisallowedControlCharacter("hello\n", allowLineBreaks: true, out _));
    }

    [Theory]
    [InlineData("relay.pfx", true)]
    [InlineData("relay.crt", true)]
    [InlineData("relay.txt", false)]
    public void AllowedExtensionValidationMatchesExtensions(string value, bool expected)
    {
        Assert.Equal(expected, ValidationRules.HasAllowedExtension(value, [".pfx", ".crt"]));
    }
}

using WindowsAIAssistant.Infrastructure.Logging;

namespace WindowsAIAssistant.Application.Tests.Logging;

public sealed class SensitiveDataSanitizerTests
{
    [Theory]
    [InlineData("Authorization: Bearer abc123def", "abc123def")]
    [InlineData("api_key=sk-secret-example", "sk-secret-example")]
    [InlineData("password=hunter2", "hunter2")]
    [InlineData("client_secret: topsecret", "topsecret")]
    [InlineData("access_token=abc.def.ghi", "abc.def.ghi")]
    [InlineData("token=plain-secret-value", "plain-secret-value")]
    public void Sanitize_WithSensitiveValue_RedactsValue(string input, string secret)
    {
        var sanitized = SensitiveDataSanitizer.Sanitize(input);

        Assert.NotNull(sanitized);
        Assert.DoesNotContain(secret, sanitized, StringComparison.Ordinal);
        Assert.Contains(SensitiveDataSanitizer.RedactedPlaceholder, sanitized, StringComparison.Ordinal);
    }

    [Fact]
    public void Sanitize_WithPrefixedApiKey_RedactsKey()
    {
        var sanitized = SensitiveDataSanitizer.Sanitize("failed with key sk-secret-example");

        Assert.NotNull(sanitized);
        Assert.DoesNotContain("sk-secret-example", sanitized, StringComparison.Ordinal);
    }

    [Fact]
    public void Sanitize_WithJwt_RedactsToken()
    {
        const string token = "eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiIxIn0.signature";
        var sanitized = SensitiveDataSanitizer.Sanitize($"received {token} from provider");

        Assert.NotNull(sanitized);
        Assert.DoesNotContain(token, sanitized, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("File search started. 12")]
    [InlineData("AI message request completed using provider Mock.")]
    [InlineData("Application launch failed for Calculator. Application not found.")]
    public void Sanitize_WithOrdinaryText_LeavesTextUnchanged(string input)
    {
        Assert.Equal(input, SensitiveDataSanitizer.Sanitize(input));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Sanitize_WithNullOrEmpty_ReturnsInput(string? input)
    {
        Assert.Equal(input, SensitiveDataSanitizer.Sanitize(input));
    }
}

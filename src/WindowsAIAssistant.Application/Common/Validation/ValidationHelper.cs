namespace WindowsAIAssistant.Application.Common.Validation;

internal static class ValidationHelper
{
    public static string RequireText(string? value, string parameterName, string errorMessage)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException(errorMessage, parameterName);
        }

        return value;
    }
}

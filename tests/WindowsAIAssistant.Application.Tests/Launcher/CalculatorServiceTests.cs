using WindowsAIAssistant.Application.Launcher;
using WindowsAIAssistant.Core.Common;

namespace WindowsAIAssistant.Application.Tests.Launcher;

public class CalculatorServiceTests
{
    private readonly CalculatorService _calculator = new();

    [Theory]
    [InlineData("2 + 3 * 4", 14)]
    [InlineData("(2 + 3) * 4", 20)]
    [InlineData("10 / 4", 2.5)]
    [InlineData("10 × 4", 40)]
    [InlineData("10 ÷ 4", 2.5)]
    [InlineData("-5 + 2", -3)]
    [InlineData("2 ^ 10", 1024)]
    [InlineData("50%", 0.5)]
    public void Evaluate_Computes_Arithmetic(string expression, double expected)
    {
        var result = _calculator.Evaluate(expression);

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.Equal(expected, result.Value, precision: 10);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("2 +")]
    [InlineData("(2 + 3")]
    [InlineData("2 apples")]
    public void Evaluate_Rejects_NonArithmetic(string expression)
    {
        var result = _calculator.Evaluate(expression);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorCodes.LauncherCalculationInvalid, result.ErrorCode);
    }

    [Fact]
    public void Evaluate_Rejects_DivisionByZero()
    {
        var result = _calculator.Evaluate("5 / 0");

        Assert.True(result.IsFailure);
        Assert.Contains("divide by zero", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Evaluate_Never_ExecutesCode()
    {
        var result = _calculator.Evaluate("System.Math.Abs(-1)");

        Assert.True(result.IsFailure);
    }
}

using WindowsAIAssistant.Core.Common;

namespace WindowsAIAssistant.Core.Abstractions.Launcher;

/// <summary>
/// Evaluates arithmetic locally, without executing code and without a network call.
/// </summary>
public interface ICalculatorService
{
    /// <summary>Evaluates an expression.</summary>
    Result<double> Evaluate(string expression);
}

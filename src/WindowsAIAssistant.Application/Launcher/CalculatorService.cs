using System.Globalization;
using WindowsAIAssistant.Core.Abstractions.Launcher;
using WindowsAIAssistant.Core.Common;

namespace WindowsAIAssistant.Application.Launcher;

/// <summary>
/// Evaluates arithmetic with a small recursive-descent parser. It never compiles, executes, or
/// sends the expression anywhere.
/// </summary>
public sealed class CalculatorService : ICalculatorService
{
    private const int MaximumExpressionLength = 200;
    private const double MaximumMagnitude = 1e15;

    /// <inheritdoc />
    public Result<double> Evaluate(string expression)
    {
        if (string.IsNullOrWhiteSpace(expression))
        {
            return Invalid("Enter an arithmetic expression.");
        }

        var trimmed = expression.Trim();
        if (trimmed.Length > MaximumExpressionLength)
        {
            return Invalid("That expression is too long to calculate.");
        }

        try
        {
            var parser = new Parser(trimmed);
            var value = parser.ParseExpression();

            if (!parser.IsAtEnd)
            {
                return Invalid("That expression is not arithmetic this device can calculate.");
            }

            if (!double.IsFinite(value))
            {
                return Invalid("The result is not a finite number.");
            }

            if (Math.Abs(value) > MaximumMagnitude)
            {
                return Invalid("The result is too large to display.");
            }

            return Result<double>.Success(value == 0 ? 0 : value);
        }
        catch (DivideByZeroException)
        {
            return Invalid("Cannot divide by zero.");
        }
        catch (OverflowException)
        {
            return Invalid("The result is too large to display.");
        }
        catch (FormatException)
        {
            return Invalid("That expression is not arithmetic this device can calculate.");
        }
    }

    private static Result<double> Invalid(string message) =>
        Result<double>.Failure(ErrorCodes.LauncherCalculationInvalid, message);

    private sealed class Parser
    {
        private readonly string _text;
        private int _position;

        public Parser(string text)
        {
            _text = text;
        }

        public bool IsAtEnd
        {
            get
            {
                SkipWhitespace();
                return _position >= _text.Length;
            }
        }

        public double ParseExpression()
        {
            var value = ParseTerm();

            while (true)
            {
                SkipWhitespace();

                if (TryConsume('+'))
                {
                    value += ParseTerm();
                }
                else if (TryConsume('-'))
                {
                    value -= ParseTerm();
                }
                else
                {
                    return value;
                }
            }
        }

        private double ParseTerm()
        {
            var value = ParsePower();

            while (true)
            {
                SkipWhitespace();

                if (TryConsume('*') || TryConsume('×'))
                {
                    value *= ParsePower();
                }
                else if (TryConsume('/') || TryConsume('÷'))
                {
                    var divisor = ParsePower();
                    if (divisor == 0)
                    {
                        throw new DivideByZeroException();
                    }

                    value /= divisor;
                }
                else
                {
                    return value;
                }
            }
        }

        private double ParsePower()
        {
            var value = ParseUnary();

            SkipWhitespace();
            if (TryConsume('^'))
            {
                var exponent = ParsePower();
                if (Math.Abs(exponent) > 1000)
                {
                    throw new OverflowException();
                }

                value = Math.Pow(value, exponent);
            }

            return value;
        }

        private double ParseUnary()
        {
            SkipWhitespace();

            if (TryConsume('+'))
            {
                return ParseUnary();
            }

            if (TryConsume('-'))
            {
                return -ParseUnary();
            }

            return ParsePostfix();
        }

        private double ParsePostfix()
        {
            var value = ParsePrimary();

            SkipWhitespace();
            while (TryConsume('%'))
            {
                value /= 100;
                SkipWhitespace();
            }

            return value;
        }

        private double ParsePrimary()
        {
            SkipWhitespace();

            if (TryConsume('('))
            {
                var value = ParseExpression();
                SkipWhitespace();

                if (!TryConsume(')'))
                {
                    throw new FormatException();
                }

                return value;
            }

            return ParseNumber();
        }

        private double ParseNumber()
        {
            SkipWhitespace();

            var start = _position;
            var sawDigit = false;

            while (_position < _text.Length && char.IsAsciiDigit(_text[_position]))
            {
                sawDigit = true;
                _position++;
            }

            if (_position < _text.Length && _text[_position] == '.')
            {
                _position++;

                while (_position < _text.Length && char.IsAsciiDigit(_text[_position]))
                {
                    sawDigit = true;
                    _position++;
                }
            }

            if (!sawDigit)
            {
                throw new FormatException();
            }

            return double.Parse(
                _text.AsSpan(start, _position - start),
                NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture);
        }

        private bool TryConsume(char character)
        {
            if (_position < _text.Length && _text[_position] == character)
            {
                _position++;
                return true;
            }

            return false;
        }

        private void SkipWhitespace()
        {
            while (_position < _text.Length && char.IsWhiteSpace(_text[_position]))
            {
                _position++;
            }
        }
    }
}

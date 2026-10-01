using System.Diagnostics;
using System.Globalization;
using Microsoft.Extensions.Logging;
using WindowsAIAssistant.Core.Abstractions.Agents;
using WindowsAIAssistant.Core.Common;
using WindowsAIAssistant.Core.Models.Agents;

namespace WindowsAIAssistant.Application.Agents.Tools;

/// <summary>
/// Works out arithmetic, exactly, without asking a model.
/// <para>
/// This exists because a model asked to add will sometimes answer with a number that is nearly
/// right, and "nearly" is the wrong property for arithmetic. Everything here is integer and
/// decimal arithmetic done locally, with the expression parsed rather than pattern-matched, so
/// 0.1 + 0.2 is 0.3 and not 0.30000000000000004.
/// </para>
/// <para>
/// The parser accepts only numbers and the four operators. There is no variable, no function,
/// and no way to name anything outside the expression, which is what makes it safe to hand text
/// that came from a voice microphone or a model. An expression it cannot read is refused with the
/// part it choked on rather than being sent anywhere to be guessed at.
/// </para>
/// <para>
/// Read-only, so it declares no actions and never interrupts a person.
/// </para>
/// </summary>
public sealed class CalculationTool : ITool
{
    /// <summary>
    /// How long an expression may be. Long enough for anything a person would say aloud, and
    /// short enough that the parser cannot be walked off the end of a very long line.
    /// </summary>
    private const int MaximumLength = 200;

    /// <summary>
    /// How many operations one expression may contain. A chain of fifty additions is not a
    /// calculation anybody asked for, and refusing it keeps a mistranscribed microphone
    /// utterance from producing a meaningless number with great confidence.
    /// </summary>
    private const int MaximumOperations = 32;

    /// <inheritdoc />
    public string Name => "CalculationTool";

    /// <inheritdoc />
    public string Description =>
        "Evaluate an arithmetic expression exactly. Use this for any question that is really " +
        "arithmetic, rather than sending it to a model, which may round.";

    /// <inheritdoc />
    public IReadOnlyList<string> RequiredInputs => ["expression"];

    /// <summary>
    /// Declares that this tool changes nothing. It is arithmetic on a string already in hand —
    /// it touches no file, no network, and no state, so there is nothing to ask about.
    /// </summary>
    public IReadOnlyList<AgentAction> DescribeActions(AgentStep step) => [];

    /// <inheritdoc />
    public Task<ToolResult> ExecuteAsync(
        ToolRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var stopwatch = Stopwatch.StartNew();
        var expression = request.GetParameter("expression") ?? request.Goal;

        if (string.IsNullOrWhiteSpace(expression))
        {
            return Refused("There was nothing to work out.");
        }

        if (expression.Length > MaximumLength)
        {
            return Refused($"That expression is too long to work out. Keep it under {MaximumLength} characters.");
        }

        var tokens = Tokenize(expression);

        if (tokens.Count == 0)
        {
            return Refused("There was nothing to work out.");
        }

        if (tokens.Count(token => token.IsOperator) > MaximumOperations)
        {
            return Refused("That is more calculation than one request can hold.");
        }

        var parser = new Parser(tokens);

        if (!parser.TryParse(out var value, out var failure))
        {
            return Refused($"I could not work that out: {failure}");
        }

        stopwatch.Stop();

        var formatted = Format(value);

        return Task.FromResult(ToolResult.Success(
            Name,
            formatted,
            sources: null,
            data: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["expression"] = expression,
                ["result"] = formatted,
                ["summary"] = $"{expression} = {formatted}",
            },
            stopwatch.Elapsed));
    }

    /// <summary>Reports a refusal, as a sentence a person can act on rather than a parse error.</summary>
    private Task<ToolResult> Refused(string reason) =>
        Task.FromResult(ToolResult.Failure(Name, ErrorCodes.AgentCalculationFailed, reason));

    /// <summary>
    /// Splits an expression into numbers and operators, turning the spoken forms into symbols.
    /// <para>
    /// The word forms are the reason this is a tokenizer rather than a split on symbols: somebody
    /// saying "twelve times twelve" out loud produces no symbols at all, and the recognition step
    /// is the only place that can be handled.
    /// </para>
    /// </summary>
    private static List<Token> Tokenize(string expression)
    {
        var tokens = new List<Token>();
        var text = expression.ToLowerInvariant();
        var position = 0;

        while (position < text.Length)
        {
            var character = text[position];

            if (char.IsWhiteSpace(character) || character == ',')
            {
                position++;
                continue;
            }

            if (char.IsAsciiDigit(character) || character == '.')
            {
                var start = position;

                while (position < text.Length && (char.IsAsciiDigit(text[position]) || text[position] == '.'))
                {
                    position++;
                }

                var literal = text[start..position];

                if (!decimal.TryParse(literal, NumberStyles.Number, CultureInfo.InvariantCulture, out var number))
                {
                    return [];
                }

                tokens.Add(new Token(TokenKind.Number, number, literal));
                continue;
            }

            if (character is '+' or '-' or '*' or '/')
            {
                tokens.Add(new Token(character switch
                {
                    '+' => TokenKind.Add,
                    '-' => TokenKind.Subtract,
                    '*' => TokenKind.Multiply,
                    _ => TokenKind.Divide,
                }, 0, character.ToString()));

                position++;
                continue;
            }

            // The word forms, longest first so "divided by" is not read as "d".
            var word = ReadWord(text, position);

            switch (word)
            {
                case "plus":
                case "added to":
                case "add":
                    tokens.Add(new Token(TokenKind.Add, 0, "+"));
                    position += word.Length;
                    continue;

                case "minus":
                case "take away":
                case "less":
                case "subtract":
                    tokens.Add(new Token(TokenKind.Subtract, 0, "-"));
                    position += word.Length;
                    continue;

                case "times":
                case "multiplied by":
                case "multiply":
                case "x":
                    tokens.Add(new Token(TokenKind.Multiply, 0, "*"));
                    position += word.Length;
                    continue;

                case "divided by":
                case "divide":
                case "over":
                    tokens.Add(new Token(TokenKind.Divide, 0, "/"));
                    position += word.Length;
                    continue;
            }

            // Anything left over is a word the calculator does not know, and is reported as
            // unreadable rather than skipped — skipping it would change what the expression means.
            return [];
        }

        return tokens;
    }

    /// <summary>Reads the longest word beginning at a position, if it is one this tool knows.</summary>
    private static string ReadWord(string text, int position)
    {
        var known = new[] { "plus", "added to", "add", "minus", "take away", "less", "subtract", "times", "multiplied by", "multiply", "x", "divided by", "divide", "over" };

        foreach (var candidate in known.OrderByDescending(word => word.Length))
        {
            if (position + candidate.Length <= text.Length
                && string.CompareOrdinal(text, position, candidate, 0, candidate.Length) == 0)
            {
                return candidate;
            }
        }

        return string.Empty;
    }

    /// <summary>
    /// Renders a result for a person: no trailing zeros, no exponent notation, and a comma
    /// grouping only for whole numbers large enough to read better with one.
    /// </summary>
    private static string Format(decimal value)
    {
        if (value == decimal.Truncate(value) && Math.Abs(value) < 1_000_000_000m)
        {
            return value.ToString("N0", CultureInfo.InvariantCulture);
        }

        // 2.50 prints as 2.5 rather than 2.50, and a result never picks up an exponent the way
        // ToString's default format would give it for a very small or very large decimal.
        return value
            .ToString("0.############################", CultureInfo.InvariantCulture);
    }

    private enum TokenKind
    {
        Number,
        Add,
        Subtract,
        Multiply,
        Divide,
    }

    /// <summary>One number or operator, and the text it came from, for the refusal message.</summary>
    private readonly record struct Token(TokenKind Kind, decimal Value, string Text)
    {
        public bool IsOperator => Kind != TokenKind.Number;
    }

    /// <summary>
    /// A recursive-descent parser over the token list, with the usual precedence: multiplication
    /// and division bind tighter than addition, and a minus may follow an operator.
    /// <para>
    /// Written by hand rather than handed to an expression library, because the library would
    /// have to be trusted with identifiers and member access, and this only ever needs four
    /// operators over two number types.
    /// </para>
    /// </summary>
    private sealed class Parser(List<Token> tokens)
    {
        private int _position;

        public bool TryParse(out decimal value, out string failure)
        {
            if (tokens.Count == 0)
            {
                value = 0;
                failure = "there was nothing to work out.";
                return false;
            }

            // A leading minus is arithmetic rather than a negation, so "what is -5 plus 3" works.
            var negative = false;

            if (tokens[0].Kind == TokenKind.Subtract)
            {
                negative = true;
                _position = 1;
            }

            if (!TrySum(out value, out failure))
            {
                return false;
            }

            if (negative)
            {
                value = -value;
            }

            if (_position < tokens.Count)
            {
                failure = $"\"{tokens[_position].Text}\" is not something I can work out.";
                return false;
            }

            failure = string.Empty;
            return true;
        }

        private bool TrySum(out decimal value, out string failure)
        {
            if (!TryProduct(out value, out failure))
            {
                return false;
            }

            while (_position < tokens.Count && tokens[_position].Kind is TokenKind.Add or TokenKind.Subtract)
            {
                var add = tokens[_position].Kind == TokenKind.Add;
                _position++;

                if (!TryProduct(out var right, out failure))
                {
                    return false;
                }

                value = add ? value + right : value - right;
            }

            failure = string.Empty;
            return true;
        }

        private bool TryProduct(out decimal value, out string failure)
        {
            if (!TryFactor(out value, out failure))
            {
                return false;
            }

            while (_position < tokens.Count && tokens[_position].Kind is TokenKind.Multiply or TokenKind.Divide)
            {
                var divide = tokens[_position].Kind == TokenKind.Divide;
                _position++;

                if (!TryFactor(out var right, out failure))
                {
                    return false;
                }

                if (divide && right == 0)
                {
                    // Refused rather than thrown, because a person asking for it deserves an
                    // answer rather than an exception somewhere above them.
                    value = 0;
                    failure = "\"0\" cannot be a divisor.";
                    return false;
                }

                value = divide ? value / right : value * right;
            }

            failure = string.Empty;
            return true;
        }

        private bool TryFactor(out decimal value, out string failure)
        {
            if (_position >= tokens.Count)
            {
                value = 0;
                failure = "the expression stops early.";
                return false;
            }

            var token = tokens[_position];

            if (token.Kind == TokenKind.Number)
            {
                value = token.Value;
                _position++;
                failure = string.Empty;
                return true;
            }

            if (token.Kind == TokenKind.Subtract)
            {
                // A minus straight after another operator negates the factor, so "5 * -3" works.
                _position++;

                if (!TryFactor(out value, out failure))
                {
                    return false;
                }

                value = -value;
                return true;
            }

            value = 0;
            failure = $"\"{token.Text}\" is not something I can work out.";
            return false;
        }
    }
}

using System;
using System.Globalization;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data.Converters;

namespace Vectorier.Controls;

/// <summary>
/// Every number field (NumericUpDown) of the app accepts simple math: "200-200" gives 0, "64*3+8" gives 200,
/// "(10+5)/2" gives 7.5. Supported: + - * / % ^, parentheses and unary signs. Plain numbers keep the field's own
/// parsing, and the field keeps its own FormatString when it shows the result.
/// </summary>
public static class MathFields
{
    public static void Install()
    {
        TemplatedControl.TemplateAppliedEvent.AddClassHandler<NumericUpDown>((box, _) =>
        {
            if (box.TextConverter == null)
                box.TextConverter = new Converter(box);
        });
    }

    /// <summary>Per-field converter: text → value evaluates the expression, value → text formats like the field.</summary>
    private sealed class Converter : IValueConverter
    {
        private readonly WeakReference<NumericUpDown> _box;

        public Converter(NumericUpDown box) => _box = new WeakReference<NumericUpDown>(box);

        // NumericUpDown calls Convert for text → value.
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is not string text || string.IsNullOrWhiteSpace(text))
                return null;
            _box.TryGetTarget(out var box);
            var format = box?.NumberFormat ?? culture.NumberFormat;
            if (TryEvaluate(text, format, out decimal result))
                return result;
            // Fallback: the field's own parsing (thousand separators...), but never a trailing sign ("200-" is not -200).
            var style = (box?.ParsingNumberStyle ?? NumberStyles.Any) & ~NumberStyles.AllowTrailingSign & ~NumberStyles.AllowParentheses;
            if (decimal.TryParse(text, style, format, out result))
                return result;
            // Unfinished input while typing ("200-"): the field keeps its current value.
            throw new FormatException();
        }

        // NumericUpDown calls ConvertBack for value → text.
        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is not decimal d)
                return null;
            if (!_box.TryGetTarget(out var box))
                return d.ToString(CultureInfo.CurrentCulture);
            string formatString = box.FormatString ?? string.Empty;
            var format = box.NumberFormat ?? culture.NumberFormat;
            return formatString.Contains("{0", StringComparison.Ordinal)
                ? string.Format(format, formatString, d)
                : d.ToString(formatString, format);
        }
    }

    /// <summary>Evaluates a math expression. Returns false for anything that is not a complete expression.</summary>
    public static bool TryEvaluate(string text, NumberFormatInfo? format, out decimal result)
    {
        result = 0;
        var parser = new Parser(text, format ?? NumberFormatInfo.CurrentInfo);
        try
        {
            double v = parser.ParseExpression();
            parser.SkipSpaces();
            if (!parser.AtEnd || double.IsNaN(v) || double.IsInfinity(v))
                return false;
            if (Math.Abs(v) > (double)decimal.MaxValue / 10)
                return false;
            result = (decimal)Math.Round(v, 10);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private sealed class Parser
    {
        private readonly string _s;
        private readonly string _decimalSeparator;
        private int _i;

        public Parser(string s, NumberFormatInfo format)
        {
            _s = s;
            _decimalSeparator = format.NumberDecimalSeparator;
        }

        public bool AtEnd => _i >= _s.Length;

        public void SkipSpaces()
        {
            while (_i < _s.Length && char.IsWhiteSpace(_s[_i]))
                _i++;
        }

        private bool Eat(char c)
        {
            SkipSpaces();
            if (_i < _s.Length && _s[_i] == c)
            {
                _i++;
                return true;
            }
            return false;
        }

        // expression := term (('+' | '-') term)*
        public double ParseExpression()
        {
            double v = ParseTerm();
            while (true)
            {
                if (Eat('+')) v += ParseTerm();
                else if (Eat('-') || Eat('−')) v -= ParseTerm();
                else return v;
            }
        }

        // term := power (('*' | '/' | '%') power)*
        private double ParseTerm()
        {
            double v = ParsePower();
            while (true)
            {
                if (Eat('*') || Eat('×')) v *= ParsePower();
                else if (Eat('/') || Eat('÷'))
                {
                    double d = ParsePower();
                    if (d == 0) throw new FormatException();
                    v /= d;
                }
                else if (Eat('%'))
                {
                    double d = ParsePower();
                    if (d == 0) throw new FormatException();
                    v %= d;
                }
                else return v;
            }
        }

        // power := unary ('^' power)?
        private double ParsePower()
        {
            double v = ParseUnary();
            if (Eat('^'))
                v = Math.Pow(v, ParsePower());
            return v;
        }

        private double ParseUnary()
        {
            if (Eat('-') || Eat('−')) return -ParseUnary();
            if (Eat('+')) return ParseUnary();
            return ParsePrimary();
        }

        private double ParsePrimary()
        {
            if (Eat('('))
            {
                double v = ParseExpression();
                if (!Eat(')')) throw new FormatException();
                return v;
            }
            SkipSpaces();
            int start = _i;
            bool digits = false, separator = false;
            var sb = new System.Text.StringBuilder();
            while (_i < _s.Length)
            {
                char c = _s[_i];
                if (char.IsDigit(c))
                {
                    sb.Append(c);
                    digits = true;
                    _i++;
                }
                else if (!separator && (c == '.' || string.CompareOrdinal(_s, _i, _decimalSeparator, 0, _decimalSeparator.Length) == 0))
                {
                    sb.Append('.');
                    separator = true;
                    _i += c == '.' ? 1 : _decimalSeparator.Length;
                }
                else break;
            }
            if (!digits)
            {
                _i = start;
                throw new FormatException();
            }
            // Exponent (1e3).
            if (_i < _s.Length && (_s[_i] == 'e' || _s[_i] == 'E'))
            {
                int save = _i;
                _i++;
                var exp = new System.Text.StringBuilder("e");
                if (_i < _s.Length && (_s[_i] == '+' || _s[_i] == '-')) exp.Append(_s[_i++]);
                bool expDigits = false;
                while (_i < _s.Length && char.IsDigit(_s[_i])) { exp.Append(_s[_i++]); expDigits = true; }
                if (expDigits) sb.Append(exp);
                else _i = save;
            }
            return double.Parse(sb.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture);
        }
    }
}

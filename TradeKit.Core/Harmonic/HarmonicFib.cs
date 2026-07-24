namespace TradeKit.Core.Harmonic;

/// <summary>
/// Exact Fibonacci ratios used by the harmonic models.
/// <para>
/// The irrational values are taken verbatim from <c>fib_precise()</c> of the reference
/// Pine library. Using the rounded 0.618/0.786/0.886/1.272 literals instead makes the
/// leg ratios and the resulting scores diverge from the Pine indicator, so the precise
/// constants must be used everywhere. 0.5, 1.13, 2.24 and 3.618 are literal in Pine too.
/// </para>
/// </summary>
public static class HarmonicFib
{
    /// <summary>0.382 (precise).</summary>
    public const double F382 = 0.3819660112501052;

    /// <summary>0.5 (literal).</summary>
    public const double F500 = 0.5;

    /// <summary>0.618 (precise).</summary>
    public const double F618 = 0.6180339887498948;

    /// <summary>0.786 (precise).</summary>
    public const double F786 = 0.7861513777574233;

    /// <summary>0.886 (precise).</summary>
    public const double F886 = 0.8866517793121622;

    /// <summary>1.13 (literal).</summary>
    public const double F113 = 1.13;

    /// <summary>1.272 (precise).</summary>
    public const double F1272 = 1.2720196495140689;

    /// <summary>1.414 (precise).</summary>
    public const double F1414 = 1.4142135623730950;

    /// <summary>1.618 (precise).</summary>
    public const double F1618 = 1.6180339887498948;

    /// <summary>2.24 (literal).</summary>
    public const double F224 = 2.24;

    /// <summary>2.618 (precise).</summary>
    public const double F2618 = 2.6180339887498948;

    /// <summary>3.618 (literal).</summary>
    public const double F3618 = 3.618;
}

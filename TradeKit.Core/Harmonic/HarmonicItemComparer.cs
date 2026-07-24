namespace TradeKit.Core.Harmonic;

/// <summary>
/// Compares harmonic patterns by their identity - the model and the X/A/B/C/D points.
/// </summary>
public class HarmonicItemComparer : IEqualityComparer<HarmonicItem>
{
    /// <inheritdoc/>
    public bool Equals(HarmonicItem x, HarmonicItem y)
    {
        if (ReferenceEquals(x, y))
            return true;

        if (x is null || y is null)
            return false;

        return x.Equals(y);
    }

    /// <inheritdoc/>
    public int GetHashCode(HarmonicItem obj)
    {
        return obj.GetHashCode();
    }
}

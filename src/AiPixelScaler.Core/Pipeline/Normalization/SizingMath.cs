namespace AiPixelScaler.Core.Pipeline.Normalization;

/// <summary>
/// Helper di sizing per pipeline di normalizzazione (POT, multipli, allineamenti).
/// Niente dipendenze esterne: pure aritmetica intera.
/// </summary>
public static class SizingMath
{
    /// <summary>
    /// Prossima potenza di 2 ≥ <paramref name="n"/>. Per <c>n &lt;= 1</c> ritorna 1.
    /// Se <c>n</c> è già potenza di 2, ritorna <c>n</c> invariato.
    /// Esempi: 1→1, 2→2, 3→4, 192→256, 1024→1024, 1025→2048.
    /// </summary>
    public static int NextPow2(int n)
    {
        if (n <= 1) return 1;
        n--;
        n |= n >> 1;
        n |= n >> 2;
        n |= n >> 4;
        n |= n >> 8;
        n |= n >> 16;
        return n + 1;
    }

    /// <summary>True se <paramref name="n"/> &gt; 0 e potenza di 2.</summary>
    public static bool IsPow2(int n) => n > 0 && (n & (n - 1)) == 0;

    /// <summary>Multiplo di <paramref name="m"/> ≥ <paramref name="n"/> (utile per atlas tile-aligned).</summary>
    public static int CeilToMultiple(int n, int m)
    {
        if (m < 1) return n;
        return ((n + m - 1) / m) * m;
    }
}

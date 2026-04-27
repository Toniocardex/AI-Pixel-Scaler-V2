using SixLabors.ImageSharp.PixelFormats;

namespace AiPixelScaler.Core.Color;

/// <summary>
/// Spazio colore Oklab di Björn Ottosson (2020) — percettivamente uniforme,
/// più accurato di CIELab per gradiente e color matching su display sRGB.
///
/// La distanza Euclidea in Oklab approssima ΔE percettivo:
///   ΔE_OK = √[(L1−L2)² + (a1−a2)² + (b1−b2)²]
///   JND tipico ≈ 0.02 ; tolleranze pratiche per chroma/key: 0.03–0.10
///
/// Pipeline:
///   sRGB(byte) → linear (LUT) → LMS (M1) → ³√LMS → Lab (M2)
///
/// Implementazione di precisione:
///   1. Tutto il calcolo interno è in <c>double</c> (cbrt/cube e i prodotti
///      matriciali amplificano in float32 i blu scuri).
///   2. Le matrici inverse M1⁻¹ e M2⁻¹ sono <b>derivate run-time</b>
///      (Mat3.Invert) dalle forward, NON dai coefficienti pubblicati di Björn:
///      quelli sono arrotondati a 10 cifre e NON sono inverso esatto di M1/M2,
///      causando round-trip error di ~10 byte su bianco. Con l'inversa
///      analitica il round-trip rientra nella precisione del double (≤ 1 byte).
/// </summary>
public readonly record struct Oklab(float L, float A, float B)
{
    // ── Forward matrices (linear RGB → LMS, ³√LMS → Lab) ─────────────────────
    private static readonly double[,] M1 =
    {
        { 0.4122214708, 0.5363325363, 0.0514459929 },
        { 0.2119034982, 0.6806995451, 0.1361706379 },
        { 0.0883024619, 0.2817188376, 0.6299787005 },
    };

    private static readonly double[,] M2 =
    {
        {  0.2104542553,  0.7936177850, -0.0040720468 },
        {  1.9779984951, -2.4285922050,  0.4505937099 },
        {  0.0259040371,  0.7827717662, -0.8086757660 },
    };

    // ── Inverse matrices (Lab → ³√LMS, LMS → linear RGB) ─────────────────────
    // Calcolate analiticamente: garantiscono M·M⁻¹ = I esatto in double.
    private static readonly double[,] M1Inv = Mat3.Invert(M1);
    private static readonly double[,] M2Inv = Mat3.Invert(M2);

    // ─────────────────────────────────────────────────────────────────────────

    public static Oklab FromLinearRgb(double r, double g, double b)
    {
        var l = M1[0, 0] * r + M1[0, 1] * g + M1[0, 2] * b;
        var m = M1[1, 0] * r + M1[1, 1] * g + M1[1, 2] * b;
        var s = M1[2, 0] * r + M1[2, 1] * g + M1[2, 2] * b;

        var l_ = System.Math.Cbrt(l);
        var m_ = System.Math.Cbrt(m);
        var s_ = System.Math.Cbrt(s);

        return new Oklab(
            (float)(M2[0, 0] * l_ + M2[0, 1] * m_ + M2[0, 2] * s_),
            (float)(M2[1, 0] * l_ + M2[1, 1] * m_ + M2[1, 2] * s_),
            (float)(M2[2, 0] * l_ + M2[2, 1] * m_ + M2[2, 2] * s_));
    }

    public static Oklab FromSrgb(Rgba32 c) =>
        FromLinearRgb(Srgb.ToLinearD(c.R), Srgb.ToLinearD(c.G), Srgb.ToLinearD(c.B));

    /// <summary>Inversa: Oklab → linear RGB (può uscire fuori gamut, va clampato).</summary>
    public (double r, double g, double b) ToLinearRgbDouble()
    {
        double L = this.L, A = this.A, B = this.B;

        var l_ = M2Inv[0, 0] * L + M2Inv[0, 1] * A + M2Inv[0, 2] * B;
        var m_ = M2Inv[1, 0] * L + M2Inv[1, 1] * A + M2Inv[1, 2] * B;
        var s_ = M2Inv[2, 0] * L + M2Inv[2, 1] * A + M2Inv[2, 2] * B;

        var l = l_ * l_ * l_;
        var m = m_ * m_ * m_;
        var s = s_ * s_ * s_;

        return (
            M1Inv[0, 0] * l + M1Inv[0, 1] * m + M1Inv[0, 2] * s,
            M1Inv[1, 0] * l + M1Inv[1, 1] * m + M1Inv[1, 2] * s,
            M1Inv[2, 0] * l + M1Inv[2, 1] * m + M1Inv[2, 2] * s);
    }

    /// <summary>API legacy float (ancora usata dove la precisione non è critica).</summary>
    public (float r, float g, float b) ToLinearRgb()
    {
        var (r, g, b) = ToLinearRgbDouble();
        return ((float)r, (float)g, (float)b);
    }

    public Rgba32 ToSrgb(byte alpha = 255)
    {
        var (r, g, b) = ToLinearRgbDouble();
        return new Rgba32(Srgb.ToSrgb(r), Srgb.ToSrgb(g), Srgb.ToSrgb(b), alpha);
    }

    /// <summary>ΔE_OK² fra due colori (squared per evitare sqrt nei loop hot).</summary>
    public static float DistanceSquared(in Oklab a, in Oklab b)
    {
        var dL = a.L - b.L;
        var dA = a.A - b.A;
        var dB = a.B - b.B;
        return dL * dL + dA * dA + dB * dB;
    }

    /// <summary>ΔE_OK fra due colori. JND ≈ 0.02.</summary>
    public static float Distance(in Oklab a, in Oklab b) =>
        MathF.Sqrt(DistanceSquared(a, b));
}

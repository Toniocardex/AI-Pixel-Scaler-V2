namespace AiPixelScaler.Core.Color;

/// <summary>
/// Conversioni gamma sRGB ↔ linear (IEC 61966-2-1).
/// Le metriche di colore percettive (Oklab, ΔE) DEVONO essere calcolate in spazio lineare,
/// non sui valori sRGB grezzi.
///
/// Formule:
///   sRGB→lin: v_lin = (v ≤ 0.04045) ? v/12.92 : ((v+0.055)/1.055)^2.4
///   lin→sRGB: v    = (v_lin ≤ 0.0031308) ? v_lin*12.92 : 1.055*v_lin^(1/2.4) − 0.055
///
/// La LUT precalcolata (forward) usa <c>double</c> per minimizzare l'errore
/// di round-trip a valle (cbrt/cube in Oklab amplificano errori sui linear molto bassi).
/// </summary>
public static class Srgb
{
    private static readonly double[] _toLinearD = BuildToLinear();

    private static double[] BuildToLinear()
    {
        var lut = new double[256];
        for (var i = 0; i < 256; i++)
        {
            var v = i / 255.0;
            lut[i] = v <= 0.04045
                ? v / 12.92
                : System.Math.Pow((v + 0.055) / 1.055, 2.4);
        }
        return lut;
    }

    /// <summary>byte sRGB → float lineare [0..1] via LUT.</summary>
    public static float ToLinear(byte v) => (float)_toLinearD[v];

    /// <summary>byte sRGB → double lineare [0..1] via LUT (massima precisione).</summary>
    public static double ToLinearD(byte v) => _toLinearD[v];

    /// <summary>float lineare [0..1] → byte sRGB con clamping.</summary>
    public static byte ToSrgb(float linear) => ToSrgb((double)linear);

    /// <summary>double lineare [0..1] → byte sRGB con clamping (massima precisione).</summary>
    public static byte ToSrgb(double linear)
    {
        if (linear <= 0.0) return 0;
        if (linear >= 1.0) return 255;
        var s = linear <= 0.0031308
            ? linear * 12.92
            : 1.055 * System.Math.Pow(linear, 1.0 / 2.4) - 0.055;
        var i = (int)System.Math.Round(s * 255.0);
        return (byte)System.Math.Clamp(i, 0, 255);
    }
}

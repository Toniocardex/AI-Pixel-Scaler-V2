namespace AiPixelScaler.Core.Color;

/// <summary>
/// Inversa analitica di una matrice 3×3 in <c>double</c> precisione.
/// Usata per derivare run-time gli inversi esatti dei matrici Oklab,
/// invece di affidarsi ai coefficienti pubblicati arrotondati a 10 cifre
/// (che NON sono inverso esatto della matrice forward → introducono errore
/// di round-trip percepibile, ~10 byte su immagini bianche).
/// </summary>
internal static class Mat3
{
    /// <summary>
    /// Calcola l'inversa di una matrice 3×3 (riga-major). Il chiamante è
    /// responsabile della non-singolarità (det ≠ 0).
    /// </summary>
    public static double[,] Invert(double[,] m)
    {
        var a = m[0, 0]; var b = m[0, 1]; var c = m[0, 2];
        var d = m[1, 0]; var e = m[1, 1]; var f = m[1, 2];
        var g = m[2, 0]; var h = m[2, 1]; var i = m[2, 2];

        var A =  (e * i - f * h);
        var B = -(d * i - f * g);
        var C =  (d * h - e * g);
        var D = -(b * i - c * h);
        var E =  (a * i - c * g);
        var F = -(a * h - b * g);
        var G =  (b * f - c * e);
        var H = -(a * f - c * d);
        var I =  (a * e - b * d);

        var det = a * A + b * B + c * C;
        if (System.Math.Abs(det) < 1e-30)
            throw new System.ArgumentException("Matrice singolare", nameof(m));

        var invDet = 1.0 / det;
        return new double[,]
        {
            { A * invDet, D * invDet, G * invDet },
            { B * invDet, E * invDet, H * invDet },
            { C * invDet, F * invDet, I * invDet },
        };
    }
}

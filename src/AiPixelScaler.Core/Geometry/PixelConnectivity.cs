namespace AiPixelScaler.Core.Geometry;

/// <summary>
/// Tipo di vicinato pixel usato dagli algoritmi di connected-component analysis.
///
/// • <see cref="Four"/>  (Von Neumann, a croce)  — considera solo i 4 vicini ortogonali (N/S/E/W).
///   Più conservativo: due blob collegati solo in diagonale restano componenti separate.
///
/// • <see cref="Eight"/> (Moore, 3×3)            — include anche i vicini diagonali.
///   Più aggressivo: texture a "scacchiera" o dettagli diagonali sottili vengono fusi in una componente.
///
/// Usato da: <c>IslandDenoise</c>, <c>Outline1Px</c>, <c>CclAutoSlicer</c>.
/// </summary>
public enum PixelConnectivity
{
    /// <summary>4 vicini ortogonali (N/S/E/W). Alias Von Neumann.</summary>
    Four,

    /// <summary>8 vicini (N/S/E/W + diagonali). Alias Moore.</summary>
    Eight,
}

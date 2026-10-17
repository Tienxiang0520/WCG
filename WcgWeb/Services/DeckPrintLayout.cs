using System.Globalization;

namespace WcgWeb.Services;

public enum PrintPaper { A4, Letter }

// Physical print geometry in millimetres. The web layout is separate; this only drives the print sheet.
// Without bleed, cards are 63 × 88 mm in a 3 × 3 grid with a 2 mm gutter for crop marks.
// With bleed, each full-colour card gets 3 mm of frame colour on every side (69 × 94 mm). Three of those
// never fit across A4 or Letter, so cards are turned sideways: 2 columns × 4 rows on A4, 2 × 3 on Letter.
public sealed record DeckPrintLayout(PrintPaper Paper, bool Bleed)
{
    public const double CardWidth = 63, CardHeight = 88, BleedMm = 3, Gutter = 2, HeadingMm = 6.5, FooterMm = 6.5;
    public bool IsA4 => Paper == PrintPaper.A4;
    public double PaperWidth => IsA4 ? 210 : 215.9;
    public double PaperHeight => IsA4 ? 297 : 279.4;
    public double MarginX => 6;
    public double MarginY => IsA4 && !Bleed ? 6 : 5;
    public int Columns => Bleed ? 2 : 3;
    public int Rows => Bleed ? (IsA4 ? 4 : 3) : 3;
    public int CardsPerPage => Columns * Rows;
    // Cell size on the page (bleed cells are turned sideways).
    public double CellWidth => Bleed ? CardHeight + 2 * BleedMm : CardWidth;
    public double CellHeight => Bleed ? CardWidth + 2 * BleedMm : CardHeight;
    public double Gap => Bleed ? 0 : Gutter;
    public double GridWidth => Columns * CellWidth + (Columns - 1) * Gap;
    public double GridHeight => Rows * CellHeight + (Rows - 1) * Gap;
    public bool ShowHeading => IsA4 || Bleed;
    public bool ShowFooter => IsA4 ? !Bleed : Bleed;
    public double UsedHeight => GridHeight + (ShowHeading ? HeadingMm : 0) + (ShowFooter ? FooterMm : 0);
    public double PrintableWidth => PaperWidth - 2 * MarginX;
    public double PrintableHeight => PaperHeight - 2 * MarginY;
    public bool Fits => GridWidth <= PrintableWidth && UsedHeight <= PrintableHeight;
    public int PageCount(int cards) => (cards + CardsPerPage - 1) / CardsPerPage;
    public string SheetClass => (IsA4 ? "paper-a4" : "paper-letter") + (Bleed ? " bleed" : "");
    // The default A4 rule lives in deck-print.css; other layouts add this rule after it.
    public string? PageRule => IsA4 && !Bleed ? null : string.Create(CultureInfo.InvariantCulture,
        $"@page {{ size: {(IsA4 ? "A4" : "letter")} portrait; margin: {MarginY}mm {MarginX}mm; }}");
}

using System.Globalization;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace EasyRest;

/// <summary>Feedback visual del drag &amp; drop del árbol: etiqueta que sigue al cursor,
/// línea de inserción para reordenar y resaltado del contenedor destino.</summary>
public class TreeDragAdorner : Adorner
{
    static readonly Brush AccentBrush = Frozen(Color.FromRgb(0x89, 0xB4, 0xFA));
    static readonly Brush PillBackground = Frozen(Color.FromArgb(242, 0x31, 0x32, 0x44));
    static readonly Brush LabelBrush = Frozen(Color.FromRgb(0xCD, 0xD6, 0xF4));
    static readonly Brush HighlightFill = Frozen(Color.FromArgb(36, 0x89, 0xB4, 0xFA));
    static readonly Pen LinePen = FrozenPen(new Pen(AccentBrush, 2));
    static readonly Pen BorderPen = FrozenPen(new Pen(AccentBrush, 1.3));

    static Brush Frozen(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }

    static Pen FrozenPen(Pen p)
    {
        p.Freeze();
        return p;
    }

    public string Label { get; set; } = "";
    public Point CursorPosition { get; set; }

    /// <summary>Línea de inserción: se dibuja en Y, desde X hasta Right.</summary>
    public Rect? InsertionLine { get; set; }

    /// <summary>Rectángulo del contenedor destino (mover adentro).</summary>
    public Rect? Highlight { get; set; }

    public TreeDragAdorner(UIElement adornedElement) : base(adornedElement)
    {
        IsHitTestVisible = false;
    }

    protected override void OnRender(DrawingContext dc)
    {
        if (Highlight is { } rect)
            dc.DrawRoundedRectangle(HighlightFill, BorderPen, rect, 4, 4);

        if (InsertionLine is { } line)
        {
            dc.DrawLine(LinePen, new Point(line.X, line.Y), new Point(line.Right, line.Y));
            dc.DrawEllipse(AccentBrush, null, new Point(line.X, line.Y), 3.2, 3.2);
        }

        if (Label.Length > 0)
        {
            var text = new FormattedText(Label, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
                new Typeface("Segoe UI"), 12, LabelBrush, VisualTreeHelper.GetDpi(this).PixelsPerDip)
            {
                MaxTextWidth = 260,
                MaxLineCount = 1,
                Trimming = TextTrimming.CharacterEllipsis
            };
            var origin = new Point(CursorPosition.X + 14, CursorPosition.Y + 12);
            var pill = new Rect(origin, new Size(text.WidthIncludingTrailingWhitespace + 16, text.Height + 8));
            dc.DrawRoundedRectangle(PillBackground, BorderPen, pill, 6, 6);
            dc.DrawText(text, new Point(origin.X + 8, origin.Y + 4));
        }
    }
}

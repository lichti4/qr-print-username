using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using QrLabelPrinter.Models;

namespace QrLabelPrinter.Views;

/// <summary>
/// Zeichnet das Etikett maßstabsgetreu: Die Zeichenfläche hat die Größe in Drucker-Dots
/// und wird per Viewbox skaliert. Dieselben Layout-Daten erzeugen auch den ZPL-Code.
/// Die Schriftdarstellung ist eine Annäherung (Arial) an die Druckerschrift.
/// </summary>
public sealed class LabelPreviewControl : Decorator
{
    private const double FontScale = 0.8;   // ZPL-Zeichenhöhe -> WPF-FontSize (Näherung)

    public static readonly DependencyProperty ModelProperty = DependencyProperty.Register(
        nameof(Model), typeof(LabelPreviewModel), typeof(LabelPreviewControl),
        new PropertyMetadata(null, (d, _) => ((LabelPreviewControl)d).Rebuild()));

    public LabelPreviewModel? Model
    {
        get => (LabelPreviewModel?)GetValue(ModelProperty);
        set => SetValue(ModelProperty, value);
    }

    private void Rebuild()
    {
        var model = Model;
        if (model is null) { Child = null; return; }

        var layout = model.Layout;
        var canvas = new Canvas
        {
            Width = layout.WidthDots,
            Height = layout.HeightDots,
            Background = Brushes.White,
            ClipToBounds = true,
            SnapsToDevicePixels = true
        };

        // QR-Code als eine einzige Geometrie (keine Nähte zwischen Modulen).
        var qr = layout.Qr;
        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            int n = model.QrMatrix.GetLength(0);
            double m = qr.Magnification;
            for (int r = 0; r < n; r++)
                for (int c = 0; c < n; c++)
                {
                    if (!model.QrMatrix[r, c]) continue;
                    double x = c * m, y = r * m;
                    ctx.BeginFigure(new Point(x, y), true, true);
                    ctx.LineTo(new Point(x + m, y), false, false);
                    ctx.LineTo(new Point(x + m, y + m), false, false);
                    ctx.LineTo(new Point(x, y + m), false, false);
                }
        }
        geometry.Freeze();

        var path = new System.Windows.Shapes.Path { Data = geometry, Fill = Brushes.Black };
        RenderOptions.SetEdgeMode(path, EdgeMode.Aliased);
        Canvas.SetLeft(path, qr.X);
        Canvas.SetTop(path, qr.Y);
        canvas.Children.Add(path);

        foreach (var line in layout.Lines)
        {
            var tb = new TextBlock
            {
                Text = line.Text,
                FontFamily = new FontFamily("Arial"),
                FontSize = line.FontHeight * FontScale,
                Foreground = Brushes.Black,
                TextWrapping = TextWrapping.NoWrap
            };
            Canvas.SetLeft(tb, line.X);
            Canvas.SetTop(tb, line.Y);
            canvas.Children.Add(tb);
        }

        Child = new Border
        {
            BorderBrush = Brushes.Gray,
            BorderThickness = new Thickness(1),
            Background = Brushes.White,
            Effect = new DropShadowEffect { BlurRadius = 8, ShadowDepth = 2, Opacity = 0.3 },
            Child = new Viewbox { Stretch = Stretch.Uniform, Child = canvas }
        };
    }
}

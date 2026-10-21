using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Ink;
using System.Windows.Shapes;

namespace InfiniteCanvas.App;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    private Point _panStart;
    private bool _isPanning = false;

    public MainWindow()
    {
        InitializeComponent();
        InitInkCanvas();
    }

    private void InitInkCanvas()
    {
        // Configuration initiale du stylet pour capter la pression nativement
        MyInkCanvas.DefaultDrawingAttributes = new DrawingAttributes
        {
            Color = Colors.Black,
            Width = 3,
            Height = 3,
            FitToCurve = true, // Force le lissage vectoriel en courbes de Bézier natif WPF
            IgnorePressure = false // Important pour votre XP-Pen !
        };
    }

    // --- GESTION DES MODES DE DESSIN ---
    private void SetPenMode(object sender, RoutedEventArgs e)
    {
        if (MyInkCanvas == null) return;
        MyInkCanvas.EditingMode = InkCanvasEditingMode.Ink;
    }

    private void SetEraserMode(object sender, RoutedEventArgs e)
    {
        // Mode Gomme par trait (Stroke), efface le vecteur complet au toucher, idéal pour la performance
        MyInkCanvas.EditingMode = InkCanvasEditingMode.EraseByStroke;
    }

    private void SelectColor(object sender, RoutedEventArgs e)
    {
        var button = sender as Button;
        if (button != null && button.Tag != null)
        {
            var colorStr = button.Tag.ToString();
            MyInkCanvas.DefaultDrawingAttributes.Color = (Color)ColorConverter.ConvertFromString(colorStr);
            // Repasser automatiquement en mode écriture si on choisit une couleur
            MyInkCanvas.EditingMode = InkCanvasEditingMode.Ink;
            BtnPen.IsChecked = true;
        }
    }

    // --- GESTION DU ZOOM INTERACTIF ---
    private void CanvasContainer_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        double zoomFactor = e.Delta > 0 ? 1.1 : 0.9;

        // Contraintes de sécurité pour le zoom (entre 10% et 800%)
        if (ControlScale.ScaleX * zoomFactor < 0.1 || ControlScale.ScaleX * zoomFactor > 8.0)
            return;

        // Obtenir la position de la souris par rapport au canvas pour zoomer vers le pointeur
        Point mousePos = e.GetPosition(MainCanvas);

        ControlScale.ScaleX *= zoomFactor;
        ControlScale.ScaleY *= zoomFactor;

        // Ajustement pour maintenir le point sous la souris stable
        ControlTranslate.X -= (mousePos.X * (zoomFactor - 1)) * ControlScale.ScaleX;
        ControlTranslate.Y -= (mousePos.Y * (zoomFactor - 1)) * ControlScale.ScaleY;
    }

    // --- GESTION DU PAN (DÉPLACEMENT INFINI AVEC LE CLIC DROIT OU CENTRAL) ---
    private void CanvasContainer_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Right || e.ChangedButton == MouseButton.Middle)
        {
            _isPanning = true;
            _panStart = e.GetPosition(CanvasContainer);
            CanvasContainer.CaptureMouse();
        }
    }

    private void CanvasContainer_MouseMove(object sender, MouseEventArgs e)
    {
        if (_isPanning)
        {
            Point currentPos = e.GetPosition(CanvasContainer);
            Vector delta = currentPos - _panStart;

            ControlTranslate.X += delta.X;
            ControlTranslate.Y += delta.Y;

            _panStart = currentPos;
        }
    }

    private void CanvasContainer_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_isPanning && (e.ChangedButton == MouseButton.Right || e.ChangedButton == MouseButton.Middle))
        {
            _isPanning = false;
            CanvasContainer.ReleaseMouseCapture();
        }
    }

    // --- AJOUT DE FORMES GÉOMÉTRIQUES VECTORIELLES BASIQUES ---
    public void AddRectangle(object sender, RoutedEventArgs e)
    {
        var rect = new Rectangle
        {
            Width = 150,
            Height = 100,
            Stroke = new SolidColorBrush(MyInkCanvas.DefaultDrawingAttributes.Color),
            StrokeThickness = 3,
            Fill = new SolidColorBrush(Color.FromArgb(30, 0, 0, 0)) // Transparent soft fill
        };
        // Ajout au centre relatif de la vue
        Canvas.SetLeft(rect, 250);
        Canvas.SetTop(rect, 200);
        MyInkCanvas.Children.Add(rect);
    }

    public void AddEllipse(object sender, RoutedEventArgs e)
    {
        var ellipse = new Ellipse
        {
            Width = 120,
            Height = 120,
            Stroke = new SolidColorBrush(MyInkCanvas.DefaultDrawingAttributes.Color),
            StrokeThickness = 3,
            Fill = new SolidColorBrush(Color.FromArgb(30, 0, 0, 0))
        };
        Canvas.SetLeft(ellipse, 250);
        Canvas.SetTop(ellipse, 200);
        MyInkCanvas.Children.Add(ellipse);
    }

    public void AddTriangle(object sender, RoutedEventArgs e)
    {
        var polygon = new Polygon
        {
            Points = new PointCollection { new Point(50, 0), new Point(0, 100), new Point(100, 100) },
            Stroke = new SolidColorBrush(MyInkCanvas.DefaultDrawingAttributes.Color),
            StrokeThickness = 3,
            Fill = new SolidColorBrush(Color.FromArgb(30, 0, 0, 0))
        };
        Canvas.SetLeft(polygon, 250);
        Canvas.SetTop(polygon, 200);
        MyInkCanvas.Children.Add(polygon);
    }
}

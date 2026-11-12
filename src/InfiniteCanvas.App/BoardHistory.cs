using System.Windows;
using System.Windows.Controls;
using System.Windows.Ink;
using System.Windows.Input;

namespace InfiniteCanvas.App;

/// <summary>
/// Historique Annuler / Rétablir du tableau.
/// Les ajouts/suppressions de traits sont captés automatiquement via <see cref="StrokeCollection.StrokesChanged"/>
/// (encre, formes, gomme, effacement total). Éléments (images), déplacements, redimensionnements et
/// changements de couleur sont enregistrés explicitement.
/// Un « groupe » (un geste souris/stylet) fusionne ses changements en une seule étape.
/// </summary>
public sealed class BoardHistory(InkCanvas canvas)
{
    private abstract class Op
    {
        public abstract void Undo(InkCanvas canvas);
        public abstract void Redo(InkCanvas canvas);
    }

    private sealed class StrokesOp(StrokeCollection added, StrokeCollection removed) : Op
    {
        public override void Undo(InkCanvas c)
        {
            foreach (var s in added) if (c.Strokes.Contains(s)) c.Strokes.Remove(s);
            foreach (var s in removed) if (!c.Strokes.Contains(s)) c.Strokes.Add(s);
        }

        public override void Redo(InkCanvas c)
        {
            foreach (var s in removed) if (c.Strokes.Contains(s)) c.Strokes.Remove(s);
            foreach (var s in added) if (!c.Strokes.Contains(s)) c.Strokes.Add(s);
        }
    }

    private sealed class ElementOp(UIElement element, bool added) : Op
    {
        public override void Undo(InkCanvas c) => Set(c, !added);
        public override void Redo(InkCanvas c) => Set(c, added);

        private void Set(InkCanvas c, bool present)
        {
            bool contains = c.Children.Contains(element);
            if (present && !contains) c.Children.Add(element);
            else if (!present && contains) c.Children.Remove(element);
        }
    }

    private sealed class PointsOp(Stroke stroke, StylusPointCollection before, StylusPointCollection after) : Op
    {
        public override void Undo(InkCanvas _) => stroke.StylusPoints = before.Clone();
        public override void Redo(InkCanvas _) => stroke.StylusPoints = after.Clone();
    }

    private sealed class BoundsOp(FrameworkElement element, Rect before, Rect after) : Op
    {
        public override void Undo(InkCanvas _) => Apply(element, before);
        public override void Redo(InkCanvas _) => Apply(element, after);
    }

    private sealed class AttributesOp(Stroke stroke, DrawingAttributes before, DrawingAttributes after) : Op
    {
        public override void Undo(InkCanvas _) => stroke.DrawingAttributes = before.Clone();
        public override void Redo(InkCanvas _) => stroke.DrawingAttributes = after.Clone();
    }

    private readonly Stack<List<Op>> _undo = new();
    private readonly Stack<List<Op>> _redo = new();
    private StrokeCollection? _strokes;
    private List<Op>? _openGroup;
    private bool _grouping;
    private bool _applying;

    /// <summary>Déclenché à chaque modification enregistrée, annulée ou rétablie.</summary>
    public event EventHandler? Changed;

    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;

    /// <summary>Position/taille d'un élément enfant de l'InkCanvas.</summary>
    public static Rect BoundsOf(FrameworkElement e) =>
        new(InkCanvas.GetLeft(e), InkCanvas.GetTop(e), e.Width, e.Height);

    public static void Apply(FrameworkElement e, Rect r)
    {
        InkCanvas.SetLeft(e, r.X);
        InkCanvas.SetTop(e, r.Y);
        e.Width = r.Width;
        e.Height = r.Height;
    }

    /// <summary>
    /// Suit la collection <see cref="InkCanvas.Strokes"/> actuelle sans toucher à l'historique.
    /// Chaque page a son propre historique : on l'attache quand la page devient active.
    /// </summary>
    public void Attach()
    {
        Detach();
        _strokes = canvas.Strokes;
        _strokes.StrokesChanged += OnStrokesChanged;
    }

    /// <summary>Cesse de suivre les traits (page mise en arrière-plan).</summary>
    public void Detach()
    {
        if (_strokes != null) _strokes.StrokesChanged -= OnStrokesChanged;
        _strokes = null;
        _openGroup = null;
        _grouping = false;
    }

    /// <summary>À appeler après tout remplacement de <see cref="InkCanvas.Strokes"/> : suit la collection et vide l'historique.</summary>
    public void Reset()
    {
        Attach();
        _undo.Clear();
        _redo.Clear();
    }

    public void BeginGroup()
    {
        _grouping = true;
        _openGroup = null;
    }

    public void EndGroup()
    {
        _grouping = false;
        _openGroup = null;
    }

    public void RecordElementAdded(UIElement e) => Push([new ElementOp(e, added: true)]);
    public void RecordElementRemoved(UIElement e) => Push([new ElementOp(e, added: false)]);

    public void RecordPoints(IEnumerable<(Stroke Stroke, StylusPointCollection Before, StylusPointCollection After)> changes) =>
        Push(changes.Select(c => (Op)new PointsOp(c.Stroke, c.Before, c.After)).ToList());

    public void RecordBounds(IEnumerable<(FrameworkElement Element, Rect Before, Rect After)> changes) =>
        Push(changes.Where(c => c.Before != c.After).Select(c => (Op)new BoundsOp(c.Element, c.Before, c.After)).ToList());

    public void RecordAttributes(IEnumerable<(Stroke Stroke, DrawingAttributes Before, DrawingAttributes After)> changes) =>
        Push(changes.Select(c => (Op)new AttributesOp(c.Stroke, c.Before, c.After)).ToList());

    public void Undo()
    {
        if (!CanUndo) return;
        EndGroup();
        var entry = _undo.Pop();
        Run(() => { for (int i = entry.Count - 1; i >= 0; i--) entry[i].Undo(canvas); });
        _redo.Push(entry);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Redo()
    {
        if (!CanRedo) return;
        EndGroup();
        var entry = _redo.Pop();
        Run(() => { foreach (var op in entry) op.Redo(canvas); });
        _undo.Push(entry);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void Run(Action action)
    {
        canvas.Select(new StrokeCollection()); // évite une sélection pointant sur des objets retirés
        _applying = true;
        try { action(); }
        finally { _applying = false; }
    }

    private void OnStrokesChanged(object? sender, StrokeCollectionChangedEventArgs e)
    {
        if (_applying) return;
        Push([new StrokesOp(new StrokeCollection(e.Added), new StrokeCollection(e.Removed))]);
    }

    private void Push(List<Op> ops)
    {
        if (ops.Count == 0) return;
        if (_grouping && _openGroup != null)
        {
            _openGroup.AddRange(ops); // déjà au sommet de la pile d'annulation
        }
        else
        {
            var entry = new List<Op>(ops);
            _undo.Push(entry);
            if (_grouping) _openGroup = entry;
        }
        _redo.Clear();
        Changed?.Invoke(this, EventArgs.Empty);
    }
}

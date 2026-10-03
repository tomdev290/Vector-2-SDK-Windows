using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Xml;
using System.Xml.Linq;
using Vector2LevelEditor.Services.ProjectManager;
using Vector2LevelEditor.Services;
using System.Windows.Threading;

namespace Vector2LevelEditor.Views.ProjectManager.Studios;

public sealed class TriggerXmlEditor : Grid
{
    private readonly TextBox _source;
    private readonly TextBox _lines = new() { IsReadOnly = true, IsTabStop = false, BorderThickness = new Thickness(0),
        FontFamily = new FontFamily("Consolas"), FontSize = 11, Padding = new Thickness(2), TextAlignment = TextAlignment.Right };
    private readonly TextBlock _issue = new() { TextWrapping = TextWrapping.Wrap, FontSize = 11, Margin = new Thickness(0, 5, 0, 5) };
    private readonly ListBox _choices = new() { MinWidth = 220, MaxHeight = 240 };
    private readonly Popup _popup;
    private readonly XmlCompletionSurface _completionSurface;
    private readonly XmlSyntaxSurface _syntax;
    private int _prefixStart;
    private int _errorLine;
    private bool _updating;
    private bool _closingCompletion;
    private readonly bool _requiresTriggerRoot;
    private readonly DispatcherTimer _reviewTimer = new() { Interval = TimeSpan.FromMilliseconds(350) };
    private readonly StackPanel _solutions = new();
    private CancellationTokenSource? _pendingReview;
    private XmlDraftReview? _lastReview;
    private Popup? _fixPopup;

    public TriggerXmlEditor(TextBox source, bool requiresTriggerRoot = true)
    {
        _requiresTriggerRoot = requiresTriggerRoot;
        _source = source;
        source.VerticalContentAlignment = VerticalAlignment.Top;
        _lines.VerticalContentAlignment = VerticalAlignment.Top;
        _lines.Padding = new Thickness(2, source.Padding.Top, 2, source.Padding.Bottom);
        RowDefinitions.Add(new RowDefinition());
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var code = new Grid();
        code.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(38) });
        code.ColumnDefinitions.Add(new ColumnDefinition());
        _lines.Background = StudioUi.Resource("ProjectManagerHoverBrush");
        _lines.Foreground = StudioUi.Resource("ProjectManagerMutedBrush");
        code.Children.Add(_lines);
        SetColumn(source, 1);
        code.Children.Add(source);
        var syntax = _syntax = new XmlSyntaxSurface(source, () => _errorLine);
        SetColumn(syntax, 1);
        code.Children.Add(syntax);
        source.CaretBrush = StudioUi.Resource("ProjectManagerMutedBrush");
        source.Foreground = StudioUi.Resource("ProjectManagerMutedBrush");
        source.TextChanged += (_, _) => source.Dispatcher.BeginInvoke(DispatcherPriority.Render, new Action(syntax.InvalidateVisual));
        source.SelectionChanged += (_, _) => syntax.InvalidateVisual();
        source.AddHandler(ScrollViewer.ScrollChangedEvent, new ScrollChangedEventHandler((_, _) => syntax.InvalidateVisual()));
        Children.Add(code);
        var reviewArea = new StackPanel();
        var checks = new Button { Content = "Checks", HorizontalAlignment = HorizontalAlignment.Right,
            Padding = new Thickness(8, 3, 8, 3), ToolTip = "Validate XML syntax and known trigger runtime fields" };
        checks.Click += (_, _) => { _reviewTimer.Stop(); Review(); };
        reviewArea.Children.Add(checks);
        reviewArea.Children.Add(_issue);
        reviewArea.Children.Add(_solutions);
        SetRow(reviewArea, 1);
        Children.Add(reviewArea);
        _completionSurface = new XmlCompletionSurface(_choices);
        _popup = new Popup { PlacementTarget = source, Placement = PlacementMode.Relative, StaysOpen = true,
            AllowsTransparency = true, Child = new Border { Padding = new Thickness(12), Child = _completionSurface } };
        Action? refreshGlass = null;
        _popup.Opened += (_, _) =>
        {
            refreshGlass ??= XmlFrostedSurface.Apply(_completionSurface, source, 0, 0, () => new Point(_popup.HorizontalOffset, _popup.VerticalOffset));
            refreshGlass();
            _completionSurface.Open();
        };
        _popup.Closed += (_, _) => _completionSurface.Close();
        source.SelectionChanged += (_, _) => { if (_popup.IsOpen) source.Dispatcher.BeginInvoke(new Action(() => refreshGlass?.Invoke())); };
        _reviewTimer.Tick += (_, _) => { _reviewTimer.Stop(); Review(); };
        source.TextChanged += (_, _) =>
        {
            _pendingReview?.Cancel();
            _lines.Text = string.Join("\n", Enumerable.Range(1, source.Text.Count(ch => ch == '\n') + 1));
            _reviewTimer.Stop(); _reviewTimer.Start(); UpdateCompletions(false);
        };
        source.SelectionChanged += (_, _) =>
        {
            if (_updating) return;
            if (source.SelectionLength > 0) _popup.IsOpen = false;
            else UpdateCompletions(false);
        };
        source.PreviewKeyDown += HandleKey;
        source.AddHandler(ScrollViewer.ScrollChangedEvent, new ScrollChangedEventHandler((_, args) => _lines.ScrollToVerticalOffset(args.VerticalOffset)));
        source.LostKeyboardFocus += (_, _) => { if (!_choices.IsKeyboardFocusWithin) _popup.IsOpen = false; };
        _choices.PreviewMouseLeftButtonUp += (_, _) => Accept();
        _issue.MouseLeftButtonDown += (_, _) =>
        {
            if (_lastReview is { } current && current.Source == _source.Text && current.Solutions.Count + current.Intents.Count > 0)
                OpenFixMenu(current, _issue);
            if (_errorLine <= 0) return;
            var line = Math.Min(_errorLine - 1, Math.Max(0, _source.LineCount - 1));
            _source.Focus();
            _source.CaretIndex = _source.GetCharacterIndexFromLineIndex(line);
            _source.ScrollToLine(line);
        };
        Unloaded += (_, _) => { _popup.IsOpen = false; if (_fixPopup is not null) _fixPopup.IsOpen = false; _reviewTimer.Stop(); _pendingReview?.Cancel(); };
        Review();
    }

    private void Review()
    {
        var count = _source.Text.Count(ch => ch == '\n') + 1;
        _lines.Text = string.Join("\n", Enumerable.Range(1, count));
        _errorLine = 0;
        _syntax.InvalidateVisual();
        _solutions.Children.Clear();
        if (string.IsNullOrWhiteSpace(_source.Text)) { _issue.Text = ""; return; }
        try
        {
            if (XmlAssistantLexing.IsEmptyDraft(_source.Text)) { _issue.Text = ""; return; }
            var document = XmlDraftParsing.Parse(_source.Text, LoadOptions.SetLineInfo);
            var detailed = _source.Text.Length <= 200_000;
            var diagnostics = detailed && document.Root?.Name.LocalName == "Trigger" ? TriggerRuntimeSchema.Diagnose(document.Root) : [];
            _errorLine = diagnostics.FirstOrDefault(item => item.IsError)?.Line ?? 0;
            _syntax.InvalidateVisual();
            _issue.Text = diagnostics.Count == 0 ? "XML syntax checks passed." : string.Join("\n", diagnostics.Select(item => (item.Line > 0 ? $"Line {item.Line}: " : "") + item.Message));
            if (!detailed) _issue.Text = "XML structure checks passed. Game checks and repairs were skipped: the draft exceeds 200,000 characters.";
            if (_requiresTriggerRoot && document.Root?.Name.LocalName != "Trigger") _issue.Text = "The XML root must be Trigger.";
            _issue.Foreground = diagnostics.Any(item => item.IsError) ? Brushes.Firebrick : StudioUi.Resource("ProjectManagerMutedBrush");
            QueueSolutions();
        }
        catch (XmlException ex)
        {
            _errorLine = ex.LineNumber;
            _syntax.InvalidateVisual();
            _issue.Text = $"Line {ex.LineNumber}, column {ex.LinePosition}: {ex.Message}";
            _issue.Foreground = Brushes.Firebrick;
            _issue.ToolTip = "Select this issue to move to the affected line. Check matching quotes and closing tags.";
            QueueSolutions();
        }
    }

    private void RenderSolutions(XmlDraftReview review)
    {
        _lastReview = review; _solutions.Children.Clear();
        var count = review.Solutions.Count + review.Intents.Count;
        if (count == 0) return;
        var preview = new Button { Content = $"View suggested fixes ({count})",
            HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 3, 0, 0) };
        preview.Click += (_, _) => OpenFixMenu(review, preview);
        _solutions.Children.Add(preview);
    }

    private async void QueueSolutions()
    {
        _pendingReview?.Cancel();
        var pending = _pendingReview = new CancellationTokenSource();
        var source = _source.Text;
        try
        {
            var review = await Task.Run(() => XmlDraftReviewer.Review(source), pending.Token).ConfigureAwait(false);
            await _source.Dispatcher.InvokeAsync(() =>
            {
                if (!pending.IsCancellationRequested && ReferenceEquals(_pendingReview, pending) && _source.Text == source)
                    RenderSolutions(review);
            });
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            if (!_source.Dispatcher.HasShutdownStarted)
                await _source.Dispatcher.InvokeAsync(() => { if (!pending.IsCancellationRequested) _issue.Text += "\nAssistant check failed: " + ex.Message; });
        }
        finally
        {
            if (!_source.Dispatcher.HasShutdownStarted)
                await _source.Dispatcher.InvokeAsync(() => { if (ReferenceEquals(_pendingReview, pending)) _pendingReview = null; pending.Dispose(); });
            else pending.Dispose();
        }
    }

    private void OpenFixMenu(XmlDraftReview review, FrameworkElement target)
    {
        var menu = new ContextMenu { PlacementTarget = target };
        foreach (var solution in review.Solutions)
        {
            var choice = new MenuItem { Header = solution.Title + (solution.Recommended ? "  - Recommended" : ""), ToolTip = solution.Explanation };
            choice.Click += (_, _) => ShowSolution(review, solution);
            menu.Items.Add(choice);
        }
        foreach (var intent in review.Intents)
        {
            var choice = new MenuItem { Header = "Complete " + intent.Tag + " fields..." };
            choice.Click += (_, _) => ShowIntent(review, intent);
            menu.Items.Add(choice);
        }
        menu.IsOpen = true;
    }

    private void ShowIntent(XmlDraftReview review, XmlDraftIntent intent)
    {
        if (_source.Text != review.Source) { Review(); return; }
        if (_fixPopup is not null) _fixPopup.IsOpen = false;
        var content = new StackPanel { Margin = new Thickness(14) };
        content.Children.Add(new TextBlock { Text = "XML Assistant  /  " + intent.Tag, FontWeight = FontWeights.SemiBold });
        var fields = new Dictionary<string, TextBox>();
        foreach (var key in intent.MissingFields)
        {
            content.Children.Add(StudioUi.Label(key));
            var field = StudioUi.Field(); fields[key] = field; content.Children.Add(field);
        }
        var error = new TextBlock { Foreground = Brushes.Firebrick, TextWrapping = TextWrapping.Wrap };
        content.Children.Add(error);
        var preview = StudioUi.Button("Preview Changes", (_, _) =>
        {
            if (_source.Text != review.Source) { error.Text = "The draft changed. Recheck before choosing values."; return; }
            try
            {
                var solution = XmlDraftReviewer.CompleteIntent(review.Source, intent, fields.ToDictionary(pair => pair.Key, pair => pair.Value.Text));
                ShowSolution(review, solution);
            }
            catch (Exception ex) { error.Text = ex.Message; }
        }, true);
        content.Children.Add(preview);
        var fill = StudioUi.Resource("ProjectManagerCardBrush") is SolidColorBrush solid ? solid.Color : Colors.White;
        fill.A = 232;
        var frame = new Border { Width = Math.Min(400, SystemParameters.WorkArea.Width - 32), CornerRadius = new CornerRadius(12),
            BorderBrush = StudioUi.Resource("ProjectManagerEdgeBrush"), BorderThickness = new Thickness(1),
            Background = new SolidColorBrush(fill), Child = content,
            Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 16, ShadowDepth = 3, Opacity = .2 } };
        var caret = _source.GetRectFromCharacterIndex(_source.CaretIndex);
        XmlFrostedSurface.Apply(frame, _source, caret.IsEmpty ? 0 : caret.X, caret.IsEmpty ? 0 : caret.Bottom);
        _fixPopup = new Popup { PlacementTarget = _source, Placement = PlacementMode.Relative, AllowsTransparency = true,
            StaysOpen = false, HorizontalOffset = caret.IsEmpty ? 0 : caret.X, VerticalOffset = caret.IsEmpty ? 0 : caret.Bottom,
            Child = new Border { Padding = new Thickness(12), Child = frame }, IsOpen = true };
        frame.PreviewKeyDown += (_, args) => { if (args.Key == Key.Escape) { _fixPopup.IsOpen = false; args.Handled = true; } };
    }

    private void ShowSolution(XmlDraftReview review, XmlDraftSolution solution)
    {
        if (_fixPopup is not null) _fixPopup.IsOpen = false;
        var panel = new DockPanel { Margin = new Thickness(12) };
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        DockPanel.SetDock(actions, Dock.Bottom);
        panel.Children.Add(actions);
        var text = new TextBlock { FontFamily = new FontFamily("Consolas"), FontSize = 12 };
        var dark = StudioUi.Resource("WindowTextBrush") is SolidColorBrush foreground && foreground.Color.R + foreground.Color.G + foreground.Color.B > 500;
        foreach (var line in XmlSolutionDiff.Lines(review.Source, solution.Code))
        {
            var run = new System.Windows.Documents.Run((line.Kind == XmlDiffKind.Removed ? "- " : line.Kind == XmlDiffKind.Added ? "+ " : "  ") + line.Text + "\n");
            run.Foreground = line.Kind == XmlDiffKind.Removed ? dark ? Brushes.LightPink : Brushes.Firebrick
                : line.Kind == XmlDiffKind.Added ? dark ? Brushes.LightGreen : Brushes.ForestGreen : StudioUi.Resource("WindowTextBrush");
            if (line.Kind == XmlDiffKind.Removed) run.TextDecorations = TextDecorations.Strikethrough;
            text.Inlines.Add(run);
        }
        var scroll = new ScrollViewer { Content = text, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto };
        panel.Children.Add(scroll);
        var heading = new TextBlock { Text = "XML Assistant  /  " + solution.Title + (solution.Recommended ? "  - Recommended" : ""), FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 10), TextWrapping = TextWrapping.Wrap };
        DockPanel.SetDock(heading, Dock.Top); panel.Children.Insert(1, heading);
        var checkSummary = solution.RemainingIssues is { } remaining ? $"Remaining catalogue errors: {remaining}." : "Game catalogue checks are incomplete for this fragment.";
        var explanation = new TextBlock { Text = solution.Explanation + "\n" + checkSummary, TextWrapping = TextWrapping.Wrap,
            FontSize = 11, Foreground = StudioUi.Resource("ProjectManagerMutedBrush"), Margin = new Thickness(0, 0, 0, 8) };
        DockPanel.SetDock(explanation, Dock.Top); panel.Children.Insert(2, explanation);
        var edge = new LinearGradientBrush();
        edge.GradientStops.Add(new GradientStop(Colors.Cyan, 0));
        edge.GradientStops.Add(new GradientStop(Colors.RoyalBlue, .25));
        edge.GradientStops.Add(new GradientStop(Colors.MediumPurple, .5));
        edge.GradientStops.Add(new GradientStop(Colors.HotPink, .75));
        edge.GradientStops.Add(new GradientStop(Colors.Orange, 1));
        var rotation = new RotateTransform(0, .5, .5); edge.RelativeTransform = rotation;
        var fill = StudioUi.Resource("ProjectManagerCardBrush") is SolidColorBrush solid ? solid.Color : Colors.White;
        fill.A = 232;
        var frame = new Border { BorderBrush = solution.Recommended ? edge : StudioUi.Resource("ProjectManagerEdgeBrush"), BorderThickness = new Thickness(2), CornerRadius = new CornerRadius(12),
            Background = new SolidColorBrush(fill), Child = panel,
            Width = Math.Min(540, SystemParameters.WorkArea.Width - 32), Height = Math.Min(380, SystemParameters.WorkArea.Height - 32),
            Effect = new System.Windows.Media.Effects.DropShadowEffect { Color = Colors.MediumPurple, BlurRadius = 16, ShadowDepth = 0, Opacity = .35 } };
        var anchor = _source.GetRectFromCharacterIndex(_source.CaretIndex);
        var popup = new Popup { PlacementTarget = _source, Placement = PlacementMode.Relative,
            HorizontalOffset = anchor.IsEmpty ? 0 : Math.Max(0, anchor.X - frame.Width / 2),
            VerticalOffset = anchor.IsEmpty ? 0 : anchor.Bottom,
            AllowsTransparency = true, StaysOpen = false, Child = new Border { Padding = new Thickness(12), Child = frame } };
        _fixPopup = popup;
        frame.PreviewKeyDown += (_, args) => { if (args.Key == Key.Escape) { popup.IsOpen = false; args.Handled = true; } };
        XmlFrostedSurface.Apply(frame, _source, popup.HorizontalOffset, popup.VerticalOffset);
        if (solution.Recommended && SystemParameters.ClientAreaAnimation)
            rotation.BeginAnimation(RotateTransform.AngleProperty, new System.Windows.Media.Animation.DoubleAnimation(0, 360, TimeSpan.FromSeconds(8))
                { RepeatBehavior = System.Windows.Media.Animation.RepeatBehavior.Forever });
        popup.Closed += (_, _) => rotation.BeginAnimation(RotateTransform.AngleProperty, null);
        var close = new Button { Content = "Cancel", Padding = new Thickness(12, 5, 12, 5), Margin = new Thickness(6) };
        close.Click += (_, _) => popup.IsOpen = false;
        actions.Children.Add(close);
        var apply = new Button { Content = "Apply Fix", Padding = new Thickness(12, 5, 12, 5), Margin = new Thickness(6) };
        actions.Children.Add(apply);
        apply.Click += (_, _) =>
        {
            if (_source.Text != review.Source)
            {
                popup.IsOpen = false; Review();
                _issue.Text = "The draft changed; the stale fix was rejected.\n" + _issue.Text;
                return;
            }
            _source.BeginChange();
            try { _source.SelectAll(); _source.SelectedText = solution.Code; }
            finally { _source.EndChange(); }
            popup.IsOpen = false; _source.Focus(); Review();
        };
        var firstChange = XmlSolutionDiff.Lines(review.Source, solution.Code).TakeWhile(line => line.Kind == XmlDiffKind.Unchanged).Count();
        scroll.Loaded += (_, _) => scroll.ScrollToVerticalOffset(firstChange * 16);
        popup.IsOpen = true;
    }

    private void HandleKey(object sender, KeyEventArgs args)
    {
        if (args.Key == Key.Escape && _fixPopup?.IsOpen == true) { _fixPopup.IsOpen = false; args.Handled = true; return; }
        if (args.Key == Key.Space && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        { UpdateCompletions(true); args.Handled = true; return; }
        if (!_popup.IsOpen) return;
        if (args.Key is Key.Up or Key.Down)
        {
            _choices.SelectedIndex = Math.Clamp(_choices.SelectedIndex + (args.Key == Key.Up ? -1 : 1), 0, _choices.Items.Count - 1);
            _choices.ScrollIntoView(_choices.SelectedItem); args.Handled = true;
        }
        else if (args.Key is Key.Tab or Key.Enter) { Accept(); args.Handled = true; }
        else if (args.Key == Key.Escape) { _popup.IsOpen = false; args.Handled = true; }
    }

    private void UpdateCompletions(bool forced)
    {
        if (!forced && !XmlAuthoringPreferences.PredictionEnabled) { _popup.IsOpen = false; return; }
        if (_updating || !_source.IsKeyboardFocusWithin) return;
        var caret = Math.Clamp(_source.CaretIndex, 0, _source.Text.Length);
        var before = _source.Text[..caret];
        var token = XmlAssistantLexing.NameAtCaret(_source.Text, caret);
        if (token is null)
        {
            var attribute = XmlAttributeCompletion.AtCaret(_source.Text, caret);
            if (attribute is null) { _popup.IsOpen = false; return; }
            var existing = _source.Text.Substring(attribute.Start, attribute.Length);
            var choices = attribute.Choices.Where(name => name.StartsWith(attribute.Prefix, StringComparison.OrdinalIgnoreCase) && name != existing).Take(10).ToList();
            if (choices.Count == 0) { _popup.IsOpen = false; return; }
            _prefixStart = -1;
            _choices.ItemsSource = choices; _choices.SelectedIndex = 0;
            _completionSurface.Update(choices.Count, null);
            var position = _source.GetRectFromCharacterIndex(caret);
            _popup.HorizontalOffset = double.IsFinite(position.X) ? position.X : 0;
            _popup.VerticalOffset = double.IsFinite(position.Bottom) ? position.Bottom : 0;
            _popup.IsOpen = forced || attribute.Prefix.Length > 0;
            return;
        }
        var start = token.Value.MarkupStart;
        var prefix = _source.Text[token.Value.Start..caret];
        _closingCompletion = token.Value.Closing;
        var stack = new Stack<string>();
        // XmlReader gives the parent context even when the draft ends mid-tag.
        try
        {
            using var reader = XmlReader.Create(new StringReader(before[..start]), new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit });
            while (reader.Read())
            {
                if (reader.NodeType == XmlNodeType.Element && !reader.IsEmptyElement) stack.Push(reader.Name);
                else if (reader.NodeType == XmlNodeType.EndElement && stack.Count > 0) stack.Pop();
            }
        }
        catch (XmlException) { }
        var parent = stack.FirstOrDefault() ?? "";
        IEnumerable<string> names = parent switch
        {
            "Events" => TriggerRuntimeSchema.Events.Select(item => item.Name),
            "Conditions" => TriggerRuntimeSchema.Conditions.Select(item => item.Name),
            "Actions" => TriggerRuntimeSchema.Actions.Select(item => item.Name),
            "Trigger" => ["Content"], "Content" => _requiresTriggerRoot ? ["Init", "Loop", "Template"]
                : ["Object", "ObjectReference", "Image", "Platform", "Trapezoid", "Trigger", "Area", "In", "Out", "Spawn", "Waypoint", "Init", "Loop", "Template"],
            "Root" => ["Track"], "Track" or "Object" => ["Properties", "Content"],
            "Properties" => ["Static", "Dynamic"], "Static" => ["Enable", "Selection", "Next"],
            "Loop" => ["Events", "Conditions", "Actions"], "Init" => ["SetVariable"],
            "" => _requiresTriggerRoot ? ["Trigger"] : ["Root", "Track", "Scene"], _ => []
        };
        if (_closingCompletion) names = stack.Take(1);
        var existingName = _source.Text.Substring(token.Value.Start, token.Value.Length);
        var matches = names.Where(name => name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            && !name.Equals(existingName, StringComparison.Ordinal)).Take(10).ToList();
        if (matches.Count == 0) { _popup.IsOpen = false; return; }
        _prefixStart = start;
        _choices.ItemsSource = matches; _choices.SelectedIndex = 0;
        _completionSurface.Update(matches.Count, matches.Count == 1 ? matches[0] :
            matches.FirstOrDefault(name => name.Equals(prefix, StringComparison.OrdinalIgnoreCase)));
        var rect = _source.GetRectFromCharacterIndex(caret);
        _popup.HorizontalOffset = double.IsFinite(rect.X) ? rect.X : 0;
        _popup.VerticalOffset = double.IsFinite(rect.Bottom) ? rect.Bottom : 0;
        _popup.IsOpen = forced || prefix.Length > 0 || before.EndsWith('<');
    }

    private void Accept()
    {
        if (_choices.SelectedItem is not string name) return;
        var caret = _source.CaretIndex;
        var token = XmlAssistantLexing.NameAtCaret(_source.Text, caret);
        var attribute = _prefixStart == -1 ? XmlAttributeCompletion.AtCaret(_source.Text, caret) : null;
        if (attribute is null && (token is null || token.Value.MarkupStart != _prefixStart)) { _popup.IsOpen = false; return; }
        if (attribute is not null && !attribute.Choices.Contains(name)) { _popup.IsOpen = false; return; }
        var replacementStart = attribute?.Start ?? token!.Value.Start;
        var replacementLength = attribute?.Length ?? token!.Value.Length;
        _updating = true;
        _source.BeginChange();
        try
        {
            _source.Select(replacementStart, replacementLength);
            _source.SelectedText = name;
            _source.CaretIndex = replacementStart + name.Length;
            _source.Focus();
        }
        finally { _source.EndChange(); _updating = false; _popup.IsOpen = false; }
    }

    private sealed class XmlSyntaxSurface : FrameworkElement
    {
        private readonly TextBox _editor;
        private readonly Func<int> _errorLine;
        private static readonly System.Text.RegularExpressions.Regex Tokens = new(
            "</?[A-Za-z_][\\w:.-]*|\\\"[^\\\"]*\\\"|'[^']*'|[A-Za-z_][\\w:.-]*(?=\\s*=)",
            System.Text.RegularExpressions.RegexOptions.Compiled, TimeSpan.FromMilliseconds(100));
        public XmlSyntaxSurface(TextBox editor, Func<int> errorLine) { _editor = editor; _errorLine = errorLine; IsHitTestVisible = false; ClipToBounds = true; }
        protected override void OnRender(DrawingContext dc)
        {
            if (_editor.SelectionLength > 0) return;
            var first = _editor.GetFirstVisibleLineIndex();
            var last = _editor.GetLastVisibleLineIndex();
            if (first < 0 || last < first) return;
            var typeface = new Typeface(_editor.FontFamily, _editor.FontStyle, _editor.FontWeight, _editor.FontStretch);
            var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
            void Draw(string text, int index, Brush brush)
            {
                if (text.Length == 0) return;
                var position = _editor.GetRectFromCharacterIndex(index);
                if (position.IsEmpty || !double.IsFinite(position.X)) return;
                dc.DrawText(new FormattedText(text, System.Globalization.CultureInfo.CurrentCulture,
                    FlowDirection.LeftToRight, typeface, _editor.FontSize, brush, dpi), position.TopLeft);
            }
            for (var line = first; line <= last; line++)
            {
                var text = _editor.GetLineText(line).TrimEnd('\r', '\n');
                var start = _editor.GetCharacterIndexFromLineIndex(line);
                if (line + 1 == _errorLine())
                {
                    var rect = _editor.GetRectFromCharacterIndex(start);
                    if (!rect.IsEmpty)
                    {
                        dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(22, 200, 40, 55)), null, new Rect(0, rect.Top, ActualWidth, rect.Height));
                        dc.DrawRectangle(Brushes.Firebrick, null, new Rect(0, rect.Top, 2, rect.Height));
                    }
                }
                var offset = 0;
                foreach (System.Text.RegularExpressions.Match token in Tokens.Matches(text))
                {
                    Draw(text[offset..token.Index], start + offset, StudioUi.Resource("ProjectManagerMutedBrush"));
                    var brush = token.Value.StartsWith('<') ? Brushes.RoyalBlue
                        : token.Value.StartsWith('"') || token.Value.StartsWith('\'') ? Brushes.DodgerBlue : Brushes.DarkCyan;
                    Draw(token.Value, start + token.Index, brush);
                    offset = token.Index + token.Length;
                }
                Draw(text[offset..], start + offset, StudioUi.Resource("ProjectManagerMutedBrush"));
            }
        }
    }
}

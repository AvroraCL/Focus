import io

def patch(path, pairs, required=True):
    with io.open(path, encoding='utf-8') as f:
        s = f.read()
    for old, new in pairs:
        if old not in s:
            if required: print('MISS', path, '|', old[:60].replace('\n',' '))
            continue
        s = s.replace(old, new, 1)
    with io.open(path, 'w', encoding='utf-8') as f:
        f.write(s)

# --- DesignCanvas: public hit test for context menus
patch('src/Focus.App/Controls/DesignCanvas.cs', [
('''    public void SetSpaceDown(bool down) => _spaceDown = down;''',
'''    public void SetSpaceDown(bool down) => _spaceDown = down;

    /// <summary>Element under a point in this control's coordinates, if any.</summary>
    public HudElement? HitTest(Point screenPoint) => HitElement(ScreenToCanvas(screenPoint));'''),
])

# --- EditorPage: lightweight row refresh + right-click menu
patch('src/Focus.App/Views/EditorPage.xaml.cs', [
('''        Canvas.BeginMutation += PushUndo;
        Canvas.Changed += OnCanvasChanged;
        Canvas.SelectionChanged += OnCanvasSelectionChanged;''',
'''        Canvas.BeginMutation += PushUndo;
        Canvas.Changed += OnCanvasChanged;
        Canvas.SelectionChanged += OnCanvasSelectionChanged;
        Canvas.PreviewMouseRightButtonDown += OnCanvasRightClick;'''),
('''    private void RebuildLayerList()
    {
        _loading = true;
        var rows = _profile.Elements
            .OrderByDescending(e => e.ZIndex)
            .Select(e => new ElementRow(e))
            .ToList();
        ElementList.ItemsSource = rows;
        var match = rows.FirstOrDefault(r => _selection.Contains(r.Element.Id));
        ElementList.SelectedItem = match;
        ElementCountText.Text = _profile.Elements.Count.ToString();
        _loading = false;
    }''',
'''    private void RebuildLayerList()
    {
        _loading = true;
        _rows = _profile.Elements
            .OrderByDescending(e => e.ZIndex)
            .Select(e => new ElementRow(e))
            .ToList();
        ElementList.ItemsSource = _rows;
        var match = _rows.FirstOrDefault(r => _selection.Contains(r.Element.Id));
        ElementList.SelectedItem = match;
        ElementCountText.Text = _profile.Elements.Count.ToString();
        _loading = false;
    }

    private void RefreshLayerRows()
    {
        foreach (var row in _rows) row.Refresh();
    }'''),
('''    private void OnPropertyChanged()
    {
        _profile.Normalize();
        Canvas.InvalidateVisual();
        Canvas.RefreshSelection();
        RebuildLayerList();
        UpdateStatus();
        MarkDirty();
        SchedulePreview();
    }''',
'''    private void OnPropertyChanged()
    {
        _profile.Normalize();
        Canvas.InvalidateVisual();
        Canvas.RefreshSelection();
        RefreshLayerRows();
        UpdateStatus();
        MarkDirty();
        SchedulePreview();
    }'''),
('''    private bool _switchingProfile;''',
'''    private bool _switchingProfile;
    private List<ElementRow> _rows = new();

    private void OnCanvasRightClick(object sender, MouseButtonEventArgs e)
    {
        var element = Canvas.HitTest(e.GetPosition(Canvas));
        if (element is null) return;

        if (!_selection.Contains(element.Id))
        {
            _selection.Clear();
            _selection.Add(element.Id);
            Canvas.SetSelection(new[] { element.Id });
            BuildProperties();
            UpdateStatus();
            SyncLayerSelection();
        }

        var menu = new ContextMenu { PlacementTarget = Canvas };
        menu.Items.Add(MenuItem(Loc.Inst["common.duplicate"], () => DuplicateSelection()));
        menu.Items.Add(MenuItem(Loc.Inst["common.delete"], () => DeleteSelection()));
        menu.Items.Add(new Separator());
        menu.Items.Add(MenuItem(Loc.Inst["editor.group"], () => GroupSelection()));
        menu.Items.Add(MenuItem(Loc.Inst["editor.ungroup"], () => UngroupSelection()));
        menu.Items.Add(new Separator());
        menu.Items.Add(MenuItem(Loc.Inst["editor.bringForward"], () => ChangeZOrder(1)));
        menu.Items.Add(MenuItem(Loc.Inst["editor.sendBackward"], () => ChangeZOrder(-1)));
        menu.IsOpen = true;
        e.Handled = true;
    }

    private MenuItem MenuItem(string header, Action action)
    {
        var item = new MenuItem { Header = header };
        item.Click += (_, _) => action();
        return item;
    }'''),
])

# ElementRow: notify changes so the layer list does not need a full rebuild while typing
patch('src/Focus.App/Views/EditorPage.xaml.cs', [
('''public sealed class ElementRow
{
    public ElementRow(HudElement element) => Element = element;

    public HudElement Element { get; }

    public string Name => Element.Name;''',
'''public sealed class ElementRow : System.ComponentModel.INotifyPropertyChanged
{
    public ElementRow(HudElement element) => Element = element;

    public HudElement Element { get; }

    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;

    public void Refresh()
    {
        PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(string.Empty));
    }

    public string Name => Element.Name;'''),
])

# --- Sessions: localize remaining labels
patch('src/Focus.App/Views/SessionsPage.xaml', [
('''                                <TextBlock Style="{StaticResource StatLabel}" Text="Median" />''',
 '''                                <TextBlock Style="{StaticResource StatLabel}"
                                           Text="{Binding [sessions.median], Source={x:Static loc:Loc.Inst}}" />'''),
('''                                <TextBlock Style="{StaticResource StatLabel}" Text="Max" />''',
 '''                                <TextBlock Style="{StaticResource StatLabel}"
                                           Text="{Binding [sessions.max], Source={x:Static loc:Loc.Inst}}" />'''),
('''                                <TextBlock Text="Stutter" Style="{StaticResource Text.Caption}" Margin="0,0,6,0"''',
 '''                                <TextBlock Text="{Binding [sessions.stutter], Source={x:Static loc:Loc.Inst}}"
                                           Style="{StaticResource Text.Caption}" Margin="0,0,6,0"'''),
])

patch('src/Focus.App/Localization/Strings.cs', [
('        ["sessions.allSessions"] = "全部会话",',
 '        ["sessions.allSessions"] = "全部会话",\n        ["sessions.median"] = "中位帧时间",\n        ["sessions.max"] = "最大帧时间",'),
('        ["sessions.allSessions"] = "All sessions",',
 '        ["sessions.allSessions"] = "All sessions",\n        ["sessions.median"] = "Median frame",\n        ["sessions.max"] = "Max frame",'),
])
print('batch 2 done')

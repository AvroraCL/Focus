using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Focus.App.Controls;
using Focus.App.Localization;
using Focus.App.Services;
using Focus.Core.Metrics;
using Focus.Core.Profiles;

namespace Focus.App.Views;

public partial class EditorPage : UserControl
{
    private readonly AppServices _services;
    private readonly List<string> _selection = new();
    private readonly Stack<string> _undo = new();
    private readonly Stack<string> _redo = new();
    private readonly DispatcherTimer _autosave;
    private readonly DispatcherTimer _previewPump;
    private readonly DispatcherTimer _liveRefresh;
    private HudProfile _profile = new();
    private bool _loading;
    private bool _dirty;
    private string _clipboard = string.Empty;

    public EditorPage(AppServices services)
    {
        _services = services;
        InitializeComponent();

        Canvas.BeginMutation += PushUndo;
        Canvas.Changed += OnCanvasChanged;
        Canvas.SelectionChanged += OnCanvasSelectionChanged;
        Canvas.PreviewMouseRightButtonDown += OnCanvasRightClick;
        Properties.ValueChanged += OnPropertyChanged;

        _autosave = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(1400) };
        _autosave.Tick += (_, _) =>
        {
            _autosave.Stop();
            if (_dirty) SaveInternal(silent: true);
        };

        _previewPump = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _previewPump.Tick += (_, _) =>
        {
            _previewPump.Stop();
            if (Canvas.IsInteracting) { _previewPump.Start(); return; }
            _services.Overlay.SetProfile(_profile);
        };

        _liveRefresh = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(250) };
        _liveRefresh.Tick += (_, _) =>
        {
            if (_profile.Elements.Any(e => e.Kind == HudElementKind.Graph)) Canvas.InvalidateVisual();
        };
        _liveRefresh.Start();

        PreviewKeyDown += OnPreviewKeyDown;
        PreviewKeyUp += OnPreviewKeyUp;

        _services.ActiveProfileChanged += OnExternalProfileChanged;
        Loc.Inst.LanguageChanged += OnLanguageChanged;
        Loaded += (_, _) =>
        {
            _liveRefresh.Start();
            LoadProfileChoices();
            var target = _services.ActiveProfile;
            if (_services.Profiles.IsPreset(target.Id))
            {
                target = _services.Profiles.SaveNewFrom(target, Loc.Inst["preset." + target.Id]);
                LoadProfileChoices();
            }
            LoadProfile(target);
        };
        Unloaded += (_, _) =>
        {
            if (_dirty) SaveInternal(silent: true);
            _autosave.Stop();
            _previewPump.Stop();
            _liveRefresh.Stop();
        };
    }

    // ------------------------------------------------------------- profiles

    private sealed record ProfileChoice(string Id, string Display)
    {
        public override string ToString() => Display;
    }

    private void LoadProfileChoices()
    {
        _loading = true;
        var items = new List<ProfileChoice>();
        if (_services.Profiles.Custom.Count > 0)
        {
            foreach (var profile in _services.Profiles.Custom)
            {
                items.Add(new ProfileChoice(profile.Id, profile.Name));
            }
        }
        foreach (var preset in _services.Profiles.Presets)
        {
            items.Add(new ProfileChoice(preset.Id, Loc.Inst["preset." + preset.Id] + " ·"));
        }
        ProfileCombo.ItemsSource = items;
        ProfileCombo.SelectedIndex = 0;
        _loading = false;
    }

    private void LoadProfile(HudProfile profile)
    {
        _profile = profile.Clone();
        _profile.Normalize();
        _selection.Clear();
        _undo.Clear();
        _redo.Clear();
        _dirty = false;

        Canvas.Profile = _profile;
        Canvas.Data = _services.HudData;
        Canvas.SetSelection(Array.Empty<string>(), notify: false);
        Canvas.InvalidateVisual();
        Canvas.ZoomToFit();

        RebuildLayerList();
        BuildProperties();
        UpdateStatus();
        UpdateSaveState();
        _services.Overlay.SetProfile(_profile);
        SelectComboItem(_profile.Id);
    }

    private void SelectComboItem(string id)
    {
        if (ProfileCombo.ItemsSource is not IEnumerable<ProfileChoice> choices) return;
        _loading = true;
        var match = choices.FirstOrDefault(c => c.Id == id);
        if (match is not null) ProfileCombo.SelectedItem = match;
        _loading = false;
    }

    private void OnProfileComboChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        if (ProfileCombo.SelectedItem is not ProfileChoice choice) return;

        if (_dirty) SaveInternal(silent: true);

        _switchingProfile = true;
        try
        {
            if (_services.Profiles.IsPreset(choice.Id))
            {
                var preset = _services.Profiles.Get(choice.Id);
                if (preset is null) return;
                var copy = _services.Profiles.SaveNewFrom(preset, Loc.Inst["preset." + preset.Id]);
                _services.SetActiveProfile(copy);
                LoadProfileChoices();
                LoadProfile(copy);
                return;
            }

            var profile = _services.Profiles.Get(choice.Id);
            if (profile is null) return;
            _services.SetActiveProfile(profile);
            LoadProfile(profile);
        }
        finally
        {
            _switchingProfile = false;
        }
    }

    private bool _switchingProfile;
    private List<ElementRow> _rows = new();

    private void OnCanvasRightClick(object sender, MouseButtonEventArgs e)
    {
        var element = Canvas.HitTest(e.GetPosition(Canvas));
        if (element is null)
        {
            var addMenu = new ContextMenu { PlacementTarget = Canvas };
            foreach (var kind in new[]
                     {
                         HudElementKind.Text, HudElementKind.Bar, HudElementKind.Graph,
                         HudElementKind.MetricList, HudElementKind.Panel, HudElementKind.Divider, HudElementKind.Icon,
                     })
            {
                var captured = kind;
                addMenu.Items.Add(MenuItem(Loc.Inst[KindKey(captured)], () => CreateElement(captured)));
            }
            addMenu.IsOpen = true;
            e.Handled = true;
            return;
        }

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
    }

    private void OnExternalProfileChanged()
    {
        if (_loading || _switchingProfile) return;
        var active = _services.ActiveProfile;
        if (active.Id == _profile.Id) return;
        if (_dirty) SaveInternal(silent: true);
        LoadProfileChoices();
        LoadProfile(active);
    }

    private void OnLanguageChanged()
    {
        LoadProfileChoices();
        BuildProperties();
        UpdateStatus();
    }

    private void OnNewProfile(object sender, RoutedEventArgs e)
    {
        if (_dirty) SaveInternal(silent: true);
        var profile = _services.Profiles.CreateBlank(Loc.Inst["profiles.new"]);
        _services.SetActiveProfile(profile);
        LoadProfileChoices();
        LoadProfile(profile);
    }

    private void OnImportProfile(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "Focus profile (*.json)|*.json|All files (*.*)|*.*",
            Title = Loc.Inst["profiles.import"],
        };
        if (dialog.ShowDialog() != true) return;
        if (!_services.Profiles.Import(dialog.FileName, out var profile, out var error) || profile is null)
        {
            MessageBox.Show(error ?? Loc.Inst["msg.invalidProfile"], Loc.Inst["msg.importFailed"]);
            return;
        }
        _services.SetActiveProfile(profile);
        LoadProfileChoices();
        LoadProfile(profile);
    }

    // ------------------------------------------------------------- saving

    private void OnSave(object sender, RoutedEventArgs e) => SaveInternal(silent: false);
    public void FlushPendingSave()
    {
        if (_dirty) SaveInternal(silent: true);
    }

    private void SaveInternal(bool silent)
    {
        _profile.Normalize();
        _services.Profiles.Save(_profile);
        _services.NotifyProfileSaved(_profile);
        _dirty = false;
        UpdateSaveState();
        RebuildLayerList();
    }

    private void MarkDirty()
    {
        _dirty = true;
        UpdateSaveState();
        _autosave.Stop();
        _autosave.Start();
        _previewPump.Stop();
        _previewPump.Start();
    }

    private void UpdateSaveState()
    {
        SaveStateText.Text = Loc.Inst[_dirty ? "editor.unsaved" : "editor.saved"];
        SaveStateText.Foreground = (Brush)FindResource(_dirty ? "Brush.Warning" : "Brush.TextMuted");
    }

    // ------------------------------------------------------------- undo

    private void PushUndo()
    {
        _undo.Push(ProfileCodec.Serialize(_profile));
        while (_undo.Count > 80) TrimBottom(_undo);
        _redo.Clear();
        UpdateUndoButtons();
    }

    private static void TrimBottom(Stack<string> stack)
    {
        var items = stack.ToArray();
        stack.Clear();
        for (int i = items.Length - 2; i >= 0; i--) stack.Push(items[i]);
    }

    private void OnUndo(object sender, RoutedEventArgs e) => Undo();
    private void OnRedo(object sender, RoutedEventArgs e) => Redo();

    private void Undo()
    {
        if (_undo.Count == 0) return;
        _redo.Push(ProfileCodec.Serialize(_profile));
        ApplySnapshot(_undo.Pop());
        UpdateUndoButtons();
        MarkDirty();
    }

    private void Redo()
    {
        if (_redo.Count == 0) return;
        _undo.Push(ProfileCodec.Serialize(_profile));
        ApplySnapshot(_redo.Pop());
        UpdateUndoButtons();
        MarkDirty();
    }

    private void ApplySnapshot(string json)
    {
        try
        {
            var restored = ProfileCodec.Parse(json);
            var keep = _selection.Where(id => restored.FindElement(id) is not null).ToList();
            _profile = restored;
            Canvas.Profile = _profile;
            Canvas.SetSelection(keep, notify: false);
            _selection.Clear();
            _selection.AddRange(keep);
            Canvas.InvalidateVisual();
            RebuildLayerList();
            BuildProperties();
            UpdateStatus();
        }
        catch
        {
            // A corrupt snapshot is simply skipped.
        }
    }

    private void UpdateUndoButtons()
    {
        UndoButton.IsEnabled = _undo.Count > 0;
        RedoButton.IsEnabled = _redo.Count > 0;
    }

    // ------------------------------------------------------------- canvas events

    private void OnCanvasChanged()
    {
        MarkDirty();
        if (!Canvas.IsInteracting)
        {
            Canvas.RefreshSelection();
            BuildProperties();
        }
        SchedulePreview();
    }

    private void OnCanvasSelectionChanged()
    {
        _selection.Clear();
        _selection.AddRange(Canvas.SelectedIds);
        BuildProperties();
        UpdateStatus();
        SyncLayerSelection();
    }

    private void SelectOnly(string id)
    {
        _selection.Clear();
        _selection.Add(id);
        Canvas.SetSelection(_selection);
        BuildProperties();
        UpdateStatus();
        SyncLayerSelection();
    }

    private void OnLayerSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        if (ElementList.SelectedItem is not ElementRow row) return;
        if (_selection.Count == 1 && _selection[0] == row.Element.Id) return;
        _loading = true;
        Canvas.SetSelection(new[] { row.Element.Id });
        _selection.Clear();
        _selection.Add(row.Element.Id);
        BuildProperties();
        UpdateStatus();
        _loading = false;
    }

    private void SyncLayerSelection()
    {
        if (ElementList.ItemsSource is not IEnumerable<ElementRow> rows) return;
        _loading = true;
        var match = rows.FirstOrDefault(r => _selection.Contains(r.Element.Id));
        ElementList.SelectedItem = match;
        if (match is not null) ElementList.ScrollIntoView(match);
        _loading = false;
    }

    private void RebuildLayerList()
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
    }

    private void UpdateStatus()
    {
        EmptyCanvasHint.Visibility = _profile.Elements.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        StatusCanvasText.Text = $"{_profile.CanvasWidth:F0} × {_profile.CanvasHeight:F0}";
        StatusSelectionText.Text = _selection.Count == 0
            ? Loc.Inst["editor.noSelection"]
            : Loc.Inst.Format("editor.multiSelection", _selection.Count);
        ZoomText.Text = $"{Canvas.Zoom * 100:F0}%";
        AlignTools.Visibility = _selection.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
    }

    // ------------------------------------------------------------- element rows

    private void OnToggleVisibility(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: ElementRow row }) return;
        PushUndo();
        row.Element.Visible = !row.Element.Visible;
        AfterEdit();
    }

    private void OnToggleLock(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: ElementRow row }) return;
        PushUndo();
        row.Element.Locked = !row.Element.Locked;
        AfterEdit();
    }

    private void AfterEdit()
    {
        _profile.Normalize();
        Canvas.InvalidateVisual();
        Canvas.RefreshSelection();
        RebuildLayerList();
        UpdateStatus();
        MarkDirty();
    }

    private void OnPropertyChanged()
    {
        _profile.Normalize();
        Canvas.InvalidateVisual();
        Canvas.RefreshSelection();
        RefreshLayerRows();
        UpdateStatus();
        MarkDirty();
        SchedulePreview();
    }

    private void SchedulePreview()
    {
        _previewPump.Stop();
        _previewPump.Start();
    }

    // ------------------------------------------------------------- add / commands

    private void OnAddElement(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu { PlacementTarget = AddButton };
        AddMenuItem(menu, HudElementKind.Text, "editor.text");
        AddMenuItem(menu, HudElementKind.Bar, "editor.bar");
        AddMenuItem(menu, HudElementKind.Graph, "editor.graph");
        AddMenuItem(menu, HudElementKind.MetricList, "editor.metricList");
        AddMenuItem(menu, HudElementKind.Panel, "editor.panel");
        AddMenuItem(menu, HudElementKind.Divider, "editor.divider");
        AddMenuItem(menu, HudElementKind.Icon, "editor.icon");
        menu.IsOpen = true;
    }

    private void AddMenuItem(ContextMenu menu, HudElementKind kind, string key)
    {
        var item = new MenuItem { Header = Loc.Inst[key], Tag = kind };
        item.Click += (_, _) => CreateElement(kind);
        menu.Items.Add(item);
    }

    private HudElement CreateElement(HudElementKind kind)
    {
        PushUndo();
        var element = new HudElement
        {
            Kind = kind,
            ZIndex = _profile.Elements.Count == 0 ? 1 : _profile.Elements.Max(e => e.ZIndex) + 1,
            Name = Loc.Inst[KindKey(kind)],
        };

        switch (kind)
        {
            case HudElementKind.Text:
                element.Width = 150;
                element.Height = 28;
                element.Metric = MetricId.Fps;
                element.FontSize = 18;
                element.Template = "{value}";
                break;
            case HudElementKind.Bar:
                element.Width = 200;
                element.Height = 16;
                element.Metric = MetricId.GpuUsage;
                element.RangeMax = 100;
                element.CornerRadius = 4;
                element.ShowBarValue = false;
                element.PaddingLeft = 0;
                element.PaddingRight = 0;
                element.PaddingTop = 0;
                element.PaddingBottom = 0;
                break;
            case HudElementKind.Graph:
                element.Width = 240;
                element.Height = 80;
                element.Metric = MetricId.FrameTime;
                element.ShowGrid = true;
                element.Background = "#14000000";
                element.CornerRadius = 6;
                element.PaddingLeft = 2;
                element.PaddingTop = 2;
                element.PaddingRight = 2;
                element.PaddingBottom = 2;
                break;
            case HudElementKind.MetricList:
                element.Width = 220;
                element.Height = 96;
                element.FontSize = 13;
                element.Items.Add(new MetricListItem { Metric = MetricId.Fps, Label = "FPS" });
                element.Items.Add(new MetricListItem { Metric = MetricId.FpsOnePercentLow, Label = "1% Low" });
                element.Items.Add(new MetricListItem { Metric = MetricId.GpuUsage, Label = "GPU" });
                break;
            case HudElementKind.Panel:
                element.Width = 200;
                element.Height = 80;
                element.Background = "#B4000000";
                element.BorderColor = "#1FFFFFFF";
                element.BorderWidth = 1;
                element.CornerRadius = 10;
                break;
            case HudElementKind.Divider:
                element.Width = 200;
                element.Height = 1;
                element.BorderColor = "#33FFFFFF";
                break;
            case HudElementKind.Icon:
                element.Width = 28;
                element.Height = 28;
                element.Glyph = "";
                element.FontSize = 18;
                break;
        }

        element.X = Math.Max(0, (_profile.CanvasWidth - element.Width) / 2);
        element.Y = Math.Max(0, (_profile.CanvasHeight - element.Height) / 2 + (_profile.Elements.Count % 5) * 18);
        _profile.Elements.Add(element);
        SelectOnly(element.Id);
        AfterEdit();
        return element;
    }

    private static string KindKey(HudElementKind kind) => kind switch
    {
        HudElementKind.Text => "editor.text",
        HudElementKind.Bar => "editor.bar",
        HudElementKind.Graph => "editor.graph",
        HudElementKind.MetricList => "editor.metricList",
        HudElementKind.Panel => "editor.panel",
        HudElementKind.Divider => "editor.divider",
        _ => "editor.icon",
    };

    private void DuplicateSelection()
    {
        if (_selection.Count == 0) return;
        PushUndo();
        var created = new List<string>();
        foreach (var id in _selection.ToList())
        {
            var source = _profile.FindElement(id);
            if (source is null) continue;
            var copy = System.Text.Json.JsonSerializer.Deserialize<HudElement>(
                System.Text.Json.JsonSerializer.Serialize(source, ProfileCodec.Options), ProfileCodec.Options) ?? new HudElement();
            copy.Id = Guid.NewGuid().ToString("N");
            copy.X += 12;
            copy.Y += 12;
            copy.ZIndex = _profile.Elements.Max(e => e.ZIndex) + 1;
            copy.GroupId = null;
            _profile.Elements.Add(copy);
            created.Add(copy.Id);
        }
        _selection.Clear();
        _selection.AddRange(created);
        Canvas.SetSelection(_selection);
        AfterEdit();
    }

    private void DeleteSelection()
    {
        if (_selection.Count == 0) return;
        PushUndo();
        _profile.Elements.RemoveAll(e => _selection.Contains(e.Id) && !e.Locked);
        _selection.Clear();
        Canvas.SetSelection(Array.Empty<string>());
        AfterEdit();
    }

    private void CopySelection()
    {
        var items = _profile.Elements.Where(e => _selection.Contains(e.Id)).ToList();
        if (items.Count == 0) return;
        _clipboard = System.Text.Json.JsonSerializer.Serialize(items, ProfileCodec.Options);
    }

    private void PasteSelection()
    {
        if (string.IsNullOrWhiteSpace(_clipboard)) return;
        try
        {
            var items = System.Text.Json.JsonSerializer.Deserialize<List<HudElement>>(_clipboard, ProfileCodec.Options);
            if (items is null || items.Count == 0) return;
            PushUndo();
            var created = new List<string>();
            foreach (var item in items)
            {
                item.Id = Guid.NewGuid().ToString("N");
                item.X += 16;
                item.Y += 16;
                item.ZIndex = _profile.Elements.Count == 0 ? 1 : _profile.Elements.Max(e => e.ZIndex) + 1;
                _profile.Elements.Add(item);
                created.Add(item.Id);
            }
            _clipboard = System.Text.Json.JsonSerializer.Serialize(items, ProfileCodec.Options);
            _selection.Clear();
            _selection.AddRange(created);
            Canvas.SetSelection(_selection);
            AfterEdit();
        }
        catch
        {
            // ignore malformed clipboard
        }
    }

    private void GroupSelection()
    {
        if (_selection.Count < 2) return;
        PushUndo();
        string groupId = Guid.NewGuid().ToString("N")[..8];
        foreach (var id in _selection)
        {
            var element = _profile.FindElement(id);
            if (element is not null) element.GroupId = groupId;
        }
        AfterEdit();
    }

    private void UngroupSelection()
    {
        if (_selection.Count == 0) return;
        PushUndo();
        foreach (var id in _selection)
        {
            var element = _profile.FindElement(id);
            if (element is not null) element.GroupId = null;
        }
        AfterEdit();
    }

    private void ChangeZOrder(int delta)
    {
        if (_selection.Count == 0) return;
        PushUndo();
        foreach (var id in _selection)
        {
            var element = _profile.FindElement(id);
            if (element is null) continue;
            element.ZIndex = Math.Clamp(element.ZIndex + delta, 0, 9999);
        }
        AfterEdit();
    }

    private void OnAlign(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string mode } || _selection.Count == 0) return;
        PushUndo();
        var elements = _profile.Elements.Where(el => _selection.Contains(el.Id)).ToList();
        if (elements.Count == 0) return;

        var bounds = new Rect(
            elements.Min(el => el.X), elements.Min(el => el.Y),
            elements.Max(el => el.X + el.Width) - elements.Min(el => el.X),
            elements.Max(el => el.Y + el.Height) - elements.Min(el => el.Y));

        bool single = elements.Count == 1;
        double canvasW = _profile.CanvasWidth, canvasH = _profile.CanvasHeight;

        foreach (var element in elements)
        {
            switch (mode)
            {
                case "Left": element.X = single ? 0 : bounds.X; break;
                case "Center": element.X = single ? (canvasW - element.Width) / 2 : bounds.X + (bounds.Width - element.Width) / 2; break;
                case "Right": element.X = single ? canvasW - element.Width : bounds.Right - element.Width; break;
                case "Top": element.Y = single ? 0 : bounds.Y; break;
                case "Middle": element.Y = single ? (canvasH - element.Height) / 2 : bounds.Y + (bounds.Height - element.Height) / 2; break;
                case "Bottom": element.Y = single ? canvasH - element.Height : bounds.Bottom - element.Height; break;
            }
            element.X = Math.Round(element.X, 1);
            element.Y = Math.Round(element.Y, 1);
        }
        AfterEdit();
    }

    private void Distribute(bool horizontal)
    {
        var elements = _profile.Elements.Where(el => _selection.Contains(el.Id)).ToList();
        if (elements.Count < 3) return;
        PushUndo();

        if (horizontal)
        {
            elements.Sort((a, b) => a.X.CompareTo(b.X));
            double left = elements[0].X;
            double right = elements[^1].X;
            double step = (right - left) / (elements.Count - 1);
            for (int i = 1; i < elements.Count - 1; i++)
            {
                elements[i].X = Math.Round(left + step * i, 1);
            }
        }
        else
        {
            elements.Sort((a, b) => a.Y.CompareTo(b.Y));
            double top = elements[0].Y;
            double bottom = elements[^1].Y;
            double step = (bottom - top) / (elements.Count - 1);
            for (int i = 1; i < elements.Count - 1; i++)
            {
                elements[i].Y = Math.Round(top + step * i, 1);
            }
        }
        AfterEdit();
    }

    private DateTime _lastNudge = DateTime.MinValue;

    private void Nudge(double dx, double dy)
    {
        if (_selection.Count == 0) return;
        // Holding an arrow key should be one undo step, not one per pixel.
        if ((DateTime.UtcNow - _lastNudge).TotalMilliseconds > 700) PushUndo();
        _lastNudge = DateTime.UtcNow;
        foreach (var id in _selection)
        {
            var element = _profile.FindElement(id);
            if (element is null || element.Locked) continue;
            element.X += dx;
            element.Y += dy;
        }
        AfterEdit();
    }

    private GridLength _layerWidth = new(184);
    private GridLength _propertyWidth = new(280);
    private void OnFocusCanvasChanged(object sender, RoutedEventArgs e)
    {
        if (LayerPanel is null || PropertyPanel is null) return;
        bool focus = FocusCanvasToggle.IsChecked == true;
        if (focus)
        {
            _layerWidth = LayerColumn.Width;
            _propertyWidth = PropertyColumn.Width;
        }
        LayerColumn.MinWidth = focus ? 0 : 160;
        PropertyColumn.MinWidth = focus ? 0 : 240;
        LayerColumn.Width = focus ? new GridLength(0) : _layerWidth;
        PropertyColumn.Width = focus ? new GridLength(0) : _propertyWidth;
        LayerPanel.Visibility = PropertyPanel.Visibility = LeftResize.Visibility = RightResize.Visibility =
            focus ? Visibility.Collapsed : Visibility.Visible;
        Canvas.Focus();
    }

    // ------------------------------------------------------------- keyboard

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        bool typing = Keyboard.FocusedElement is TextBox;
        bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
        bool shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;

        if (e.Key == Key.Space && !typing)
        {
            Canvas.SetSpaceDown(true);
            e.Handled = true;
            return;
        }

        if (ctrl && e.Key == Key.S)
        {
            SaveInternal(silent: false);
            e.Handled = true;
            return;
        }

        if (ctrl && shift && e.Key == Key.F)
        {
            FocusCanvasToggle.IsChecked = FocusCanvasToggle.IsChecked != true;
            e.Handled = true;
            return;
        }

        if (typing) return;

        if (ctrl && e.Key == Key.Z)
        {
            if (shift) Redo(); else Undo();
            e.Handled = true;
        }
        else if (ctrl && e.Key == Key.Y)
        {
            Redo();
            e.Handled = true;
        }
        else if (ctrl && e.Key == Key.C)
        {
            CopySelection();
            e.Handled = true;
        }
        else if (ctrl && e.Key == Key.V)
        {
            PasteSelection();
            e.Handled = true;
        }
        else if (ctrl && e.Key == Key.D)
        {
            DuplicateSelection();
            e.Handled = true;
        }
        else if (ctrl && e.Key == Key.G)
        {
            if (shift) UngroupSelection(); else GroupSelection();
            e.Handled = true;
        }
        else if (ctrl && e.Key == Key.A)
        {
            Canvas.SelectAll();
            e.Handled = true;
        }
        else if (e.Key == Key.Delete || e.Key == Key.Back)
        {
            DeleteSelection();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            Canvas.SetSelection(Array.Empty<string>());
            e.Handled = true;
        }
        else if (e.Key == Key.Left) { Nudge(shift ? -10 : -1, 0); e.Handled = true; }
        else if (e.Key == Key.Right) { Nudge(shift ? 10 : 1, 0); e.Handled = true; }
        else if (e.Key == Key.Up) { Nudge(0, shift ? -10 : -1); e.Handled = true; }
        else if (e.Key == Key.Down) { Nudge(0, shift ? 10 : 1); e.Handled = true; }
    }

    private void OnPreviewKeyUp(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Space) Canvas.SetSpaceDown(false);
    }

    // ------------------------------------------------------------- property panel

    private void BuildProperties()
    {
        Properties.Children.Clear();

        if (_selection.Count == 0)
        {
            SelectionTitle.Text = Loc.Inst["editor.noSelection"];
            BuildCanvasProperties();
            return;
        }

        if (_selection.Count > 1)
        {
            SelectionTitle.Text = Loc.Inst.Format("editor.multiSelection", _selection.Count);
            BuildMultiProperties();
            return;
        }

        var element = _profile.FindElement(_selection[0]);
        if (element is null)
        {
            SelectionTitle.Text = Loc.Inst["editor.noSelection"];
            return;
        }

        SelectionTitle.Text = element.Name;
        BuildElementProperties(element);
    }

    private void BuildCanvasProperties()
    {
        Properties.AddSection(Loc.Inst["editor.canvas"]);
        Properties.AddNumber(Loc.Inst["prop.width"], _profile.CanvasWidth, v => _profile.CanvasWidth = v, 80, 4096, 0, " px");
        Properties.AddNumber(Loc.Inst["prop.height"], _profile.CanvasHeight, v => _profile.CanvasHeight = v, 40, 4096, 0, " px");

        Properties.AddSection(Loc.Inst["editor.display"]);
        Properties.AddSlide(Loc.Inst["editor.scale"], _profile.Display.Scale, 0.4, 2.5,
            v => _profile.Display.Scale = Math.Round(v, 2), v => $"{v:F2}×");
        Properties.AddSlide(Loc.Inst["editor.opacity"], _profile.Display.Opacity, 0.1, 1.0,
            v => _profile.Display.Opacity = Math.Round(v, 2), v => $"{v * 100:F0}%");

        var anchors = Enum.GetValues<HudAnchor>()
            .Select(a => (a, Loc.Inst[LocKeys.Anchor(a)])).ToList();
        Properties.AddChoice(Loc.Inst["editor.anchor"], anchors, _profile.Display.Anchor, v => _profile.Display.Anchor = v);

        Properties.AddNumber(Loc.Inst["editor.offsetX"], _profile.Display.OffsetX, v => _profile.Display.OffsetX = v, -4096, 4096, 0, " px");
        Properties.AddNumber(Loc.Inst["editor.offsetY"], _profile.Display.OffsetY, v => _profile.Display.OffsetY = v, -4096, 4096, 0, " px");
        Properties.AddBool(Loc.Inst["editor.followGame"], _profile.Display.FollowGameWindow, v => _profile.Display.FollowGameWindow = v);

        var monitors = new List<(string? Value, string Text)> { (null, Loc.Inst["editor.primaryMonitor"]) };
        try
        {
            foreach (var screen in System.Windows.Forms.Screen.AllScreens)
            {
                monitors.Add((screen.DeviceName, $"{screen.DeviceName} · {screen.Bounds.Width}×{screen.Bounds.Height}"));
            }
        }
        catch
        {
            // Screen enumeration is optional.
        }
        Properties.AddChoice(Loc.Inst["editor.monitor"], monitors, _profile.Display.MonitorDeviceName,
            v => _profile.Display.MonitorDeviceName = v);

        Properties.AddSection(Loc.Inst["editor.gameBinding"]);
        Properties.AddText(Loc.Inst["profiles.bindGame"], _profile.GameExecutable ?? string.Empty,
            v => _profile.GameExecutable = Services.ProfileStore.NormalizeGameName(v), "game.exe");

        var browseRow = new Grid { Margin = new Thickness(0, 0, 0, 6) };
        browseRow.ColumnDefinitions.Add(new ColumnDefinition());
        browseRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
        browseRow.ColumnDefinitions.Add(new ColumnDefinition());
        var browse = new Button
        {
            Content = Loc.Inst["common.browse"],
            Style = (Style)FindResource("Btn.Ghost"),
            Height = 26,
            FontSize = 11.5,
        };
        browse.Click += (_, _) =>
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "Executable (*.exe)|*.exe|All files (*.*)|*.*",
                Title = Loc.Inst["profiles.bindGame"],
            };
            if (dialog.ShowDialog() != true) return;
            _profile.GameExecutable = Services.ProfileStore.NormalizeGameName(dialog.FileName);
            BuildProperties();
            OnPropertyChanged();
        };
        var clear = new Button
        {
            Content = Loc.Inst["common.none"],
            Style = (Style)FindResource("Btn.Ghost"),
            Height = 26,
            FontSize = 11.5,
        };
        clear.Click += (_, _) =>
        {
            _profile.GameExecutable = null;
            BuildProperties();
            OnPropertyChanged();
        };
        Grid.SetColumn(clear, 2);
        browseRow.Children.Add(browse);
        browseRow.Children.Add(clear);
        Properties.AddWide(browseRow);
        Properties.AddHint(Loc.Inst["profiles.bindHint"]);
    }

    private void BuildMultiProperties()
    {
        Properties.AddSection(Loc.Inst["editor.align"]);
        Properties.AddHint(Loc.Inst.Format("editor.multiSelection", _selection.Count));

        var alignPanel = new WrapPanel();
        foreach (var (mode, key) in new[]
                 {
                     ("Left", "editor.alignLeft"), ("Center", "editor.alignCenter"), ("Right", "editor.alignRight"),
                     ("Top", "editor.alignTop"), ("Middle", "editor.alignMiddle"), ("Bottom", "editor.alignBottom"),
                 })
        {
            var button = new Button
            {
                Content = Loc.Inst[key],
                Style = (Style)FindResource("Btn"),
                Tag = mode,
                Margin = new Thickness(0, 0, 4, 4),
                Height = 28,
                FontSize = 11.5,
                Padding = new Thickness(9, 0, 9, 0),
            };
            button.Click += OnAlign;
            alignPanel.Children.Add(button);
        }
        Properties.AddWide(alignPanel);

        var distributeRow = new Grid { Margin = new Thickness(0, 0, 0, 6) };
        distributeRow.ColumnDefinitions.Add(new ColumnDefinition());
        distributeRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
        distributeRow.ColumnDefinitions.Add(new ColumnDefinition());
        var distributeH = new Button
        {
            Content = Loc.Inst["editor.distributeH"],
            Style = (Style)FindResource("Btn"),
            Height = 28,
            FontSize = 11.5,
        };
        distributeH.Click += (_, _) => Distribute(true);
        var distributeV = new Button
        {
            Content = Loc.Inst["editor.distributeV"],
            Style = (Style)FindResource("Btn"),
            Height = 28,
            FontSize = 11.5,
        };
        distributeV.Click += (_, _) => Distribute(false);
        Grid.SetColumn(distributeV, 2);
        distributeRow.Children.Add(distributeH);
        distributeRow.Children.Add(distributeV);
        Properties.AddWide(distributeRow);

        var groupRow = new Grid();
        groupRow.ColumnDefinitions.Add(new ColumnDefinition());
        groupRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
        groupRow.ColumnDefinitions.Add(new ColumnDefinition());
        var group = new Button { Content = Loc.Inst["editor.group"], Style = (Style)FindResource("Btn"), Height = 28, FontSize = 11.5 };
        group.Click += (_, _) => GroupSelection();
        var ungroup = new Button { Content = Loc.Inst["editor.ungroup"], Style = (Style)FindResource("Btn"), Height = 28, FontSize = 11.5 };
        ungroup.Click += (_, _) => UngroupSelection();
        Grid.SetColumn(ungroup, 2);
        groupRow.Children.Add(group);
        groupRow.Children.Add(ungroup);
        Properties.AddWide(groupRow);

        Properties.AddSection(Loc.Inst["editor.layers"]);
        var orderPanel = new Grid();
        orderPanel.ColumnDefinitions.Add(new ColumnDefinition());
        orderPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
        orderPanel.ColumnDefinitions.Add(new ColumnDefinition());
        var forward = new Button { Content = Loc.Inst["editor.bringForward"], Style = (Style)FindResource("Btn"), Height = 28, FontSize = 11.5 };
        forward.Click += (_, _) => ChangeZOrder(1);
        var backward = new Button { Content = Loc.Inst["editor.sendBackward"], Style = (Style)FindResource("Btn"), Height = 28, FontSize = 11.5 };
        backward.Click += (_, _) => ChangeZOrder(-1);
        Grid.SetColumn(backward, 2);
        orderPanel.Children.Add(forward);
        orderPanel.Children.Add(backward);
        Properties.AddWide(orderPanel);
    }

    private void BuildElementProperties(HudElement element)
    {
        Properties.AddSection(Loc.Inst["editor.properties"]);
        Properties.AddText(Loc.Inst["prop.name"], element.Name, v => { element.Name = v; SelectionTitle.Text = v; });
        Properties.AddBool(Loc.Inst["prop.visible"], element.Visible, v => element.Visible = v);
        Properties.AddBool(Loc.Inst["prop.locked"], element.Locked, v => element.Locked = v);

        Properties.AddSection(Loc.Inst["editor.layout"]);
        Properties.AddNumber(Loc.Inst["prop.x"], element.X, v => element.X = v, -4096, 4096, 1);
        Properties.AddNumber(Loc.Inst["prop.y"], element.Y, v => element.Y = v, -4096, 4096, 1);
        Properties.AddNumber(Loc.Inst["prop.width"], element.Width, v => element.Width = v, 4, 4096, 1);
        Properties.AddNumber(Loc.Inst["prop.height"], element.Height, v => element.Height = v, 4, 4096, 1);

        switch (element.Kind)
        {
            case HudElementKind.Text:
            case HudElementKind.Icon:
                BuildTextProperties(element);
                break;
            case HudElementKind.Bar:
                BuildBarProperties(element);
                break;
            case HudElementKind.Graph:
                BuildGraphProperties(element);
                break;
            case HudElementKind.MetricList:
                BuildListProperties(element);
                break;
            case HudElementKind.Divider:
                BuildDividerProperties(element);
                break;
        }

        BuildStyleProperties(element);
        BuildConditions(element);
    }

    private void BuildTextProperties(HudElement element)
    {
        Properties.AddSection(Loc.Inst["editor.text"]);
        if (element.Kind == HudElementKind.Icon)
        {
            Properties.AddText(Loc.Inst["prop.glyph"], element.Glyph, v => element.Glyph = v);
            Properties.AddText(Loc.Inst["prop.fontFamily"], element.GlyphFontFamily, v => element.GlyphFontFamily = v);
            Properties.AddNumber(Loc.Inst["prop.fontSize"], element.FontSize, v => element.FontSize = v, 4, 256, 1);
            return;
        }

        Properties.AddChoice(Loc.Inst["prop.metric"], MetricOptions(), element.Metric, v => element.Metric = v);
        if (element.Metric == MetricId.CpuCoreUsage)
        {
            Properties.AddNumber(Loc.Inst["prop.coreIndex"], element.CoreIndex, v => element.CoreIndex = (int)v, 0, 127, 0);
        }
        Properties.AddText(Loc.Inst["prop.template"], element.Template, v => element.Template = v, "{value} {label}");
        Properties.AddText(Loc.Inst["prop.label"], element.Label, v => element.Label = v);

        Properties.AddSection(Loc.Inst["editor.typography"]);
        Properties.AddText(Loc.Inst["prop.fontFamily"], element.FontFamily, v => element.FontFamily = v);
        Properties.AddNumber(Loc.Inst["prop.fontSize"], element.FontSize, v => element.FontSize = v, 4, 256, 1);
        Properties.AddNumber(Loc.Inst["prop.fontWeight"], element.FontWeight, v => element.FontWeight = (int)v, 100, 900, 0);
        Properties.AddBool(Loc.Inst["prop.italic"], element.Italic, v => element.Italic = v);
        Properties.AddSegmented(Loc.Inst["prop.align"], new[]
        {
            (HudTextAlign.Left, Loc.Inst["hud.align.left"]),
            (HudTextAlign.Center, Loc.Inst["hud.align.center"]),
            (HudTextAlign.Right, Loc.Inst["hud.align.right"]),
        }, element.TextAlign, v => element.TextAlign = v);
        Properties.AddSegmented(Loc.Inst["prop.valign"], new[]
        {
            (HudVerticalAlign.Top, Loc.Inst["hud.valign.top"]),
            (HudVerticalAlign.Center, Loc.Inst["hud.valign.center"]),
            (HudVerticalAlign.Bottom, Loc.Inst["hud.valign.bottom"]),
        }, element.VerticalAlign, v => element.VerticalAlign = v);

        Properties.AddSection(Loc.Inst["prop.decimals"]);
        Properties.AddNumber(Loc.Inst["prop.decimals"], element.Decimals, v => element.Decimals = (int)v, -1, 4, 0);
        Properties.AddBool(Loc.Inst["prop.showUnit"], element.ShowUnit, v => element.ShowUnit = v);
    }

    private void BuildBarProperties(HudElement element)
    {
        Properties.AddSection(Loc.Inst["editor.bar"]);
        Properties.AddChoice(Loc.Inst["prop.metric"], MetricOptions(), element.Metric, v => element.Metric = v);
        Properties.AddNumber(Loc.Inst["prop.scaleMin"], element.RangeMin, v => element.RangeMin = v, -100000, 100000, 1);
        Properties.AddNumber(Loc.Inst["prop.scaleMax"], element.RangeMax, v => element.RangeMax = v, -100000, 100000, 1);
        Properties.AddChoice(Loc.Inst["prop.direction"], new[]
        {
            (BarDirection.LeftToRight, Loc.Inst["hud.dir.ltr"]),
            (BarDirection.RightToLeft, Loc.Inst["hud.dir.rtl"]),
            (BarDirection.BottomToTop, Loc.Inst["hud.dir.btt"]),
            (BarDirection.TopToBottom, Loc.Inst["hud.dir.ttb"]),
        }, element.BarDirection, v => element.BarDirection = v);
        Properties.AddColor(Loc.Inst["prop.trackColor"], element.TrackColor, v => element.TrackColor = v ?? "#33FFFFFF", allowEmpty: false);
        Properties.AddColor(Loc.Inst["prop.fillColor"], element.BarFillColor, v => element.BarFillColor = v ?? "#FF4EA1FF", allowEmpty: false);
        Properties.AddBool(Loc.Inst["prop.showValue"], element.ShowBarValue, v => element.ShowBarValue = v);
        Properties.AddNumber(Loc.Inst["prop.valueFontSize"], element.BarValueFontSize, v => element.BarValueFontSize = v, 4, 64, 1);
    }

    private void BuildGraphProperties(HudElement element)
    {
        Properties.AddSection(Loc.Inst["editor.graph"]);
        Properties.AddChoice(Loc.Inst["prop.metric"], MetricOptions(), element.Metric, v => element.Metric = v);
        Properties.AddNumber(Loc.Inst["prop.windowSeconds"], element.WindowSeconds, v => element.WindowSeconds = (int)v, 2, 120, 0, " s");
        Properties.AddBool(Loc.Inst["prop.autoScale"], element.AutoScale, v => element.AutoScale = v);
        Properties.AddNumber(Loc.Inst["prop.scaleMin"], element.ScaleMin, v => element.ScaleMin = v, -100000, 100000, 1);
        Properties.AddNumber(Loc.Inst["prop.scaleMax"], element.ScaleMax, v => element.ScaleMax = v, -100000, 100000, 1);
        Properties.AddBool(Loc.Inst["prop.showGrid"], element.ShowGrid, v => element.ShowGrid = v);
        Properties.AddColor(Loc.Inst["prop.gridColor"], element.GridColor, v => element.GridColor = v ?? "#22FFFFFF", allowEmpty: false);
        Properties.AddBool(Loc.Inst["prop.showFill"], element.ShowGraphFill, v => element.ShowGraphFill = v);
        Properties.AddSlide(Loc.Inst["prop.fillOpacity"], element.GraphFillOpacity, 0, 1, v => element.GraphFillOpacity = Math.Round(v, 2), v => $"{v * 100:F0}%");
        Properties.AddColor(Loc.Inst["prop.lineColor"], element.LineColor, v => element.LineColor = v ?? "#FF4EA1FF", allowEmpty: false);
        Properties.AddNumber(Loc.Inst["prop.lineWidth"], element.LineWidth, v => element.LineWidth = v, 0.5, 12, 1);
        Properties.AddBool(Loc.Inst["prop.showStats"], element.ShowGraphStats, v => element.ShowGraphStats = v);
    }

    private void BuildListProperties(HudElement element)
    {
        Properties.AddSection(Loc.Inst["editor.metricList"]);
        Properties.AddNumber(Loc.Inst["prop.fontSize"], element.FontSize, v => element.FontSize = v, 4, 64, 1);
        Properties.AddNumber(Loc.Inst["prop.rowSpacing"], element.RowSpacing, v => element.RowSpacing = v, 0, 40, 1);
        Properties.AddBool(Loc.Inst["prop.showLabels"], element.ShowMetricListLabels, v => element.ShowMetricListLabels = v);
        Properties.AddNumber(Loc.Inst["prop.labelWidth"], element.LabelColumnWidth, v => element.LabelColumnWidth = v, 0, 1000, 1);

        Properties.AddSection(Loc.Inst["prop.items"]);
        for (int i = 0; i < element.Items.Count; i++)
        {
            int index = i;
            var item = element.Items[i];
            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(6) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(86) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(26) });

            var metricCombo = new ComboBox { Height = 28, FontSize = 11 };
            var options = MetricOptions();
            metricCombo.ItemsSource = options.Select(o => o.Text).ToList();
            metricCombo.SelectedIndex = Math.Max(0, options.FindIndex(o => o.Value == item.Metric));
            metricCombo.SelectionChanged += (_, _) =>
            {
                if (metricCombo.SelectedIndex < 0) return;
                item.Metric = options[metricCombo.SelectedIndex].Value;
                OnPropertyChanged();
            };
            Grid.SetColumn(metricCombo, 0);
            row.Children.Add(metricCombo);

            var labelBox = new TextBox { Text = item.Label, Height = 28, FontSize = 11 };
            labelBox.LostFocus += (_, _) => { item.Label = labelBox.Text; OnPropertyChanged(); };
            Grid.SetColumn(labelBox, 2);
            row.Children.Add(labelBox);

            var remove = new Button
            {
                Content = "×",
                Style = (Style)FindResource("Btn.Icon"),
                Width = 24,
                Height = 24,
                FontSize = 12,
            };
            remove.Click += (_, _) =>
            {
                PushUndo();
                element.Items.RemoveAt(index);
                BuildProperties();
                OnPropertyChanged();
            };
            Grid.SetColumn(remove, 3);
            row.Children.Add(remove);

            Properties.AddWide(row);
        }

        var add = new Button
        {
            Content = Loc.Inst["prop.addItem"],
            Style = (Style)FindResource("Btn.Ghost"),
            Height = 28,
            FontSize = 11.5,
        };
        add.Click += (_, _) =>
        {
            PushUndo();
            element.Items.Add(new MetricListItem { Metric = MetricId.GpuUsage, Label = "GPU" });
            BuildProperties();
            OnPropertyChanged();
        };
        Properties.AddWide(add);
    }

    private void BuildDividerProperties(HudElement element)
    {
        Properties.AddSection(Loc.Inst["editor.divider"]);
        Properties.AddChoice(Loc.Inst["prop.orientation"], new[]
        {
            (DividerOrientation.Horizontal, Loc.Inst["hud.orient.horizontal"]),
            (DividerOrientation.Vertical, Loc.Inst["hud.orient.vertical"]),
        }, element.DividerOrientation, v => element.DividerOrientation = v);
        Properties.AddNumber(Loc.Inst["prop.thickness"], element.DividerThickness, v => element.DividerThickness = v, 0.5, 20, 1);
        Properties.AddColor(Loc.Inst["prop.borderColor"], element.BorderColor ?? element.Color, v => element.BorderColor = v ?? "#33FFFFFF", allowEmpty: false);
    }

    private void BuildStyleProperties(HudElement element)
    {
        Properties.AddSection(Loc.Inst["editor.style"]);
        Properties.AddColor(Loc.Inst["prop.color"], element.Color, v => element.Color = v ?? "#FFFFFFFF", allowEmpty: false);
        Properties.AddSlide(Loc.Inst["prop.opacity"], element.Opacity, 0.05, 1, v => element.Opacity = Math.Round(v, 2), v => $"{v * 100:F0}%");
        Properties.AddColor(Loc.Inst["prop.background"], element.Background, v => element.Background = v, allowEmpty: true);
        if (element.Background is not null)
        {
            Properties.AddSlide(Loc.Inst["prop.backgroundOpacity"], element.BackgroundOpacity, 0, 1, v => element.BackgroundOpacity = Math.Round(v, 2), v => $"{v * 100:F0}%");
            Properties.AddNumber(Loc.Inst["prop.cornerRadius"], element.CornerRadius, v => element.CornerRadius = v, 0, 200, 1);
        }
        Properties.AddColor(Loc.Inst["prop.borderColor"], element.BorderColor, v => element.BorderColor = v, allowEmpty: true);
        if (element.BorderColor is not null)
        {
            Properties.AddNumber(Loc.Inst["prop.borderWidth"], element.BorderWidth, v => element.BorderWidth = v, 0, 20, 1);
            Properties.AddNumber(Loc.Inst["prop.cornerRadius"], element.CornerRadius, v => element.CornerRadius = v, 0, 200, 1);
        }
        Properties.AddNumber(Loc.Inst["prop.paddingLeft"], element.PaddingLeft, v => element.PaddingLeft = v, 0, 200, 1);
        Properties.AddNumber(Loc.Inst["prop.paddingTop"], element.PaddingTop, v => element.PaddingTop = v, 0, 200, 1);
        Properties.AddNumber(Loc.Inst["prop.paddingRight"], element.PaddingRight, v => element.PaddingRight = v, 0, 200, 1);
        Properties.AddNumber(Loc.Inst["prop.paddingBottom"], element.PaddingBottom, v => element.PaddingBottom = v, 0, 200, 1);
    }

    private void BuildConditions(HudElement element)
    {
        Properties.AddSection(Loc.Inst["prop.conditions"]);
        if (element.Conditions.Count == 0)
        {
            Properties.AddHint(Loc.Inst["prop.noConditions"]);
        }

        for (int i = 0; i < element.Conditions.Count; i++)
        {
            var rule = element.Conditions[i];
            var header = new Grid();
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(26) });
            var title = new TextBlock
            {
                Text = $"{Loc.Inst[LocKeys.Metric(rule.Metric)]} {OperatorText(rule.Operator)} {rule.Threshold:F0}",
                FontSize = 11.5,
                Foreground = (Brush)FindResource("Brush.TextSecondary"),
                VerticalAlignment = VerticalAlignment.Center,
            };
            var remove = new Button { Content = "×", Style = (Style)FindResource("Btn.Icon"), Width = 24, Height = 24 };
            remove.Click += (_, _) =>
            {
                PushUndo();
                element.Conditions.Remove(rule);
                BuildProperties();
                OnPropertyChanged();
            };
            Grid.SetColumn(remove, 1);
            header.Children.Add(title);
            header.Children.Add(remove);
            Properties.AddWide(header);

            Properties.AddChoice(Loc.Inst["prop.metric"], MetricOptions(), rule.Metric, v => { rule.Metric = v; });
            Properties.AddChoice(Loc.Inst["prop.operator"], new[]
            {
                (ComparisonOperator.Greater, Loc.Inst["hud.op.greater"]),
                (ComparisonOperator.GreaterOrEqual, Loc.Inst["hud.op.greaterEqual"]),
                (ComparisonOperator.Less, Loc.Inst["hud.op.less"]),
                (ComparisonOperator.LessOrEqual, Loc.Inst["hud.op.lessEqual"]),
            }, rule.Operator, v => { rule.Operator = v; });
            Properties.AddNumber(Loc.Inst["prop.threshold"], rule.Threshold, v => rule.Threshold = v, -100000, 100000, 1);
            Properties.AddChoice(Loc.Inst["prop.target"], new[]
            {
                (ColorTarget.Foreground, Loc.Inst["hud.target.foreground"]),
                (ColorTarget.Background, Loc.Inst["hud.target.background"]),
                (ColorTarget.Border, Loc.Inst["hud.target.border"]),
                (ColorTarget.BarFill, Loc.Inst["hud.target.barFill"]),
            }, rule.Target, v => rule.Target = v);
            Properties.AddColor(Loc.Inst["prop.conditionColor"], rule.Color, v => rule.Color = v ?? "#FFFF6B6B", allowEmpty: false);
        }

        var add = new Button
        {
            Content = Loc.Inst["prop.addCondition"],
            Style = (Style)FindResource("Btn.Ghost"),
            Height = 28,
            FontSize = 11.5,
        };
        add.Click += (_, _) =>
        {
            PushUndo();
            element.Conditions.Add(new ConditionalColorRule
            {
                Metric = element.Metric,
                Threshold = 80,
                Operator = ComparisonOperator.GreaterOrEqual,
                Target = ColorTarget.Foreground,
                Color = "#FFFF6B6B",
            });
            BuildProperties();
            OnPropertyChanged();
        };
        Properties.AddWide(add);
    }

    private static string OperatorText(ComparisonOperator op) => op switch
    {
        ComparisonOperator.Greater => ">",
        ComparisonOperator.GreaterOrEqual => "≥",
        ComparisonOperator.Less => "<",
        _ => "≤",
    };

    private List<(MetricId Value, string Text)> MetricOptions()
    {
        return MetricCatalog.All
            .OrderBy(d => d.SortOrder)
            .Select(d => (d.Id, Loc.Inst[LocKeys.Metric(d.Id)]))
            .ToList();
    }

    // ------------------------------------------------------------- view commands

    private void OnZoomIn(object sender, RoutedEventArgs e) { Canvas.Zoom *= 1.15; UpdateStatus(); }
    private void OnZoomOut(object sender, RoutedEventArgs e) { Canvas.Zoom /= 1.15; UpdateStatus(); }
    private void OnZoomFit(object sender, RoutedEventArgs e) { Canvas.ZoomToFit(); UpdateStatus(); }

    private void OnGridToggle(object sender, RoutedEventArgs e)
    {
        if (Canvas is null) return;
        Canvas.GridEnabled = GridToggle?.IsChecked == true;
        Canvas.InvalidateVisual();
    }

    private void OnSnapToggle(object sender, RoutedEventArgs e)
    {
        if (Canvas is null) return;
        Canvas.SnapEnabled = SnapToggle?.IsChecked == true;
    }
}

public sealed class ElementRow : System.ComponentModel.INotifyPropertyChanged
{
    public ElementRow(HudElement element) => Element = element;

    public HudElement Element { get; }

    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;

    public void Refresh()
    {
        PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(string.Empty));
    }

    public string Name => Element.Name;

    public string Glyph => Element.Kind switch
    {
        HudElementKind.Text => "T",
        HudElementKind.Bar => "▬",
        HudElementKind.Graph => "∿",
        HudElementKind.MetricList => "☰",
        HudElementKind.Panel => "▢",
        HudElementKind.Divider => "─",
        _ => "★",
    };

    public string VisibilityGlyph => "";

    public Brush VisibilityBrush => Element.Visible
        ? new SolidColorBrush(Color.FromRgb(0x98, 0xA1, 0xAE))
        : new SolidColorBrush(Color.FromArgb(0x55, 0x98, 0xA1, 0xAE));

    public string LockGlyph => Element.Locked ? "" : "";

    public Brush LockBrush => Element.Locked
        ? new SolidColorBrush(Color.FromRgb(0xF0, 0xB4, 0x5E))
        : new SolidColorBrush(Color.FromArgb(0x44, 0x98, 0xA1, 0xAE));
}

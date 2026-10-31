using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Focus.App.Controls;
using Focus.App.Localization;
using Focus.App.Services;
using Focus.Core.Profiles;

namespace Focus.App.Views;

public partial class ProfilesPage : UserControl
{
    private readonly AppServices _services;

    public event Action<string>? NavigateRequested;

    public ProfilesPage(AppServices services)
    {
        _services = services;
        InitializeComponent();
        Loaded += (_, _) => Rebuild();
        _services.Profiles.Changed += OnStoreChanged;
        _services.ActiveProfileChanged += OnStoreChanged;
        Loc.Inst.LanguageChanged += OnStoreChanged;
    }

    private void OnStoreChanged()
    {
        if (IsLoaded) Rebuild();
    }

    private void Rebuild()
    {
        ProfileGrid.Children.Clear();
        PresetGrid.Children.Clear();

        foreach (var profile in _services.Profiles.Custom)
        {
            ProfileGrid.Children.Add(BuildCard(profile, isPreset: false));
        }

        foreach (var preset in _services.Profiles.Presets)
        {
            PresetGrid.Children.Add(BuildCard(preset, isPreset: true));
        }

        EmptyState.Visibility = _services.Profiles.Custom.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private FrameworkElement BuildCard(HudProfile profile, bool isPreset)
    {
        var card = new Border
        {
            Style = (Style)FindResource("Card"),
            Width = 320,
            Padding = new Thickness(0),
            Margin = new Thickness(0, 0, 16, 16),
            ClipToBounds = true,
        };

        var stack = new StackPanel();

        var previewBorder = new Border
        {
            Height = 148,
            Background = new SolidColorBrush(Color.FromRgb(0x0A, 0x0C, 0x10)),
            CornerRadius = new CornerRadius(9, 9, 0, 0),
            ClipToBounds = true,
        };
        var preview = new HudPreview
        {
            Profile = profile,
            Data = global::Focus.Hud.SampleHudDataSource.Instance,
            Padding = 12,
        };
        previewBorder.Child = preview;
        stack.Children.Add(previewBorder);

        var info = new StackPanel { Margin = new Thickness(16, 12, 16, 14) };

        var titleRow = new DockPanel();
        var name = new TextBlock
        {
            Text = DisplayName(profile),
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        var count = new TextBlock
        {
            Text = Loc.Inst.Format("profiles.elementCount", profile.Elements.Count),
            Style = (Style)FindResource("Text.Caption"),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
        };
        DockPanel.SetDock(count, Dock.Right);
        titleRow.Children.Add(count);
        titleRow.Children.Add(name);
        info.Children.Add(titleRow);

        string meta = isPreset
            ? Loc.Inst["profiles.readonly"]
            : string.IsNullOrWhiteSpace(profile.GameExecutable)
                ? Loc.Inst["profiles.default"]
                : $"{Loc.Inst["profiles.bindGame"]}: {profile.GameExecutable}";
        if (_services.Settings.Current.ActiveProfileId == profile.Id) meta += "  ·  ★";
        info.Children.Add(new TextBlock
        {
            Text = meta,
            Style = (Style)FindResource("Text.Muted"),
            Margin = new Thickness(0, 5, 0, 10),
            TextTrimming = TextTrimming.CharacterEllipsis,
        });

        var actions = new WrapPanel();
        actions.Children.Add(SmallButton(Loc.Inst["profiles.edit"], "Btn.Primary", () => Edit(profile, isPreset)));
        if (isPreset)
        {
            actions.Children.Add(SmallButton(Loc.Inst["common.duplicate"], "Btn", () => Duplicate(profile)));
        }
        else
        {
            actions.Children.Add(SmallButton(Loc.Inst["profiles.setDefault"], "Btn", () => SetDefault(profile)));
            actions.Children.Add(SmallButton(Loc.Inst["common.duplicate"], "Btn", () => Duplicate(profile)));
            actions.Children.Add(SmallButton(Loc.Inst["common.export"], "Btn", () => Export(profile)));
            actions.Children.Add(SmallButton("×", "Btn.Danger", () => Delete(profile)));
        }
        info.Children.Add(actions);
        stack.Children.Add(info);

        card.Child = stack;
        return card;
    }

    private Button SmallButton(string text, string style, Action action)
    {
        var button = new Button
        {
            Content = text,
            Style = (Style)FindResource(style),
            Height = 27,
            FontSize = 11.5,
            Padding = new Thickness(9, 0, 9, 0),
            Margin = new Thickness(0, 0, 6, 6),
        };
        button.Click += (_, _) => action();
        return button;
    }

    private string DisplayName(HudProfile profile)
    {
        if (profile.Id.StartsWith("preset-", StringComparison.Ordinal))
        {
            string key = "preset." + profile.Id;
            string localized = Loc.Inst[key];
            if (localized != key) return localized;
        }
        return profile.Name;
    }

    private void Edit(HudProfile profile, bool isPreset)
    {
        if (isPreset)
        {
            var copy = _services.Profiles.SaveNewFrom(profile, DisplayName(profile));
            _services.SetActiveProfile(copy);
        }
        else
        {
            _services.SetActiveProfile(profile);
        }
        NavigateRequested?.Invoke("editor");
    }

    private void Duplicate(HudProfile profile)
    {
        var copy = _services.Profiles.SaveNewFrom(profile, DisplayName(profile));
        _services.SetActiveProfile(copy);
        Rebuild();
    }

    private void SetDefault(HudProfile profile)
    {
        _services.SetActiveProfile(profile);
        Rebuild();
    }

    private void Delete(HudProfile profile)
    {
        var result = MessageBox.Show(
            Loc.Inst.Format("profiles.deleteConfirm", profile.Name),
            Loc.Inst["common.delete"],
            MessageBoxButton.OKCancel,
            MessageBoxImage.Warning);
        if (result != MessageBoxResult.OK) return;

        _services.Profiles.Delete(profile.Id);
        if (_services.Settings.Current.ActiveProfileId == profile.Id)
        {
            var fallback = _services.Profiles.DefaultProfile;
            if (fallback is not null) _services.SetActiveProfile(fallback);
        }
        Rebuild();
    }

    private void Export(HudProfile profile)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "Focus profile (*.json)|*.json",
            FileName = SafeFileName(profile.Name) + ".focus.json",
            Title = Loc.Inst["common.export"],
        };
        if (dialog.ShowDialog() != true) return;
        try
        {
            _services.Profiles.Export(profile, dialog.FileName);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, Loc.Inst["msg.error"]);
        }
    }

    private static string SafeFileName(string name)
    {
        foreach (var c in System.IO.Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
        return string.IsNullOrWhiteSpace(name) ? "profile" : name;
    }

    private void OnNewProfile(object sender, RoutedEventArgs e)
    {
        var profile = _services.Profiles.CreateBlank(Loc.Inst["profiles.new"]);
        _services.SetActiveProfile(profile);
        NavigateRequested?.Invoke("editor");
    }

    private void OnImport(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "Focus profile (*.json)|*.json|All files (*.*)|*.*",
            Title = Loc.Inst["profiles.import"],
        };
        if (dialog.ShowDialog() != true) return;
        if (_services.Profiles.Import(dialog.FileName, out var profile, out var error) && profile is not null)
        {
            _services.SetActiveProfile(profile);
            Rebuild();
        }
        else
        {
            MessageBox.Show(error ?? Loc.Inst["msg.invalidProfile"], Loc.Inst["msg.importFailed"]);
        }
    }
}

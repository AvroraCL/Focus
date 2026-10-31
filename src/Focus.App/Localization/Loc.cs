using System.ComponentModel;
using System.Globalization;

namespace Focus.App.Localization;

/// <summary>
/// Minimal runtime localization. XAML binds to the indexer of the static <see cref="Inst"/> instance,
/// so switching language refreshes every bound string without restarting the app.
/// </summary>
public sealed class Loc : INotifyPropertyChanged
{
    public static Loc Inst { get; } = new();

    private string _language = "zh-CN";
    private Dictionary<string, string> _map = Strings.Zh;

    private Loc() { }

    public event PropertyChangedEventHandler? PropertyChanged;
    public event Action? LanguageChanged;

    public string Language => _language;

    public string this[string key]
    {
        get
        {
            if (_map.TryGetValue(key, out var value)) return value;
            if (Strings.En.TryGetValue(key, out var fallback)) return fallback;
            return key;
        }
    }

    public string T(string key) => this[key];

    public string Format(string key, params object[] args)
    {
        try
        {
            return string.Format(CultureInfo.CurrentCulture, this[key], args);
        }
        catch
        {
            return this[key];
        }
    }

    public static string ResolveSystemLanguage()
    {
        var culture = CultureInfo.CurrentUICulture;
        return culture.TwoLetterISOLanguageName.Equals("zh", StringComparison.OrdinalIgnoreCase) ? "zh-CN" : "en-US";
    }

    public void SetLanguage(string language)
    {
        string resolved = language == "system" ? ResolveSystemLanguage() : language;
        if (resolved != "zh-CN" && resolved != "en-US") resolved = ResolveSystemLanguage();
        _language = resolved;
        _map = resolved == "zh-CN" ? Strings.Zh : Strings.En;
        CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.GetCultureInfo(resolved);
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Language)));
        LanguageChanged?.Invoke();
    }
}

public static class LocKeys
{
    public static string Metric(Core.Metrics.MetricId id) => "metric." + id;

    public static string Category(Core.Metrics.MetricCategory category) => category switch
    {
        Core.Metrics.MetricCategory.Game => "category.game",
        Core.Metrics.MetricCategory.Gpu => "category.gpu",
        Core.Metrics.MetricCategory.Cpu => "category.cpu",
        _ => "category.system",
    };

    public static string Anchor(Core.Profiles.HudAnchor anchor) => anchor switch
    {
        Core.Profiles.HudAnchor.TopLeft => "hud.anchor.topLeft",
        Core.Profiles.HudAnchor.TopCenter => "hud.anchor.topCenter",
        Core.Profiles.HudAnchor.TopRight => "hud.anchor.topRight",
        Core.Profiles.HudAnchor.MiddleLeft => "hud.anchor.middleLeft",
        Core.Profiles.HudAnchor.Center => "hud.anchor.center",
        Core.Profiles.HudAnchor.MiddleRight => "hud.anchor.middleRight",
        Core.Profiles.HudAnchor.BottomLeft => "hud.anchor.bottomLeft",
        Core.Profiles.HudAnchor.BottomCenter => "hud.anchor.bottomCenter",
        _ => "hud.anchor.bottomRight",
    };
}

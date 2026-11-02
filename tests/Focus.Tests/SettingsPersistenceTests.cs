using System.IO;
using Focus.App.Services;
using Xunit;

namespace Focus.Tests;

public class SettingsPersistenceTests
{
    [Fact]
    public void SaveReportsFailureAndRecoversPreviousBackup()
    {
        var directory = Path.Combine(Path.GetTempPath(), "focus-settings-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "settings.json");
        try
        {
            var service = new SettingsService(path);
            service.Current.Language = "en-US";
            Assert.True(service.Save());
            service.Current.Language = "zh-CN";
            Assert.True(service.Save());
            bool failed = false;
            service.SaveFailed += _ => failed = true;
            using (var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
                Assert.False(service.Save());
            Assert.True(failed);
            Assert.NotNull(service.LastSaveError);
            File.WriteAllText(path, "invalid json");
            var recovered = new SettingsService(path);
            recovered.Load();
            Assert.Equal("en-US", recovered.Current.Language);
            Assert.True(recovered.Save());
            Assert.Null(recovered.LastSaveError);
        }
        finally { Directory.Delete(directory, true); }
    }
}

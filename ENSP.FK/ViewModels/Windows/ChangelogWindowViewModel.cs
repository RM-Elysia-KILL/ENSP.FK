using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.IO;
using System.Reflection;
using System.Windows;

namespace ENSP.ZD.ViewModels.Windows;

public partial class ChangelogWindowViewModel : ObservableObject
{
    [ObservableProperty]
    private string _changelogContent = string.Empty;

    [ObservableProperty]
    private string _windowTitle = "ENSP.FK 更新公告";

    public ChangelogWindowViewModel()
    {
        LoadChangelog();
    }

    private void LoadChangelog()
    {
        try
        {
            var assembly = Assembly.GetExecutingAssembly();
            using var stream = assembly.GetManifestResourceStream("ENSP.ZD.Prompts.Changelog.md");
            if (stream == null) return;
            using var reader = new StreamReader(stream);
            ChangelogContent = reader.ReadToEnd();
        }
        catch { }
    }

    [RelayCommand]
    private void Close(Window window)
    {
        window?.Close();
    }
}

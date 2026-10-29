using System.ComponentModel;
using YouTubeDL.Services;

namespace YouTubeDL.ViewModels;

public sealed class LocalizedOption : INotifyPropertyChanged
{
    public LocalizedOption(string value, string displayKey)
    {
        Value = value;
        DisplayKey = displayKey;
    }

    public string Value { get; }

    public string DisplayKey { get; }

    public string Display => LocalizationService.Instance.Get(DisplayKey);

    public event PropertyChangedEventHandler? PropertyChanged;

    public void Refresh() =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Display)));
}


using CommunityToolkit.Mvvm.ComponentModel;

namespace ClickBoard.App.ViewModels;

public partial class SnippetVariable(string name) : ObservableObject
{
    public string Name { get; } = name;
    [ObservableProperty] private string _value = string.Empty;
}

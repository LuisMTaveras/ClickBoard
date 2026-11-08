using System.Windows;
using ClickBoard.App.ViewModels;

namespace ClickBoard.App;

public partial class ComparisonView : System.Windows.Controls.UserControl
{
    private readonly LineComparison _comparison;
    public event EventHandler? CloseRequested;
    internal ComparisonView(LineComparison comparison, string leftTitle, string rightTitle)
    {
        InitializeComponent();
        _comparison = comparison;
        LeftTitle.Text = leftTitle; RightTitle.Text = rightTitle;
        Summary.Text = $"{comparison.Removed} líneas eliminadas · {comparison.Added} añadidas. Rojo: origen; verde: destino." +
            (comparison.Simplified ? " Comparación por bloques para clips grandes: se conserva el prefijo y sufijo común; el bloque intermedio se muestra completo." : " Se comparan líneas; los saltos CRLF y LF se consideran equivalentes.");
        Rows.ItemsSource = comparison.Lines;
    }
    private void Back_Click(object sender, RoutedEventArgs e) => CloseRequested?.Invoke(this, EventArgs.Empty);
    private void Filter_Changed(object sender, RoutedEventArgs e)
    {
        if (_comparison is null || Rows is null) return;
        Rows.ItemsSource = ChangesOnly.IsChecked == true ? _comparison.Lines.Where(line => line.Added || line.Removed).ToArray() : _comparison.Lines;
    }
}

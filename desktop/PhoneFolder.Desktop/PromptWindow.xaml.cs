using System.Windows;

namespace PhoneFolder.Desktop;

public partial class PromptWindow : Window
{
    private PromptWindow(string title, string prompt, string initialValue)
    {
        InitializeComponent();
        Title = title;
        PromptText.Text = prompt;
        ValueTextBox.Text = initialValue;
        Loaded += (_, _) =>
        {
            ValueTextBox.Focus();
            ValueTextBox.SelectAll();
        };
    }

    public string Value => ValueTextBox.Text;

    public static string? Show(Window owner, string title, string prompt, string initialValue = "")
    {
        var window = new PromptWindow(title, prompt, initialValue) { Owner = owner };
        return window.ShowDialog() == true ? window.Value : null;
    }

    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }
}

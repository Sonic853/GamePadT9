using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace GamePadT9.TestEditor;
internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        var form = new Window { Title = args.Length > 0 ? Path.GetFileNameWithoutExtension(args[0]) : "GamePadT9 Test Editor", Width = 900, Height = 660 };
        var editor = new TextBox { FontSize = 28, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap };
        form.Content = editor;
        form.ContentRendered += (_, _) => editor.Focus();
        new Application().Run(form);
    }
}

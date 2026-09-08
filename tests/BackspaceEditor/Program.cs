using GamePadT9.TestEditor;

namespace GamePadT9.BackspaceEditor;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        using var window = new Form { Text = Path.GetFileNameWithoutExtension(args[0]), Width = 700, Height = 400 };
        // A real Win32 EDIT control exposes a transitory TSF store on this Windows version.
        using var field = new TextBox { Multiline = true, Dock = DockStyle.Fill, Text = "甲乙ABC", Font = new Font("Microsoft YaHei UI", 22) };
        window.Controls.Add(field);
        window.Shown += (_, _) => {
            Isolation.ActivateProfile(!args.Contains("--xiaobai"));
            field.Focus(); field.SelectionStart = field.TextLength;
            File.WriteAllText(args[0] + ".ready", "ready");
        };
        Application.Run(window);
    }
}

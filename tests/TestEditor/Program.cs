using System.IO;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;

namespace GamePadT9.TestEditor;
internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        var form = new Window { Title = args.Length > 0 ? Path.GetFileNameWithoutExtension(args[0]) : "GamePadT9 Test Editor", Width = 900, Height = 660 };
        var editor = new TextBox { FontSize = 28, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, IsReadOnly = args.Contains("--read-only") };
        form.Content = editor;
        var standalone = args.Contains("--standalone");
        var profileTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(80) };
        if (args.Contains("--report-profile") && args.Length > 0)
        {
            profileTimer.Tick += (_, _) => {
                var destination = args[0] + ".profile.json";
                File.WriteAllText(destination + ".tmp", JsonSerializer.Serialize(Isolation.ReadProfile()));
                File.Move(destination + ".tmp", destination, true);
            };
            profileTimer.Start();
        }
        form.ContentRendered += (_, _) => {
            Isolation.ActivateProfile(standalone); editor.Focus();
            form.Dispatcher.BeginInvoke(new Action(() => {
                var component = Process.GetCurrentProcess().Modules.Cast<ProcessModule>().FirstOrDefault(m =>
                    standalone ? m.ModuleName == "GamePadT9.TextService.dll" :
                    m.ModuleName == "weasel-gamepad.dll" || (m.ModuleName == "weasel.dll" && m.FileName.StartsWith(AppContext.BaseDirectory, StringComparison.OrdinalIgnoreCase)));
                if (args.Length > 0 && component != null) {
                    var hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(component.FileName)));
                    File.WriteAllText(args[0] + ".component.json", JsonSerializer.Serialize(new { path = component.FileName, sha256 = hash }));
                }
                else if (args.Length > 0) File.WriteAllText(args[0] + ".diagnostic.json", JsonSerializer.Serialize(new {
                    is64Bit = Environment.Is64BitProcess,
                    loaded = Process.GetCurrentProcess().Modules.Cast<ProcessModule>().Where(m => m.ModuleName.Contains("weasel", StringComparison.OrdinalIgnoreCase)).Select(m => m.FileName).ToArray()
                }));
            }));
        };
        new Application().Run(form);
        profileTimer.Stop();
    }
}

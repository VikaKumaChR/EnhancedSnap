using System.Drawing;
using System.Windows.Forms;

namespace EnhancedSnap.TestWindow;

/// <summary>
/// EnhancedSnap.TestWindow.exe [title] [x y width height] [hwndFile] [minWidth minHeight] [lifetimeSeconds] —
/// a plain, resizable, topmost window.
/// </summary>
internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        string title = args.Length > 0 ? args[0] : "EnhancedSnap Test Window";
        int[] box = args.Length >= 5 ? args.Skip(1).Take(4).Select(int.Parse).ToArray() : [480, 360, 1200, 760];

        var form = new Form
        {
            Text = title,
            StartPosition = FormStartPosition.Manual,
            Location = new Point(box[0], box[1]),
            Size = new Size(box[2], box[3]),
            BackColor = Color.FromArgb(249, 249, 249),
            TopMost = true,
        };
        form.Controls.Add(new Label
        {
            Text = "Enhanced Snap test window — safe to ignore, it closes by itself.",
            AutoSize = true,
            Location = new Point(28, 28),
            Font = new Font("Segoe UI Variable Display", 14f),
            ForeColor = Color.FromArgb(40, 40, 40),
        });

        // Optional 6th argument: file that receives this window's HWND (test scripts need distinct handles).
        if (args.Length >= 6)
        {
            form.Shown += (_, _) => File.WriteAllText(args[5], form.Handle.ToInt64().ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        // Optional 7th/8th arguments: the window's minimum size (pixels), to test divider limits.
        if (args.Length >= 8)
        {
            form.MinimumSize = new Size(int.Parse(args[6]), int.Parse(args[7]));
        }

        // Safety net: never outlive the test run (optional 9th argument: lifetime in seconds).
        int lifetime = args.Length >= 9 ? int.Parse(args[8]) : 90;
        var timer = new System.Windows.Forms.Timer { Interval = lifetime * 1000 };
        timer.Tick += (_, _) => form.Close();
        timer.Start();
        Application.Run(form);
    }
}

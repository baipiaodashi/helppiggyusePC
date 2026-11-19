using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace PiggyHelper;

public partial class App : Application
{
    // 自动测试用：--screenshot 文件路径 会在界面画好后保存截图并退出
    private string? _screenshotPath;

    private void OnStartup(object sender, StartupEventArgs e)
    {
        string? startPage = null, query = null;
        for (int i = 0; i < e.Args.Length - 1; i++)
        {
            switch (e.Args[i])
            {
                case "--page": startPage = e.Args[i + 1]; break;
                case "--query": query = e.Args[i + 1]; break;
                case "--screenshot": _screenshotPath = e.Args[i + 1]; break;
            }
        }

        var window = new MainWindow();
        window.Show();
        if (startPage != null) window.Navigate(startPage, query);

        if (_screenshotPath != null)
        {
            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                SaveScreenshot(window, _screenshotPath);
                Shutdown(0);
            };
            timer.Start();
        }
    }

    private static void SaveScreenshot(Window window, string path)
    {
        if (window.Content is not FrameworkElement root) return;
        var dpi = VisualTreeHelper.GetDpi(root);
        var bmp = new RenderTargetBitmap(
            (int)Math.Ceiling(root.ActualWidth * dpi.DpiScaleX),
            (int)Math.Ceiling(root.ActualHeight * dpi.DpiScaleY),
            dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
        // 先铺上窗口背景色，再直接画界面（不缩放）
        var bg = new DrawingVisual();
        using (var dc = bg.RenderOpen())
            dc.DrawRectangle(window.Background, null, new Rect(0, 0, root.ActualWidth, root.ActualHeight));
        bmp.Render(bg);
        bmp.Render(root);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bmp));
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        using var fs = File.Create(path);
        encoder.Save(fs);
    }

    private void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        if (_screenshotPath != null)
        {
            // 自动测试时不弹窗，直接报错退出
            Console.Error.WriteLine(e.Exception);
            File.WriteAllText(_screenshotPath + ".error.txt", e.Exception.ToString());
            e.Handled = true;
            Shutdown(1);
            return;
        }
        MessageBox.Show("哎呀，小猪遇到了一点问题：\n\n" + e.Exception.Message,
            "小猪电脑助手", MessageBoxButton.OK, MessageBoxImage.Warning);
        e.Handled = true;
    }
}

using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;

namespace PiggyHelper.Controls;

public partial class PiggyMascot : System.Windows.Controls.UserControl
{
    private static readonly Dictionary<string, ImageSource> Cache = new();

    public static readonly DependencyProperty PoseProperty = DependencyProperty.Register(
        nameof(Pose), typeof(string), typeof(PiggyMascot), new PropertyMetadata("wave", (d, _) => ((PiggyMascot)d).UpdateImage()));

    public static readonly DependencyProperty AnimatedProperty = DependencyProperty.Register(
        nameof(Animated), typeof(bool), typeof(PiggyMascot), new PropertyMetadata(true));

    /// <summary>小猪的动作：wave 招手、cheer 欢呼、broom 扫地、think 思考、search 放大镜、key 举键帽、face 只有脑袋、sleep 睡觉、clip 拿着剪贴板。</summary>
    public string Pose { get => (string)GetValue(PoseProperty); set => SetValue(PoseProperty, value); }

    /// <summary>是否轻轻上下晃动。</summary>
    public bool Animated { get => (bool)GetValue(AnimatedProperty); set => SetValue(AnimatedProperty, value); }

    public PiggyMascot()
    {
        InitializeComponent();
        UpdateImage();
        Loaded += (_, _) =>
        {
            if (!Animated) return;
            var bob = new DoubleAnimation(0, -5, TimeSpan.FromSeconds(1.2))
            {
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever,
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
            };
            Timeline.SetDesiredFrameRate(bob, 30); // 慢悠悠的晃动不需要高帧率，省电
            Bob.BeginAnimation(TranslateTransform.YProperty, bob);
        };
        Unloaded += (_, _) => Bob.BeginAnimation(TranslateTransform.YProperty, null);
    }

    private void UpdateImage() => Img.Source = Load(Pose);

    public static ImageSource Load(string? pose)
    {
        pose = string.IsNullOrWhiteSpace(pose) ? "wave" : pose;
        if (Cache.TryGetValue(pose, out var img)) return img;
        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.UriSource = new Uri($"pack://application:,,,/Assets/pig/{pose}.png");
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.EndInit();
            bmp.Freeze();
            img = bmp;
        }
        catch
        {
            img = pose == "wave" ? new DrawingImage() : Load("wave");
        }
        Cache[pose] = img;
        return img;
    }
}

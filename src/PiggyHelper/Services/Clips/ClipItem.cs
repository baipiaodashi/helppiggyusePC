using System.Text.Json.Serialization;

namespace PiggyHelper.Services.Clips;

/// <summary>剪贴板记录的种类。</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ClipKind { Text, Image, Files }

/// <summary>小猪剪贴板里的分类标签。</summary>
public enum ClipFilter { All, Favorite, Text, Image, Files }

/// <summary>小猪剪贴板里的一条记录：复制过的一段文字、一张图片，或者几个文件。</summary>
public sealed class ClipItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public ClipKind Kind { get; set; }

    /// <summary>文字的小分类：link 网址、email 邮箱、color 颜色、path 文件路径；普通文字为空。</summary>
    public string? SubKind { get; set; }

    /// <summary>纯文字内容。文件记录里是每行一个文件路径。</summary>
    public string Text { get; set; } = "";

    /// <summary>从网页、Word 复制时带的格式，粘贴时能保留字体、颜色和表格。</summary>
    public string? Html { get; set; }

    public string? Rtf { get; set; }

    /// <summary>图片文件名：images 文件夹里的"指纹.png"。</summary>
    public string? Image { get; set; }

    public int Width { get; set; }

    public int Height { get; set; }

    public List<string>? Files { get; set; }

    /// <summary>内容指纹：一模一样的内容只保存一条。</summary>
    public string Hash { get; set; } = "";

    /// <summary>内容大小（字节）。</summary>
    public long Size { get; set; }

    /// <summary>从哪个软件复制的（exe 完整路径）。</summary>
    public string? SourcePath { get; set; }

    public string? SourceName { get; set; }

    /// <summary>第一次复制的时间。</summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>最近一次复制或粘贴的时间，列表按它从新到旧排。</summary>
    public DateTime UsedAt { get; set; }

    public int UseCount { get; set; } = 1;

    /// <summary>收藏：一直保存，清空时也不删。</summary>
    public bool Favorite { get; set; }

    /// <summary>可能是密码、证件号：列表里打码显示，保存到硬盘时加密。</summary>
    public bool Sensitive { get; set; }

    /// <summary>只在硬盘上的文件里用：敏感内容加密后放在这里，Text/Html/Rtf 留空。</summary>
    public string? Secret { get; set; }

    /// <summary>复制一份（保存到硬盘时用，免得后台写文件时列表正好在改）。</summary>
    public ClipItem Clone() => (ClipItem)MemberwiseClone();
}

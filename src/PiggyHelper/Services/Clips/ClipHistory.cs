namespace PiggyHelper.Services.Clips;

/// <summary>
/// 剪贴板记录列表（只管内存里的数据，不碰硬盘和系统剪贴板，方便单独测试）。
/// 列表永远按"最近用过"从新到旧排；一模一样的内容只留一条。
/// </summary>
public sealed class ClipHistory
{
    private readonly List<ClipItem> _items = new();

    /// <summary>从新到旧。</summary>
    public IReadOnlyList<ClipItem> Items => _items;

    public int Count => _items.Count;

    public ClipItem? Find(string id) => _items.Find(x => x.Id == id);

    public ClipItem? FindByHash(string hash) => _items.Find(x => x.Hash == hash);

    /// <summary>读取保存的记录时用：整体换掉，并按使用时间重新排好。</summary>
    public void Load(IEnumerable<ClipItem> items)
    {
        _items.Clear();
        var seen = new HashSet<string>();
        foreach (var it in items.OrderByDescending(x => x.UsedAt))
        {
            if (string.IsNullOrEmpty(it.Hash) || !seen.Add(it.Hash)) continue;
            _items.Add(it);
        }
    }

    /// <summary>
    /// 新复制了一样东西：没见过就放到最上面；见过就把原来那条挪到最上面，次数 +1，
    /// 并换成这次带的格式和来源。返回列表里实际的那一条，以及它是不是新的。
    /// </summary>
    public (ClipItem Item, bool IsNew) Add(ClipItem item, DateTime now)
    {
        int idx = _items.FindIndex(x => x.Hash == item.Hash);
        if (idx < 0)
        {
            if (item.CreatedAt == default) item.CreatedAt = now;
            item.UsedAt = now;
            _items.Insert(0, item);
            return (item, true);
        }

        var old = _items[idx];
        old.UsedAt = now;
        old.UseCount++;
        if (item.Kind == ClipKind.Text)
        {
            old.Html = item.Html;
            old.Rtf = item.Rtf;
            old.SubKind = item.SubKind;
        }
        old.Sensitive |= item.Sensitive;
        if (item.SourcePath != null)
        {
            old.SourcePath = item.SourcePath;
            old.SourceName = item.SourceName;
        }
        MoveToTop(idx);
        return (old, false);
    }

    /// <summary>粘贴过一次：挪到最上面，次数 +1。</summary>
    public bool Touch(string id, DateTime now)
    {
        int idx = _items.FindIndex(x => x.Id == id);
        if (idx < 0) return false;
        _items[idx].UsedAt = now;
        _items[idx].UseCount++;
        MoveToTop(idx);
        return true;
    }

    public ClipItem? Remove(string id)
    {
        int idx = _items.FindIndex(x => x.Id == id);
        if (idx < 0) return null;
        var it = _items[idx];
        _items.RemoveAt(idx);
        return it;
    }

    public bool SetFavorite(string id, bool favorite)
    {
        var it = Find(id);
        if (it == null || it.Favorite == favorite) return false;
        it.Favorite = favorite;
        return true;
    }

    /// <summary>清空记录。收藏的默认留着。返回被删掉的记录（好去删对应的图片文件）。</summary>
    public List<ClipItem> Clear(bool keepFavorites = true)
    {
        var removed = _items.Where(x => !keepFavorites || !x.Favorite).ToList();
        _items.RemoveAll(x => !keepFavorites || !x.Favorite);
        return removed;
    }

    /// <summary>
    /// 按设置删掉旧记录：超过 maxItems 条的、超过 keepDays 天没用过的（keepDays 为 0 表示一直留着）。
    /// 收藏的不算在条数里，也永远不删。
    /// </summary>
    public List<ClipItem> Prune(int maxItems, int keepDays, DateTime now)
    {
        var removed = new List<ClipItem>();
        var cutoff = keepDays > 0 ? now.AddDays(-keepDays) : DateTime.MinValue;
        int kept = 0;
        for (int i = 0; i < _items.Count; i++)
        {
            var it = _items[i];
            if (it.Favorite) continue;
            if (it.UsedAt < cutoff || kept >= Math.Max(1, maxItems))
            {
                removed.Add(it);
                _items.RemoveAt(i);
                i--;
                continue;
            }
            kept++;
        }
        return removed;
    }

    /// <summary>按分类和搜索词筛选。搜索词用空格分开时，每个词都要出现（不分大小写）。</summary>
    public List<ClipItem> Query(ClipFilter filter, string? search)
    {
        var words = (search ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        var result = new List<ClipItem>();
        foreach (var it in _items)
        {
            if (!Matches(it, filter)) continue;
            if (words.Length > 0 && !words.All(w => Contains(it, w))) continue;
            result.Add(it);
        }
        return result;
    }

    /// <summary>每个分类有几条，标签上显示用。</summary>
    public int CountOf(ClipFilter filter) => filter == ClipFilter.All ? _items.Count : _items.Count(x => Matches(x, filter));

    /// <summary>还有记录在用的图片文件名（剩下的图片文件可以删掉）。</summary>
    public HashSet<string> ImagesInUse()
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var it in _items)
            if (!string.IsNullOrEmpty(it.Image)) set.Add(it.Image);
        return set;
    }

    private static bool Matches(ClipItem it, ClipFilter filter) => filter switch
    {
        ClipFilter.Favorite => it.Favorite,
        ClipFilter.Text => it.Kind == ClipKind.Text,
        ClipFilter.Image => it.Kind == ClipKind.Image,
        ClipFilter.Files => it.Kind == ClipKind.Files,
        _ => true,
    };

    private static bool Contains(ClipItem it, string word)
    {
        if (it.Text.Contains(word, StringComparison.OrdinalIgnoreCase)) return true;
        if (it.SourceName?.Contains(word, StringComparison.OrdinalIgnoreCase) == true) return true;
        return it.Kind switch
        {
            ClipKind.Image => word is "图片" or "截图" or "图",
            ClipKind.Files => word is "文件" or "文件夹",
            _ => it.SubKind switch
            {
                "link" => word is "网址" or "链接",
                "email" => word is "邮箱",
                "color" => word is "颜色",
                "path" => word is "路径",
                _ => false,
            },
        };
    }

    private void MoveToTop(int idx)
    {
        if (idx == 0) return;
        var it = _items[idx];
        _items.RemoveAt(idx);
        _items.Insert(0, it);
    }
}

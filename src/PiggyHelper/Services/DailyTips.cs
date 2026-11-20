namespace PiggyHelper.Services;

public static class DailyTips
{
    private static readonly string[] Tips =
    {
        "按 Win + D 可以一下子回到桌面，再按一次就回来啦。",
        "按 Win + Shift + S 可以截图，截完按 Ctrl + V 就能粘贴到微信里。",
        "按 Ctrl + Z 可以撤销刚才的操作，删错字、移错文件都能救回来。",
        "按 Alt + Tab 可以在打开的窗口之间快速切换。",
        "按 Win + L 可以马上锁屏，离开座位时用它保护隐私。",
        "按 Win + E 可以打开“此电脑”，找文件更方便。",
        "按 Ctrl + F 可以在网页或文档里查找文字。",
        "软件卡死了？按 Ctrl + Shift + Esc 打开任务管理器，把它结束掉。",
        "按 Win + V 可以打开小猪剪贴板，之前复制过的文字、图片、文件都在里面，点一下就能粘贴。",
        "常用的一段话（比如学号、地址）复制一次，在小猪剪贴板里点星星收藏，以后按 Win + V 随时能粘贴。",
        "按 Ctrl + 鼠标滚轮 可以放大或缩小网页上的字。",
        "下载软件尽量去官网，别点“高速下载”按钮，那通常会装一堆广告软件。",
        "按 Win + . （句号）可以打开表情符号面板。",
        "按 Ctrl + Shift + T 可以重新打开浏览器里刚关掉的网页。",
        "文件误删了先别慌，去回收站里右键“还原”就能找回来。",
        "按 F2 可以给选中的文件改名字。",
    };

    public static string Random() => Tips[System.Random.Shared.Next(Tips.Length)];
}

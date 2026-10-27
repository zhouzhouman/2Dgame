using System;
using System.Collections.Generic;

namespace Juhuo.Application;

/// <summary>
/// 玩家进度（跨启动保留）。刻意不引用 Godot —— 这一层只认字符串，
/// 存到哪个文件由表现层决定（现在是 user://progress.cfg）。
///
/// 目前只记三件事：新手引导看过没有、上次停在哪、一共开过几次。
/// 存档要扩的时候，就在这里加字段，别把状态散到各个节点里。
/// </summary>
public sealed class Progress
{
    public bool TutorialDone { get; set; }
    public string LastHub { get; set; } = "hub1";
    public int Launches { get; set; }

    /// <summary>读出"看过引导"，所以启动时不再挡人。</summary>
    public static Progress Parse(string text)
    {
        var p = new Progress();

        foreach (var line in text.Replace("\r", "").Split('\n'))
        {
            var i = line.IndexOf('=');
            if (i <= 0) continue;

            var key = line[..i].Trim();
            var val = line[(i + 1)..].Trim();

            switch (key)
            {
                case "tutorial_done":
                    p.TutorialDone = val is "1" or "true";
                    break;
                case "last_hub":
                    if (val.Length > 0) p.LastHub = val;
                    break;
                case "launches":
                    if (int.TryParse(val, out var n)) p.Launches = n;
                    break;
            }
        }

        return p;
    }

    public string Serialize()
    {
        var lines = new List<string>
        {
            "# 举火 · 本地进度。删掉这个文件等于全新开始。",
            $"tutorial_done={(TutorialDone ? 1 : 0)}",
            $"last_hub={LastHub}",
            $"launches={Launches}",
        };

        return string.Join("\n", lines) + "\n";
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using Juhuo.Application;
using Juhuo.Core;
using Xunit;

namespace Juhuo.Core.Tests;

/// <summary>
/// 主菜单（S3-00）。玩家报的 bug 是"启动没有菜单、直接进旧档、教程不弹"，
/// 所以这里的每一条都盯着一件会真的发生在玩家身上的事：
/// 默认停在哪、灰着的项能不能被选中、「新的开始」是不是真的把进度与旧档都让开。
///
/// 只验 Application 那一段：菜单模型 + 启动流程。世界怎么造是 Game 层的事，
/// 但"新局的起点"可以用 <see cref="SaveState"/> 的默认值说清楚。
/// </summary>
public class BootMenuTests
{
    /// <summary>内存里的文件系统，跟存档自检那个是同一套形状。</summary>
    private sealed class FakeFs
    {
        public readonly Dictionary<string, string> Files = new(StringComparer.Ordinal);

        public SaveFileSystem Build() => new()
        {
            Exists = p => Files.ContainsKey(p),
            Read = p => Files.TryGetValue(p, out var t) ? t : null,
            Write = (p, t) => Files[p] = t,
            Copy = (from, to) =>
            {
                if (!Files.TryGetValue(from, out var t)) throw new System.IO.FileNotFoundException(from);
                Files[to] = t;
            },
            Move = (from, to) =>
            {
                if (!Files.TryGetValue(from, out var t)) throw new System.IO.FileNotFoundException(from);
                Files[to] = t;
                Files.Remove(from);
            },
            Delete = p => Files.Remove(p),
            Now = () => new DateTime(2026, 11, 3, 9, 0, 0),
        };
    }

    /// <summary>一份"上一局停在这"的真档。</summary>
    private static SaveState 旧档()
    {
        var s = new SaveState { Hub = "hub7", Day = 1 };
        s.Flags["tutorial_seen"] = true;
        return s;
    }

    // ————————————————————————— 菜单模型 —————————————————————————

    [Fact]
    public void 三项文案与顺序_新的开始_继续_退出()
    {
        var menu = new BootMenu(hasSave: true);

        Assert.Equal(BootMenu.Count, 3);
        Assert.Equal(("新的开始", true), menu.Line(0));
        Assert.Equal(("继续", true), menu.Line(1));
        Assert.Equal(("退出", true), menu.Line(2));
    }

    [Fact]
    public void 无存档时_继续是灰的_默认停在新的开始()
    {
        var menu = new BootMenu(hasSave: false);

        Assert.False(menu.ContinueEnabled);
        Assert.Equal(BootChoice.NewGame, menu.Selected);
        Assert.Equal(0, menu.SelectedIndex);
        Assert.False(menu.Line(1).Enabled);
    }

    [Fact]
    public void 有存档时_继续是亮的_默认停在继续()
    {
        var menu = new BootMenu(hasSave: true);

        Assert.True(menu.ContinueEnabled);
        Assert.Equal(BootChoice.Continue, menu.Selected);
        Assert.Equal(1, menu.SelectedIndex);
        Assert.True(menu.Line(1).Enabled);
    }

    [Fact]
    public void 坏档视同无档_继续是灰的_默认新的开始()
    {
        var menu = new BootMenu(hasSave: true, saveBroken: true);

        Assert.False(menu.ContinueEnabled);
        Assert.Equal(BootChoice.NewGame, menu.Selected);
    }

    [Fact]
    public void 无档时往下走_跳过灰的继续_直接到退出()
    {
        var menu = new BootMenu(hasSave: false);

        menu.MoveDown();

        Assert.Equal(BootChoice.Quit, menu.Selected);
        Assert.Equal(2, menu.SelectedIndex);
    }

    [Fact]
    public void 无档时往上走_跳过灰的继续_直接回新的开始()
    {
        var menu = new BootMenu(hasSave: false);
        menu.Select(BootChoice.Quit);

        menu.MoveUp();

        Assert.Equal(BootChoice.NewGame, menu.Selected);
    }

    [Fact]
    public void 有档时上下走_三项都在队列里()
    {
        var menu = new BootMenu(hasSave: true);

        menu.MoveUp();
        Assert.Equal(BootChoice.NewGame, menu.Selected);

        menu.MoveDown();
        Assert.Equal(BootChoice.Continue, menu.Selected);

        menu.MoveDown();
        Assert.Equal(BootChoice.Quit, menu.Selected);

        // 到底再往下：绕回第一项
        menu.MoveDown();
        Assert.Equal(BootChoice.NewGame, menu.Selected);
    }

    [Fact]
    public void 灰的项选不上_保持原样()
    {
        var menu = new BootMenu(hasSave: false);

        menu.Select(BootChoice.Continue);

        Assert.Equal(BootChoice.NewGame, menu.Selected);
    }

    [Fact]
    public void Line_越界给空行()
    {
        var menu = new BootMenu(hasSave: true);

        Assert.Equal(("", false), menu.Line(-1));
        Assert.Equal(("", false), menu.Line(BootMenu.Count));
    }

    [Fact]
    public void Confirm_回的就是停着的那一项()
    {
        var menu = new BootMenu(hasSave: false);
        menu.MoveDown();

        Assert.Equal(BootChoice.Quit, menu.Confirm());
    }

    // ————————————————————————— 「新的开始」 —————————————————————————

    [Fact]
    public void 新的开始_教程重置并且落盘()
    {
        var fs = new FakeFs();
        fs.Files[SaveService.SavePath] = 旧档().ToJson();

        var progress = new Progress { TutorialDone = true, LastHub = "hub7", Launches = 9 };
        string? written = null;

        var flow = new BootFlow(new SaveService(fs.Build()), p => written = p.Serialize());
        var outcome = flow.Choose(BootChoice.NewGame, progress);

        Assert.Equal(BootAction.NewGame, outcome.Action);
        Assert.True(outcome.Ok);
        Assert.False(progress.TutorialDone);
        Assert.Equal("", progress.LastHub);

        // 落盘的那一份也得是重置后的：不然下次启动照样判"教程已看完"
        Assert.NotNull(written);
        var back = Progress.Parse(written!);
        Assert.False(back.TutorialDone);

        // 清空的 last_hub 读回来是 hub1：新局就从那里起
        Assert.Equal("hub1", back.LastHub);
    }

    [Fact]
    public void 新的开始_旧档改名到save_pre_boot_而不是删掉()
    {
        var fs = new FakeFs();
        fs.Files[SaveService.SavePath] = 旧档().ToJson();

        var flow = new BootFlow(new SaveService(fs.Build()), _ => { });
        var outcome = flow.Choose(BootChoice.NewGame, new Progress { TutorialDone = true, LastHub = "hub7" });

        Assert.Equal(SaveService.PreBootPath, outcome.ShelvedTo);
        Assert.False(fs.Files.ContainsKey(SaveService.SavePath));
        Assert.True(fs.Files.ContainsKey(SaveService.PreBootPath));

        // 挪走的那一份还能读出来（可恢复），内容一字不改
        var shelved = SaveState.FromJson(fs.Files[SaveService.PreBootPath]);
        Assert.Equal("hub7", shelved.Hub);
    }

    [Fact]
    public void 新的开始_备份也一起挪开_不会在坏档时把上一局捞回来()
    {
        var fs = new FakeFs();
        fs.Files[SaveService.SavePath] = 旧档().ToJson();
        fs.Files[SaveService.BackupPath] = 旧档().ToJson();

        var flow = new BootFlow(new SaveService(fs.Build()), _ => { });
        flow.Choose(BootChoice.NewGame, new Progress());

        Assert.False(fs.Files.ContainsKey(SaveService.BackupPath));
        Assert.True(fs.Files.ContainsKey(SaveService.PreBootBackupPath));

        // 新局读档：没有档（不是从备份里捞回旧档）
        var result = new SaveService(fs.Build()).LoadOrRecover();
        Assert.Equal(SaveOrigin.None, result.Origin);
        Assert.Null(result.State);
    }

    [Fact]
    public void 新的开始之后_新局的起点是hub1第1天()
    {
        var fs = new FakeFs();
        fs.Files[SaveService.SavePath] = 旧档().ToJson();

        new BootFlow(new SaveService(fs.Build()), _ => { })
            .Choose(BootChoice.NewGame, new Progress { TutorialDone = true, LastHub = "hub7" });

        // 没有档 = 世界从默认值起跑；默认值就是剧本第一处与第一天
        var fresh = new SaveState();
        Assert.Equal("hub1", fresh.Hub);
        Assert.Equal(1, fresh.Day);
        Assert.Empty(fresh.Inventory);
        Assert.Equal(0, fresh.Prologue.Troops);
    }

    // ————————————————————————— 「继续」与「退出」 —————————————————————————

    [Fact]
    public void 继续_不动教程也不写文件()
    {
        var fs = new FakeFs();
        fs.Files[SaveService.SavePath] = 旧档().ToJson();
        var before = new Dictionary<string, string>(fs.Files, StringComparer.Ordinal);

        var progress = new Progress { TutorialDone = true, LastHub = "hub7" };
        var outcome = new BootFlow(new SaveService(fs.Build()), _ => throw new Xunit.Sdk.XunitException("继续不该写进度"))
            .Choose(BootChoice.Continue, progress);

        Assert.Equal(BootAction.Continue, outcome.Action);
        Assert.True(progress.TutorialDone);
        Assert.Equal("hub7", progress.LastHub);
        Assert.Equal(before, fs.Files);
    }

    [Fact]
    public void 退出_一个文件都不写()
    {
        var fs = new FakeFs();
        fs.Files[SaveService.SavePath] = 旧档().ToJson();
        var before = new Dictionary<string, string>(fs.Files, StringComparer.Ordinal);

        var outcome = new BootFlow(new SaveService(fs.Build()), _ => throw new Xunit.Sdk.XunitException("退出不该写进度"))
            .Choose(BootChoice.Quit, new Progress { TutorialDone = true, LastHub = "hub7" });

        Assert.Equal(BootAction.Stay, outcome.Action);
        Assert.Equal(before, fs.Files);
    }

    // ————————————————————————— --bootmenu 自检 —————————————————————————

    [Fact]
    public void Bootmenu自检_三行文案对得上()
    {
        var lines = BootMenuSelfCheck.Lines();

        Assert.Equal(3, lines.Length);
        Assert.Equal("[菜单] 无存档：默认「新的开始」，继续=灰", lines[0]);
        Assert.Equal("[菜单] 有存档：默认「继续」，继续=亮", lines[1]);
        Assert.Equal("[菜单] 选新的开始 → 教程重置，存档改名 save.pre-boot.json", lines[2]);
    }

    [Fact]
    public void Bootmenu自检不碰内存以外的东西_连跑两遍结果一样()
    {
        var first = BootMenuSelfCheck.Lines();
        var second = BootMenuSelfCheck.Lines();

        Assert.Equal(first, second);
        Assert.All(first, l => Assert.StartsWith("[菜单]", l));
    }
}

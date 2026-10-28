using System;
using Godot;
using Juhuo.Application;

namespace Juhuo;

/// <summary>
/// 存档文件的真实读写。
///
/// 为什么不用 System.IO：Godot 的 <c>user://</c> 不是操作系统路径，
/// <c>File.*</c> / <c>Directory.*</c> 处理不了它；
/// 而 Application 那一层又不许引用 Godot。
/// 所以文件动作由这里包一份，交给 <see cref="SaveService"/> 用。
/// </summary>
public static class GodotSaveFileSystem
{
    /// <summary>造一份给 SaveService 用的文件动作。</summary>
    /// <returns>委托集合。</returns>
    public static SaveFileSystem Create() => new()
    {
        Exists = path => FileAccess.FileExists(path),

        Read = path => FileAccess.FileExists(path)
            ? FileAccess.GetFileAsString(path)
            : null,

        Write = (path, text) =>
        {
            using var f = FileAccess.Open(path, FileAccess.ModeFlags.Write);
            if (f == null) throw new System.IO.IOException($"打不开 {path}（{FileAccess.GetOpenError()}）。");

            f.StoreString(text);
            f.Flush();
        },

        Copy = (from, to) =>
        {
            var text = FileAccess.GetFileAsString(from);

            using var f = FileAccess.Open(to, FileAccess.ModeFlags.Write);
            if (f == null) throw new System.IO.IOException($"打不开 {to}（{FileAccess.GetOpenError()}）。");

            f.StoreString(text);
            f.Flush();
        },

        Move = (from, to) =>
        {
            // 同卷改名才叫原子。Godot 的改名不覆盖已经存在的目标，
            // 所以先直接改；不行才把旧的挪走再改一次。
            // 旧的那一份此刻已经在 save.bak 里，删得起。
            var err = DirAccess.RenameAbsolute(from, to);
            if (err == Error.Ok) return;

            if (!FileAccess.FileExists(to))
                throw new System.IO.IOException($"改名失败（{err}）。");

            if (DirAccess.RemoveAbsolute(to) != Error.Ok)
                throw new System.IO.IOException($"旧文件挪不走（{err}）。");

            err = DirAccess.RenameAbsolute(from, to);
            if (err != Error.Ok) throw new System.IO.IOException($"改名失败（{err}）。");
        },

        Delete = path => DirAccess.RemoveAbsolute(path),

        Now = () => DateTime.Now,
    };
}

using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Juhuo.Core;

/// <summary>
/// 旗标仓库：两层作用域——全局一层，每个空间一层。
/// 取值只有一条规则：全局有键就取全局的，没有就看空间的局部，都没有就是 false。
/// 规则源：Docs/Rules/rule_flags_save.md §1 命名、§1.4 作用域、§边界。
/// </summary>
public sealed class FlagStore
{
    /// <summary>旗标名长度上限（字节）。</summary>
    public const int MaxNameBytes = 40;

    /// <summary>单档全局旗标数上限。</summary>
    public const int MaxGlobalFlags = 512;

    /// <summary>单个空间的局部旗标数上限。</summary>
    public const int MaxLocalFlagsPerHub = 128;

    // 新式三/四段命名：hub3_well_shoe_taken（rule_flags_save.md §1.1，只用下划线）。
    private static readonly Regex NewStylePattern = new(@"^[a-z][a-z0-9]*(_[a-z0-9]+){1,3}$", RegexOptions.Compiled);

    // 票面写法：域.对象.状态。规则卡 §1.1 说不用点号，这里两种都认，写入时以宽松为准。
    private static readonly Regex DotStylePattern = new(@"^[a-z][a-z0-9]*(\.[a-z0-9]+){2,3}$", RegexOptions.Compiled);

    // 已知空间：序章 hub1–hub7，正篇 reg_*（schema_v2.md §A.1 / §B.3）。
    private static readonly Regex KnownHubPattern = new(@"^(hub[1-7]|reg_[a-z0-9_]+)$", RegexOptions.Compiled);

    // 冻结的旧式旗标名（rule_flags_save.md §1.3）：不改名，也不报错。
    private static readonly HashSet<string> FrozenNames = new(StringComparer.Ordinal)
    {
        "has_shoe", "knows_shoe_inward", "knows_lu_wenzhao", "knows_11_banner",
        "intel_seal_order", "has_letter", "has_deed", "has_medicine", "has_ash",
        "took_temple_coin", "knows_herb_path", "path_backhill_open",
    };

    private readonly Dictionary<string, bool> _global = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Dictionary<string, bool>> _local = new(StringComparer.Ordinal);

    /// <summary>同一个名字同时出现在全局与局部时触发（开发期警告钩子，正式构建可留空）。</summary>
    public static Action<string>? OnConflictWarning { get; set; }

    /// <summary>全局旗标数。</summary>
    public int GlobalCount => _global.Count;

    /// <summary>
    /// 读一个旗标。全局有键就用全局的（哪怕全局写的是 false），全局没有才看空间的局部。
    /// 缺失、null、空串、未知 hub 一律 false。
    /// </summary>
    /// <param name="name">旗标名。</param>
    /// <param name="hub">当前空间 id。</param>
    /// <returns>成立返回 true，其余一律 false。</returns>
    public bool Has(string? name, string? hub)
    {
        if (string.IsNullOrEmpty(name)) return false;
        if (!IsKnownHub(hub)) return false;

        if (_global.TryGetValue(name!, out var g)) return g;

        return _local.TryGetValue(hub!, out var scope) && scope.TryGetValue(name!, out var v) && v;
    }

    /// <summary>
    /// 置一个全局旗标。重复置位是空操作，键数不变。
    /// </summary>
    /// <param name="name">旗标名。</param>
    /// <returns>是否写入成功。</returns>
    public bool Set(string? name)
    {
        if (!CanWrite(name)) return false;

        if (_global.ContainsKey(name!)) return true;
        if (_global.Count >= MaxGlobalFlags) return false;

        _global[name!] = true;

        foreach (var scope in _local.Values)
            if (scope.ContainsKey(name!))
            {
                OnConflictWarning?.Invoke(name!);
                break;
            }

        return true;
    }

    /// <summary>
    /// 置一个空间内的局部旗标。重复置位是空操作。
    /// </summary>
    /// <param name="name">旗标名。</param>
    /// <param name="hub">空间 id。</param>
    /// <returns>是否写入成功。</returns>
    public bool SetLocal(string? name, string? hub)
    {
        if (!CanWrite(name)) return false;
        if (!IsKnownHub(hub)) return false;

        if (!_local.TryGetValue(hub!, out var scope))
        {
            scope = new Dictionary<string, bool>(StringComparer.Ordinal);
            _local[hub!] = scope;
        }

        if (scope.ContainsKey(name!)) return true;
        if (scope.Count >= MaxLocalFlagsPerHub) return false;

        scope[name!] = true;
        if (_global.ContainsKey(name!)) OnConflictWarning?.Invoke(name!);

        return true;
    }

    /// <summary>
    /// 清掉一个旗标：全局与所有空间的局部都清掉，保证读完就是 false。
    /// </summary>
    /// <param name="name">旗标名。</param>
    /// <returns>原来有没有这个键。</returns>
    public bool Clear(string? name)
    {
        if (string.IsNullOrEmpty(name)) return false;

        var existed = _global.Remove(name!);

        foreach (var scope in _local.Values)
            if (scope.Remove(name!)) existed = true;

        return existed;
    }

    /// <summary>
    /// 只清掉一个空间内的局部旗标。
    /// </summary>
    /// <param name="name">旗标名。</param>
    /// <param name="hub">空间 id。</param>
    /// <returns>原来有没有这个键。</returns>
    public bool ClearLocal(string? name, string? hub)
    {
        if (string.IsNullOrEmpty(name)) return false;
        if (!IsKnownHub(hub)) return false;

        if (!_local.TryGetValue(hub!, out var scope)) return false;
        return scope.Remove(name!);
    }

    /// <summary>
    /// 导出全局作用域的快照（副本，改它不影响仓库）。
    /// </summary>
    /// <returns>全局旗标副本。</returns>
    public IReadOnlyDictionary<string, bool> SnapshotGlobal() => new Dictionary<string, bool>(_global, StringComparer.Ordinal);

    /// <summary>
    /// 导出某个空间的局部作用域快照（副本）。
    /// </summary>
    /// <param name="hub">空间 id。</param>
    /// <returns>局部旗标副本；空间未知返回空表。</returns>
    public IReadOnlyDictionary<string, bool> SnapshotLocal(string? hub)
    {
        if (!IsKnownHub(hub)) return new Dictionary<string, bool>(StringComparer.Ordinal);
        if (!_local.TryGetValue(hub!, out var scope)) return new Dictionary<string, bool>(StringComparer.Ordinal);

        return new Dictionary<string, bool>(scope, StringComparer.Ordinal);
    }

    /// <summary>
    /// 用存档里的旗标整体覆盖当前内容。
    /// </summary>
    /// <param name="global">全局作用域旗标；null 视为空。</param>
    /// <param name="locals">各空间的局部旗标；null 视为空。</param>
    public void Load(
        IReadOnlyDictionary<string, bool>? global,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, bool>>? locals = null)
    {
        _global.Clear();
        _local.Clear();

        if (global != null)
            foreach (var kv in global)
                if (CanWrite(kv.Key))
                    _global[kv.Key] = kv.Value;

        if (locals == null) return;

        foreach (var hub in locals)
        {
            if (!IsKnownHub(hub.Key)) continue;

            var scope = new Dictionary<string, bool>(StringComparer.Ordinal);
            foreach (var kv in hub.Value)
                if (CanWrite(kv.Key))
                    scope[kv.Key] = kv.Value;

            if (scope.Count > 0) _local[hub.Key] = scope;
        }
    }

    /// <summary>
    /// 用一整个存档对象里的旗标覆盖当前内容：全局取 <c>flags</c>，局部取各 <c>hubs.*.flags</c>。
    /// </summary>
    /// <param name="state">存档对象。</param>
    public void Load(SaveState? state)
    {
        if (state == null)
        {
            Load(null, null);
            return;
        }

        var locals = new Dictionary<string, IReadOnlyDictionary<string, bool>>(StringComparer.Ordinal);
        foreach (var hub in state.Hubs)
            if (hub.Value != null)
                locals[hub.Key] = hub.Value.Flags;

        Load(state.Flags, locals);
    }

    /// <summary>
    /// 旗标名是否合规。认三种写法：新式三/四段下划线（hub3_well_shoe_taken）、
    /// 票面写的点号式（hub3.well.shoe_taken）、以及冻结的旧式名（knows_herb_path）。
    /// </summary>
    /// <param name="name">旗标名。</param>
    /// <returns>合规返回 true。</returns>
    public static bool IsValidName(string? name)
    {
        if (string.IsNullOrEmpty(name)) return false;
        if (name!.Length > MaxNameBytes) return false;
        if (FrozenNames.Contains(name)) return true;

        return NewStylePattern.IsMatch(name) || DotStylePattern.IsMatch(name);
    }

    /// <summary>
    /// 空间 id 是否是已知空间（序章 hub1–hub7，正篇 reg_*）。
    /// </summary>
    /// <param name="hub">空间 id。</param>
    /// <returns>已知返回 true。</returns>
    public static bool IsKnownHub(string? hub)
    {
        if (string.IsNullOrEmpty(hub)) return false;
        return KnownHubPattern.IsMatch(hub!);
    }

    /// <summary>
    /// 能不能写这个名字：不为空、不超长。旧式名照写不误。
    /// </summary>
    private static bool CanWrite(string? name)
    {
        if (string.IsNullOrEmpty(name)) return false;
        if (name!.Length > MaxNameBytes) return false;

        return true;
    }
}

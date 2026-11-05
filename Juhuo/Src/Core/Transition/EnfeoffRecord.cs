using System;

namespace Juhuo.Core;

/// <summary>
/// 分封的存档侧：把六个人的封赏记进 <c>prologue.enfeoffs</c>（id → heavy / light / none）。
///
/// 为什么要记：转段之后要能回答"谁当时被封了什么"——厚封的野心家将来会成权臣，
/// 被薄封的会离心，没封的当夜就走了。这些不是一次性的台词，是正篇要接着算的账。
///
/// 与 <see cref="TransitionBook"/> 的关系：**分封不是转段的前置条件**。
/// <c>prologue.done</c> 的判定一个字不动，缺这张表的老档照样能判完成——
/// 老档读进来 <see cref="PrologueState.Enfeoffs"/> 就是空字典，谁都没封过。
/// </summary>
public static class EnfeoffRecord
{
    /// <summary>
    /// 记一个人的封赏。同一个人再记一次就是覆盖（以最后一次为准）。
    /// </summary>
    /// <param name="state">存档。</param>
    /// <param name="companionId">功臣 id，不能为空。</param>
    /// <param name="grade">封的哪一档。</param>
    /// <exception cref="ArgumentNullException"><paramref name="state"/> 为 null。</exception>
    /// <exception cref="ArgumentException">id 为空。</exception>
    public static void Put(SaveState state, string? companionId, Enfeoff grade)
    {
        if (state == null) throw new ArgumentNullException(nameof(state));

        var id = (companionId ?? "").Trim();
        if (id.Length == 0) throw new ArgumentException("功臣 id 不能为空。", nameof(companionId));

        state.Prologue ??= new PrologueState();
        state.Prologue.Enfeoffs ??= new System.Collections.Generic.Dictionary<string, string>(StringComparer.Ordinal);
        state.Prologue.Enfeoffs[id] = EnfeoffBook.Code(grade);
    }

    /// <summary>
    /// 这个人当时封的是哪一档。没记过、或记的值认不出来，都返回 false（不抛）。
    /// </summary>
    /// <param name="state">存档。</param>
    /// <param name="companionId">功臣 id。</param>
    /// <param name="grade">认出来的档位；没记过为 <see cref="Enfeoff.None"/>。</param>
    /// <returns>记过且认得出来返回 true。</returns>
    public static bool TryGet(SaveState state, string? companionId, out Enfeoff grade)
    {
        grade = Enfeoff.None;
        if (state?.Prologue?.Enfeoffs == null) return false;

        var id = (companionId ?? "").Trim();
        if (id.Length == 0) return false;
        if (!state.Prologue.Enfeoffs.TryGetValue(id, out var code)) return false;

        return EnfeoffBook.TryParseCode(code, out grade);
    }

    /// <summary>这个人记过没有（不关心值认不认得出来）。</summary>
    /// <param name="state">存档。</param>
    /// <param name="companionId">功臣 id。</param>
    /// <returns>记过返回 true。</returns>
    public static bool IsRecorded(SaveState state, string? companionId) =>
        TryGet(state, companionId, out _);

    /// <summary>记过几位。</summary>
    /// <param name="state">存档。</param>
    /// <returns>记过的条数；没有存档/没有字典就是 0。</returns>
    public static int Count(SaveState? state) => state?.Prologue?.Enfeoffs?.Count ?? 0;

    /// <summary>
    /// 六个人是不是都封过了。用来判"这一屏做完了没有"，**不参与** <c>prologue.done</c> 的判定。
    /// </summary>
    /// <param name="state">存档。</param>
    /// <param name="table">功臣表。</param>
    /// <returns>六个人都有记录返回 true。</returns>
    public static bool AllRecorded(SaveState state, CompanionTable table)
    {
        if (state == null) throw new ArgumentNullException(nameof(state));
        if (table == null) throw new ArgumentNullException(nameof(table));

        foreach (var companion in table.All)
            if (!IsRecorded(state, companion.Id))
                return false;

        return table.Count > 0;
    }
}

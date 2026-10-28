using System;

namespace Juhuo.Core;

/// <summary>
/// 序章日历：只数日子，从第 1 天走到第 30 天，到头就停。
/// 不看引擎、不读存档、不管刻；节拍与日切由上层来喊。
/// </summary>
public sealed class Calendar
{
    /// <summary>序章第一天。</summary>
    public const int FirstDay = 1;

    /// <summary>序章最后一天（关卡设计：序章 30 天）。</summary>
    public const int LastDay = 30;

    /// <summary>序章总天数。</summary>
    public const int TotalDays = LastDay - FirstDay + 1;

    /// <summary>当前是第几天（1 起）。</summary>
    public int Day { get; private set; }

    /// <summary>
    /// 建一个日历。起始天不在 1–30 之间会被夹到范围内。
    /// </summary>
    /// <param name="startDay">起始天，默认第 1 天。</param>
    public Calendar(int startDay = FirstDay)
    {
        Day = ClampDay(startDay);
    }

    /// <summary>是不是已经走到最后一天。</summary>
    public bool IsLastDay => Day >= LastDay;

    /// <summary>还剩几天可走。</summary>
    public int DaysLeft => LastDay - Day;

    /// <summary>
    /// 往前走几天。负数当 0 处理（不动，返回 false）；到第 30 天封顶。
    /// </summary>
    /// <param name="days">要推进的天数，默认 1。</param>
    /// <returns>日子真的动了才返回 true；原地不动返回 false。</returns>
    public bool Advance(int days = 1)
    {
        if (days <= 0) return false;

        var next = ClampDay(Day + days);
        if (next == Day) return false;

        Day = next;
        return true;
    }

    private static int ClampDay(int day)
    {
        if (day < FirstDay) return FirstDay;
        if (day > LastDay) return LastDay;
        return day;
    }

    public override string ToString() => $"第 {Day} 天";
}

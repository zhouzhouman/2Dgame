using System;

namespace Juhuo.Core;

/// <summary>
/// 开局的起点：第一天早上你身上有什么。
///
/// 数值集中在这里，**不许散落到 Game 层**：一局游戏只能有一个"开局给多少"，
/// 分散写就会变成"新档 30 文、重开 20 文"这种没人查得出来的差别。
///
/// 纯 C#：只往 <see cref="Inventory"/> 里放东西，不认识引擎，也不写存档。
/// </summary>
public static class StartKit
{
    /// <summary>
    /// 开局的钱，单位"文"。
    ///
    /// **占位值 30，待 D1 定稿**。出处：设定集 §18.5 场景 3 里"卖掉一本书 +15 钱"——
    /// 这个世界一笔小买卖是十几文的量级，所以开局给得起"两趟小买卖"的钱，
    /// 饿不死，也买不动刀。
    /// </summary>
    public const int Coins = 30;

    /// <summary>
    /// 钱在背包里的物品 id。与 <see cref="TradeBook.MoneyItem"/> 同一件东西——
    /// 序章只有一种货币，背包里的 <c>coin</c> 就是钱。
    /// </summary>
    public const string CoinItem = TradeBook.MoneyItem;

    /// <summary>
    /// 把开局的 30 文放进囊里。
    ///
    /// 铜钱可叠，所以 30 文只占**一格**：囊里只剩 5 格也放得下。
    /// 放不进去（囊满、或物品表里没有 coin 而按不可叠处理）就**整批拒绝**，
    /// 返回 0 并给一句人话——不会放一半，也不会吞掉钱。
    /// </summary>
    /// <param name="bag">玩家的囊（就地改）。</param>
    /// <param name="catalog">物品表，决定铜钱能不能叠。</param>
    /// <param name="error">放不进去时的一句人话；放进去为 null。</param>
    /// <returns>实际放进去多少文：成 = <see cref="Coins"/>，不成 = 0。</returns>
    /// <exception cref="ArgumentNullException"><paramref name="bag"/> 或 <paramref name="catalog"/> 为 null。</exception>
    public static int Fill(Inventory bag, ItemCatalog catalog, out string? error)
    {
        if (bag == null) throw new ArgumentNullException(nameof(bag));
        if (catalog == null) throw new ArgumentNullException(nameof(catalog));

        return bag.TryAdd(CoinItem, Coins, catalog, out error) ? Coins : 0;
    }
}

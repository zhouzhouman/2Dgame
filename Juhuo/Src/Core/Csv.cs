using System;
using System.Collections.Generic;
using System.Text;

namespace Juhuo.Core;

/// <summary>
/// 极简 CSV 解析器。
/// 支持：双引号包裹、"" 转义、CRLF、UTF-8 BOM、字段内换行。
/// 我们的内容表全部是 QUOTE_ALL，所以不需要更复杂的方言。
/// </summary>
public static class Csv
{
    public static List<string[]> Parse(string text)
    {
        var rows = new List<string[]>();
        if (string.IsNullOrEmpty(text)) return rows;

        if (text[0] == '\uFEFF') text = text.Substring(1);

        var fields = new List<string>();
        var sb = new StringBuilder();
        var inQuotes = false;

        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"') { sb.Append('"'); i++; }
                    else inQuotes = false;
                }
                else sb.Append(c);
                continue;
            }

            switch (c)
            {
                case '"':
                    inQuotes = true;
                    break;
                case ',':
                    fields.Add(sb.ToString());
                    sb.Clear();
                    break;
                case '\r':
                    break;
                case '\n':
                    fields.Add(sb.ToString());
                    sb.Clear();
                    rows.Add(fields.ToArray());
                    fields.Clear();
                    break;
                default:
                    sb.Append(c);
                    break;
            }
        }

        if (sb.Length > 0 || fields.Count > 0)
        {
            fields.Add(sb.ToString());
            rows.Add(fields.ToArray());
        }

        rows.RemoveAll(r => r.Length == 1 && string.IsNullOrWhiteSpace(r[0]));
        return rows;
    }
}

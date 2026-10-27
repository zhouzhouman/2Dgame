# 字体目录

## 现状

`skeleton` 阶段为了能立刻看到中文，`Src/Game/Main.cs` 里**临时**从系统字体加载：

```csharp
font.LoadDynamicFont("C:/Windows/Fonts/msyh.ttc");
```

**这只用于本地开发，不能发行。** 微软雅黑的许可不允许随游戏分发。

## 发行前必须替换

把下面任一字体放进本目录（都是 OFL / 可商用）：

| 字体 | 文件 | 说明 | 体积 |
|---|---|---|---|
| **霞鹜文楷**（推荐） | `LXGWWenKai-Regular.ttf` | 手写感，与"手记"气质最合 | ~20 MB |
| 思源宋体 SC | `SourceHanSerifSC-Regular.otf` | 端正，适合正式文书 | ~24 MB |
| 思源黑体 SC | `SourceHanSansSC-Regular.otf` | 中性，UI 可读性最好 | ~16 MB |

然后把 `Main.cs` 里的路径改成：

```csharp
font.LoadDynamicFont("res://Assets/Fonts/LXGWWenKai-Regular.ttf");
```

## 必须做子集化

中文字库全量 16–24 MB，会让安装包明显变大。构建时用 `fonttools` 按实际用到的字符集裁一遍：

```bash
pip install fonttools brotli
pyftsubset LXGWWenKai-Regular.ttf \
  --text-file=../../Content/text/all_chars.txt \
  --output-file=LXGWWenKai-Subset.ttf \
  --flavor=woff2
```

`all_chars.txt` 可由 `tools/extract_texts.py` 顺手产出（把所有文本去重拼成一行）。

**预计子集体积：300–600 KB。**

## 注意

- 字体必须走 Git LFS（已在 `.gitattributes` 里配好）。
- 不要用系统字体截图做宣传素材，发行版和宣传版要一致。

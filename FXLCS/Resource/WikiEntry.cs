namespace FXLCS;

/// <summary>
/// MC 百科条目。
/// <para/>使用 <see cref="WikiEntry.All"/> 获取所有条目。
/// </summary>
public class WikiEntry {

    /// <summary>
    /// 在 MC 百科中的对应 ID。
    /// </summary>
    public int Id;
    /// <summary>
    /// 中文译名。
    /// <c>null</c> 代表没有翻译。
    /// </summary>
    public string? ChineseName;
    /// <summary>
    /// 各个 Mod 平台的 Slug（例如 advanced-solar-panels）。
    /// 若没有对应键则为不在该平台上。
    /// </summary>
    public Dictionary<ResourcePlatforms, string> Slugs = [];
    /// <summary>
    /// MC 百科的浏览量逆序排行，1 代表浏览量最低。
    /// </summary>
    public int Popularity;

    /// <summary>
    /// 内置数据库中的所有 MC 百科条目。
    /// </summary>
    public static readonly Lazy<List<WikiEntry>> All = new(() => {
        var dataLines = FileUtils.ReadAsLines(@"Resource\WikiEntries.txt", type: typeof(WikiEntry)).ToList();

        // 读取最后一行的浏览量
        var popularityLine = dataLines[^1];
        if (popularityLine.Length % 3 != 0) { // 数据损坏时不要因为 Substring 越界而让整个 Mod 列表无法打开
            Logger.Warn($"内置 MC 百科数据的浏览量行长度（{popularityLine.Length}）不是 3 的倍数，可能已损坏，将忽略末尾 {popularityLine.Length % 3} 个字符");
        }
        Queue<int> popularities = [];
        for (int i = 0; i + 3 <= popularityLine.Length; i += 3) { // 将每 3 个字符切割成一个元素
            popularities.Enqueue(int.Parse(popularityLine.Substring(i, 3).ConvertRadix(86, 10)));
        }
        dataLines.RemoveAt(dataLines.Count - 1);

        // 解析每一行
        List<WikiEntry> results = [];
        var lineNumber = 0;
        foreach (var lineData in dataLines) {
            lineNumber++;
            if (lineData == "") continue;

            var popularity = popularities.Count > 0 ? popularities.Dequeue() : 0;
            foreach (var entryData in lineData.Split('¨')) {
                var entry = new WikiEntry();
                var parts = entryData.Split('|');
                var slugs = parts[0];
                if (slugs.StartsWithF("@")) {
                    entry.Slugs.Add(ResourcePlatforms.Modrinth, slugs.Replace("@", ""));
                } else if (slugs.EndsWithF("@")) {
                    entry.Slugs.Add(ResourcePlatforms.CurseForge, slugs.TrimEnd('@'));
                    entry.Slugs.Add(ResourcePlatforms.Modrinth, entry.Slugs[ResourcePlatforms.CurseForge]);
                } else if (slugs.Contains("@")) {
                    var splitSlugs = slugs.Split('@');
                    entry.Slugs.Add(ResourcePlatforms.CurseForge, splitSlugs[0]);
                    entry.Slugs.Add(ResourcePlatforms.Modrinth, splitSlugs[1]);
                } else {
                    entry.Slugs.Add(ResourcePlatforms.CurseForge, slugs);
                }

                entry.Id = lineNumber;
                entry.Popularity = popularity;
                if (parts.Length >= 2) {
                    entry.ChineseName = parts[^1]; // 最后一项
                    if (entry.ChineseName.Contains("*")) { // 处理 *
                        var englishName = entry.Slugs.Values.First().Replace("-", " ").Capitalize();
                        entry.ChineseName = entry.ChineseName.Replace("*", $" ({englishName})");
                    }
                }
                results.Add(entry);
            }
        }
        return results;
    });

    public override string ToString()
        => $"{Slugs.GetOrDefault(ResourcePlatforms.CurseForge)}&{Slugs.GetOrDefault(ResourcePlatforms.Modrinth)}|{Id}|{ChineseName}，浏览量 {Popularity}";
}
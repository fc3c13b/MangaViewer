using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using MangaViewer;

static string NormalizeFw(string s)
{
    if (string.IsNullOrEmpty(s)) return "";
    var sb = new StringBuilder(s.Length);
    foreach (var c in s)
    {
        if (c >= '０' && c <= '９') sb.Append((char)(c - '０' + '0')); else sb.Append(c);
    }
    return sb.ToString();
}

static int Vol(string p)
{
    var n = NormalizeFw(Path.GetFileNameWithoutExtension(p));
    var pats = new[] { @"第?\s*(\d+)\s*巻", @"(?:vol(?:ume)?\.?\s*)(\d+)", @"(\d+)\s*$", @"^(\d+)\b" };
    foreach (var pat in pats)
    {
        var m = Regex.Match(n, pat, RegexOptions.IgnoreCase);
        if (m.Success && int.TryParse(m.Groups[1].Value, out var v)) return v;
    }
    return int.MaxValue;
}

static string Hash(string full)
{
    using var md5 = MD5.Create();
    var h = md5.ComputeHash(Encoding.UTF8.GetBytes(full));
    return BitConverter.ToString(h).Replace("-", "").ToLowerInvariant();
}

static string? Resolve(string root, string pattern) =>
    Directory.GetDirectories(root).FirstOrDefault(d => Path.GetFileName(d).Contains(pattern, StringComparison.Ordinal));

static string[] SortedCbz(string folder) =>
    Directory.GetFiles(folder, "*.*", SearchOption.TopDirectoryOnly)
        .Where(f => f.EndsWith(".cbz", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        .OrderBy(f => Vol(f))
        .ThenBy(f => NormalizeFw(Path.GetFileName(f)), StringComparer.OrdinalIgnoreCase)
        .ToArray();

var rootDir = @"O:\NEW";
var f1 = Resolve(rootDir, "とんでもスキルで異世界放浪メシ 再再スキャン");
var f2 = Resolve(rootDir, "聖者無双 再スキャン");
var f3 = Resolve(rootDir, "100万回のスキをあげる");
var f4 = Resolve(rootDir, "10年間身体を乗っ取られ悪女になっていた私に、二度と顔を見せるなと婚約破棄してきた騎士様が今日も縋ってくる");
if (new[] { f1, f2, f3, f4 }.Any(string.IsNullOrEmpty))
{
    Console.WriteLine("RESOLVE_FAILED");
    return;
}

var area1 = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MangaViewer", "CBZCache");
var area2 = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MangaViewer", "CBZCache_Area2");
const string marker = ".complete";

var mgr = new CbzManager();

async Task WaitUntilAsync(Func<bool> predicate, int timeoutMs)
{
    var start = DateTime.UtcNow;
    while ((DateTime.UtcNow - start).TotalMilliseconds < timeoutMs)
    {
        if (predicate()) return;
        await Task.Delay(300);
    }
}

// Simulate list down transitions:
// 1->2: current=f2 next=f3
// 2->3: current=f3 next=f4
mgr.PreloadForNavigationContext(f2!, 4, f3!);
    await WaitUntilAsync(() =>
    {
        var f2Now = SortedCbz(f2!);
        if (f2Now.Length < 4) return false;
        return Ready(f2Now[0], false) && Ready(f2Now[1], false) && Ready(f2Now[2], false) && Ready(f2Now[3], false);
    }, 120000);

mgr.PreloadForNavigationContext(f3!, 4, f4!);
    await WaitUntilAsync(() =>
    {
        var f3Now = SortedCbz(f3!);
        var f4Now = SortedCbz(f4!);
        bool ok3 = f3Now.Length > 0 && Ready(f3Now[0], true);
        bool ok4 = f4Now.Length > 0 && Ready(f4Now[0], true);
        return ok3 && ok4;
    }, 120000);

bool Ready(string cbz, bool a2)
{
    var root = a2 ? area2 : area1;
    var dir = Path.Combine(root, Hash(cbz));
    return Directory.Exists(dir) && File.Exists(Path.Combine(dir, marker));
}

var f2cbz = SortedCbz(f2!);
var f3cbz = SortedCbz(f3!);
var f4cbz = SortedCbz(f4!);

Console.WriteLine("RESULT_START");
Console.WriteLine($"F2={Path.GetFileName(f2)}");
for (int i = 0; i < Math.Min(4, f2cbz.Length); i++)
    Console.WriteLine($"F2_TOP{i+1}:{Path.GetFileName(f2cbz[i])}|A1={Ready(f2cbz[i], false)}");
Console.WriteLine($"F3={Path.GetFileName(f3)}");
if (f3cbz.Length > 0)
    Console.WriteLine($"F3_TOP1:{Path.GetFileName(f3cbz[0])}|A2={Ready(f3cbz[0], true)}");
Console.WriteLine($"F4={Path.GetFileName(f4)}");
if (f4cbz.Length > 0)
    Console.WriteLine($"F4_TOP1:{Path.GetFileName(f4cbz[0])}|A2={Ready(f4cbz[0], true)}");

int a1Count = Directory.Exists(area1) ? Directory.GetDirectories(area1).Count(d => File.Exists(Path.Combine(d, marker))) : 0;
int a2Count = Directory.Exists(area2) ? Directory.GetDirectories(area2).Count(d => File.Exists(Path.Combine(d, marker))) : 0;
Console.WriteLine($"AREA1_READY={a1Count}");
Console.WriteLine($"AREA2_READY={a2Count}");
Console.WriteLine("RESULT_END");

using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace MangaViewer
{
    internal static class CbzVolumeNameParser
    {
        private static readonly Regex VolumeKanPattern = new(@"第?\s*(\d+)(?:[bBwWsS])?\s*巻", RegexOptions.Compiled);
        private static readonly Regex VolumeLatinPattern = new(@"(?:vol(?:ume)?\.?\s*)(\d+)(?:[bBwWsS])?", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex VolumeParenPattern = new(@"[\(（]\s*(\d+)(?:[bBwWsS])?\s*[\)）]", RegexOptions.Compiled);
        private static readonly Regex VolumeTailNumberPattern = new(@"(\d+)(?:[bBwWsS])?\s*$", RegexOptions.Compiled);
        private static readonly Regex VolumeHeadNumberPattern = new(@"^(\d+)(?:[bBwWsS])?\b", RegexOptions.Compiled);

        internal static string NormalizeFullWidthDigits(string input)
        {
            if (string.IsNullOrEmpty(input)) return "";
            var sb = new StringBuilder(input.Length);
            foreach (char c in input)
            {
                if (c >= '０' && c <= '９')
                    sb.Append((char)(c - '０' + '0'));
                else
                    sb.Append(c);
            }

            return sb.ToString();
        }

        internal static int ExtractVolumeNumber(string filePath)
        {
            string rawName = Path.GetFileNameWithoutExtension(filePath);
            string name = NormalizeFullWidthDigits(rawName);

            Match m1 = VolumeKanPattern.Match(name);
            if (m1.Success && int.TryParse(m1.Groups[1].Value, out var v1))
                return v1;

            Match m2 = VolumeLatinPattern.Match(name);
            if (m2.Success && int.TryParse(m2.Groups[1].Value, out var v2))
                return v2;

            Match m3 = VolumeParenPattern.Match(name);
            if (m3.Success && int.TryParse(m3.Groups[1].Value, out var v3))
                return v3;

            Match m4 = VolumeTailNumberPattern.Match(name);
            if (m4.Success && int.TryParse(m4.Groups[1].Value, out var v4))
                return v4;

            Match m5 = VolumeHeadNumberPattern.Match(name);
            if (m5.Success && int.TryParse(m5.Groups[1].Value, out var v5))
                return v5;

            return int.MaxValue;
        }
    }

    internal class CbzVolumeComparer : IComparer<string>
    {
        [DllImport("shlwapi.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
        private static extern int StrCmpLogicalW(string psz1, string psz2);

        public int Compare(string? xFile, string? yFile)
        {
            if (xFile == null && yFile == null) return 0;
            if (xFile == null) return -1;
            if (yFile == null) return 1;

            int vx = CbzVolumeNameParser.ExtractVolumeNumber(xFile);
            int vy = CbzVolumeNameParser.ExtractVolumeNumber(yFile);
            bool hasVx = vx != int.MaxValue;
            bool hasVy = vy != int.MaxValue;

            if (hasVx && hasVy && vx != vy)
                return vx.CompareTo(vy);

            if (hasVx != hasVy)
                return hasVx ? -1 : 1;

            string nx = CbzVolumeNameParser.NormalizeFullWidthDigits(Path.GetFileName(xFile));
            string ny = CbzVolumeNameParser.NormalizeFullWidthDigits(Path.GetFileName(yFile));
            try
            {
                int res = StrCmpLogicalW(nx, ny);
                if (res != 0) return res;
            }
            catch
            {
                // Fallback below.
            }

            return string.Compare(nx, ny, StringComparison.OrdinalIgnoreCase);
        }
    }
}

using System;
using System.IO;

namespace MangaViewer
{
    /// <summary>
    /// CBZ/ZIP/RAR のアーカイブ拡張子を一元管理するヘルパー。
    /// 拡張子の追加はここだけ変更すれば全箇所に反映される。
    /// </summary>
    internal static class ArchiveExtensions
    {
        /// <summary>対応するアーカイブ拡張子（小文字）の配列。</summary>
        private static readonly string[] Supported = { ".cbz", ".zip", ".rar" };

        /// <summary>ファイルパスがアーカイブ対象かどうかを返す。</summary>
        internal static bool IsArchive(string filePath)
        {
            string ext = Path.GetExtension(filePath);
            if (string.IsNullOrEmpty(ext)) return false;
            foreach (var s in Supported)
                if (s.Equals(ext, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        /// <summary>RAR ファイルかどうかを返す。</summary>
        internal static bool IsRar(string filePath) =>
            Path.GetExtension(filePath).Equals(".rar", StringComparison.OrdinalIgnoreCase);

        /// <summary>ZIP/CBZ ファイルかどうかを返す。</summary>
        internal static bool IsZipBased(string filePath)
        {
            string ext = Path.GetExtension(filePath);
            return ext.Equals(".cbz", StringComparison.OrdinalIgnoreCase)
                || ext.Equals(".zip", StringComparison.OrdinalIgnoreCase);
        }
    }
}

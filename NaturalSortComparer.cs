using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace MangaViewer
{
    /// <summary>
    /// Windowsエクスプローラーと同じ自然言語ソート（StrCmpLogicalW）を行う比較器。
    /// </summary>
    public class NaturalSortComparer : IComparer<string>
    {
        [DllImport("Shlwapi.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
        private static extern int StrCmpLogicalW(string pszOne, string pszTwo);

        public static readonly NaturalSortComparer Default = new();

        public int Compare(string x, string y)
        {
            if (x == null && y == null) return 0;
            if (x == null) return -1;
            if (y == null) return 1;
            return StrCmpLogicalW(x, y);
        }
    }
}

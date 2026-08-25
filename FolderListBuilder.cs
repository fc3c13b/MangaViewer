using System;
using System.IO;
using System.Windows.Forms;

namespace MangaViewer
{
    /// <summary>
    /// Encapsulates folder list building + ListBox population (extracted from Form1.BuildSubfolderList).
    /// </summary>
    public static class FolderListBuilder
    {
        public static void Build(Form1 form, string rootPath)
        {
            if (form._folderService == null)
                form._folderService = new FolderService(form._settings);

            // Status callback for labelInfo updates during indexing.
            form._folderService.SetStatusCallback(msg =>
            {
                form.labelInfo.Text = msg;
                Application.DoEvents();
            });

            try
            {
                var entries = form._folderService.BuildFolderIndex(rootPath);
                form._folderList.Clear();
                form.listBoxFolders.DataSource = null;
                form.listBoxFolders.Items.Clear();

                int firstUnratedIndex = -1;

                for (int i = 0; i < entries.Count; i++)
                {
                    var entry = entries[i];
                    form._folderList.Add(entry.Path);
                    string folderName = Path.GetFileName(entry.Path);
                    int rating = RatingService.ReadRating(entry.Path);
                    string ratingPrefix = rating >= 0 ? $"[{rating}] " : "";
                    form.listBoxFolders.Items.Add($"{ratingPrefix}{folderName} -[{entry.ImageCount}]");

                    if (firstUnratedIndex < 0 && rating < 0)
                        firstUnratedIndex = i;
                }

                if (form._folderList.Count > 0)
                {
                    int initialIndex = firstUnratedIndex >= 0 ? firstUnratedIndex : 0;
                    form.listBoxFolders.SelectedIndex = initialIndex;
                    form._currentFolderIndex = initialIndex;
                }
            }
            catch
            {
                form._folderList.Clear();
                form._currentFolderIndex = -1;
            }
            finally
            {
                form.labelInfo.Text = "";
                Application.DoEvents();
            }
        }
    }
}

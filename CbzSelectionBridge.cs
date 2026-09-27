using System;
using System.Windows.Forms;

namespace MangaViewer
{
    internal static class CbzSelectionBridge
    {
        internal static void PopulateSelectionList(Form1 form, CbzManager? manager, ListBox listBox)
        {
            if (form == null) throw new ArgumentNullException(nameof(form));
            if (listBox == null) throw new ArgumentNullException(nameof(listBox));

            if (manager == null)
                return;

            listBox.Items.Clear();
            listBox.Items.AddRange(manager.CbxFiles.ToArray());
            if (manager.CbxFiles.Count > 0)
                listBox.SelectedIndex = Math.Clamp(manager.ActiveCbxIndex, 0, manager.CbxFiles.Count - 1);
        }

        internal static void LoadSelectedCbz(Form1 form, string? selectedFile, Action beforeLoad)
        {
            if (form == null) throw new ArgumentNullException(nameof(form));
            if (string.IsNullOrWhiteSpace(selectedFile))
                return;

            beforeLoad?.Invoke();
            form._cbzManager?.LoadCbx(selectedFile);
            form.HideCbzSelectDialog();
        }
    }
}

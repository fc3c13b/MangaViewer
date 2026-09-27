using System;
using System.Drawing;
using System.Windows.Forms;

namespace MangaViewer
{
    internal static class UiLayoutService
    {
        internal static void EnsureBasicLayout(Form1 form)
        {
            if (form.ClientSize.Width < Constants.MinWidth || form.ClientSize.Height < Constants.MinHeight)
                form.Size = new Size(Constants.InitialWidth, Constants.InitialHeight);

            int cw = form.ClientSize.Width;
            int ch = form.ClientSize.Height;

            int listX = (int)(cw * 0.72);
            int listW = cw - listX;

            form.panelList.Bounds = new Rectangle(listX, 0, listW, ch);
            if (form.listBoxFolders != null)
                form.listBoxFolders.Bounds = form.panelList.ClientRectangle;

            form.labelInfo.Visible = !form._fullScreenMode;
            if (!form._fullScreenMode && form.labelInfo.Visible)
                form.labelInfo.Location = new Point(10, ch - 25);
        }

        internal static void UpdateLayout(Form1 form)
        {
            if (form.panelList == null || form.labelInfo == null) return;
            if (form._displayManager == null) return;

            int clientWidth = form.ClientSize.Width;
            int clientHeight = form.ClientSize.Height;

            var bounds = form._displayManager.CalculatePictureBoxBounds(clientWidth, clientHeight, out Rectangle listPanelBounds, form._fullScreenMode);

            for (int i = 0; i < form._displayManager.pictureBoxes.Length; i++)
                form._displayManager.pictureBoxes[i].Bounds = bounds[i];

            form.panelList.Bounds = listPanelBounds;
            if (form.listBoxFolders != null)
                form.listBoxFolders.Bounds = form.panelList.ClientRectangle;

            form.labelInfo.Visible = !form._fullScreenMode;
            if (!form._fullScreenMode)
                form.labelInfo.Location = new Point(10, clientHeight - 25);
        }

        internal static void ToggleFullScreen(Form1 form)
        {
            form.SetFullScreenMode(!form._fullScreenMode);
            form.UpdateInfoLabelAfterToggle();
            form.UpdateLayout();
        }

        internal static void ToggleLeadingBlankPage(Form1 form)
        {
            form.SetLeadingBlankPageEnabled(!form.IsLeadingBlankPageEnabled);
        }
    }
}

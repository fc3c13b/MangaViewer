using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace MangaViewer
{
    internal static class CbzDialogLayout
    {
        internal static Panel CreateOverlay(Form1 form)
        {
            var panel = new Panel
            {
                BackColor = Color.Transparent,
                BorderStyle = BorderStyle.None,
                Visible = false,
                Location = Point.Empty,
                Size = Size.Empty
            };

            var titleLabel = new Label
            {
                Height = 28,
                ForeColor = Color.White,
                BackColor = Color.FromArgb(60, 60, 60),
                Padding = new Padding(8, 6, 8, 0),
                Text = "CBZ一覧 (Lキーで閉じる)"
            };

            var listBox = new ListBox
            {
                BackColor = Color.FromArgb(40, 40, 40),
                ForeColor = Color.White,
                BorderStyle = BorderStyle.None,
                Font = new Font("Meiryo UI", 9F),
                SelectionMode = SelectionMode.One,
                HorizontalScrollbar = true,
                TabStop = false,
                DrawMode = DrawMode.OwnerDrawFixed,
                IntegralHeight = false
            };

            listBox.DrawItem += (sender, e) => DrawItem(listBox, e);
            listBox.SelectedIndexChanged += (sender, e) =>
            {
                if (listBox.SelectedIndex >= 0)
                {
                    string? selectedFile = listBox.SelectedItem?.ToString();
                    CbzSelectionBridge.LoadSelectedCbz(form, selectedFile, form.RememberCurrentPlaybackPosition);
                }
            };
            listBox.KeyDown += (sender, e) =>
            {
                if (!e.Control && !e.Alt && e.KeyCode == Keys.L)
                {
                    e.Handled = true;
                    form.HideCbzSelectDialog();
                }
            };

            panel.Controls.Add(listBox);
            panel.Controls.Add(titleLabel);
            form.Controls.Add(panel);
            panel.Paint += (sender, e) => PaintPanel(panel, e);

            return panel;
        }

        internal static void Layout(Form1 form, Panel panel, ListBox listBox)
        {
            if (panel == null || listBox == null)
                return;

            int appWidth = Math.Max(1, form.ClientSize.Width);
            int appHeight = Math.Max(1, form.ClientSize.Height);

            int charWidth;
            int rowHeight;
            using (var g = form.CreateGraphics())
            {
                charWidth = TextRenderer.MeasureText(g, new string('W', 60), listBox.Font, new Size(int.MaxValue, int.MaxValue), TextFormatFlags.NoPadding).Width;
                rowHeight = listBox.ItemHeight;
            }

            int listWidth = Math.Min(Math.Max(charWidth + 32, 420), Math.Max(240, appWidth - 80));
            int visibleRows = Math.Min(40, Math.Max(8, listBox.Items.Count == 0 ? 8 : listBox.Items.Count));
            int listHeight = Math.Min((rowHeight * visibleRows) + 6, appHeight - 120);

            int dialogWidth = listWidth + 24;
            int dialogHeight = listHeight + 40;
            int dialogLeft = Math.Max(0, (appWidth - dialogWidth) / 2);
            int dialogTop = Math.Max(0, (appHeight - dialogHeight) / 2);

            var titleLabel = panel.Controls.OfType<Label>().FirstOrDefault();
            if (titleLabel != null)
                titleLabel.Bounds = new Rectangle(0, 0, dialogWidth, 28);

            listBox.Bounds = new Rectangle(12, 28, listWidth, listHeight);
            panel.Location = new Point(dialogLeft, dialogTop);
            panel.Size = new Size(dialogWidth, dialogHeight);
            panel.Tag = new Rectangle(0, 0, dialogWidth, dialogHeight);
        }

        internal static void PaintPanel(Panel panel, PaintEventArgs e)
        {
            using var fillBrush = new SolidBrush(Color.FromArgb(235, 24, 24, 24));
            using var borderPen = new Pen(Color.FromArgb(220, 220, 220, 220));
            var panelRect = new Rectangle(0, 0, panel.Width, panel.Height);

            e.Graphics.FillRectangle(fillBrush, panelRect);
            e.Graphics.DrawRectangle(borderPen, new Rectangle(0, 0, panel.Width - 1, panel.Height - 1));
        }

        private static void DrawItem(ListBox listBox, DrawItemEventArgs e)
        {
            if (e.Index < 0) return;

            e.DrawBackground();
            e.DrawFocusRectangle();

            object? item = listBox.Items[e.Index];
            string itemText = item?.ToString() ?? string.Empty;
            string fileName = Path.GetFileName(itemText);
            Font font = e.Font ?? listBox.Font ?? new Font(FontFamily.GenericSansSerif, 9F, FontStyle.Regular);
            using (Brush brush = new SolidBrush(e.ForeColor))
            {
                e.Graphics.DrawString(fileName, font, brush, e.Bounds);
            }
        }
    }
}

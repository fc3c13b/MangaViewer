using System;
using System.Drawing;
using System.Windows.Forms;

namespace MangaViewer
{
    /// <summary>
    /// Encapsulates scroll animation and custom drawing for the folder ListBox.
    /// </summary>
    public class ListBoxScrollHelper : IDisposable
    {
        private static readonly Font ListFont = new Font("Meiryo UI", 9F);

        private ListBox _listBox;
        private Panel _panelList;
        private System.Windows.Forms.Timer? _scrollTimer;

        private int _scrollOffsetX;
        private bool _scrolling;
        private long _staticPhaseEnd;
        private long _scrollStartX;
        private long _scrollTargetX;
        private float _scrollPixelsPerMs;

        public ListBoxScrollHelper(ListBox listBox, Panel panelList)
        {
            _listBox = listBox;
            _panelList = panelList;

            _scrollTimer = new Timer { Interval = 16 }; // ~60 FPS
            _scrollTimer.Tick += (s, e) => UpdateScrollAnimation();
            _scrollTimer.Start();

            listBox.DrawItem += ListBox_DrawItem;
        }

        public void Dispose()
        {
            if (_scrollTimer != null)
            {
                _scrollTimer.Stop();
                _scrollTimer.Tick -= (s, e) => UpdateScrollAnimation();
                _scrollTimer.Dispose();
                _scrollTimer = null;
            }
        }

        /// <summary>
        /// Called when a new item is selected to start/reset its scroll animation.
        /// </summary>
        public void OnSelectedIndexChanged()
        {
            ResetScrollAnimation();
        }

        private void ResetScrollAnimation()
        {
            if (_listBox.SelectedIndex < 0) return;

            string text = _listBox.Items[_listBox.SelectedIndex]?.ToString() ?? "";
            if (string.IsNullOrEmpty(text)) return;

            _scrollOffsetX = 0;
            _scrolling = false;
            _scrollPixelsPerMs = 0f;

            using (var g = _listBox.CreateGraphics())
            {
                SizeF textSize = g.MeasureString(text, ListFont);
                int textWidthPx = (int)(textSize.Width + 10);
                int clientWidth = _listBox.ClientRectangle.Width;

                _scrollStartX = 0;
                _scrollTargetX = Math.Max(clientWidth, textWidthPx);
            }

            _staticPhaseEnd = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + 3000; // 3s static phase
        }

        private void UpdateScrollAnimation()
        {
            if (_listBox.SelectedIndex < 0) return;

            long nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            if (!_scrolling && _scrollTargetX > 0 && nowMs >= _staticPhaseEnd)
            {
                long scrollWidth = _scrollTargetX - _scrollStartX;
                if (scrollWidth > 0)
                {
                    float durationMs = Math.Max(500, scrollWidth * 3.6f);
                    _scrollPixelsPerMs = scrollWidth / durationMs;
                    _scrolling = true;
                }
            }

            if (_scrolling && _scrollPixelsPerMs > 0)
            {
                long totalScrollWidth = Math.Max(1, _scrollTargetX - _scrollStartX);
                _scrollOffsetX += (int)(_scrollPixelsPerMs * 16); // ~per frame at 60 FPS

                if (_scrollOffsetX >= totalScrollWidth)
                {
                    _scrollOffsetX = 0;
                    _scrolling = false;
                    _staticPhaseEnd = nowMs + 3000;
                }

                int idx = _listBox.SelectedIndex;
                if (idx >= 0 && idx < _listBox.Items.Count)
                {
                    Rectangle r = _listBox.GetItemRectangle(idx);
                    _listBox.Invalidate(r, false);
                }
            }
        }

        private void ListBox_DrawItem(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0 || e.Index >= _listBox.Items.Count) return;

            bool selected = (e.State & DrawItemState.Selected) != 0;
            using (var brush = new SolidBrush(selected ? Color.FromArgb(60, 60, 120) : _panelList.BackColor))
            {
                e.Graphics.FillRectangle(brush, e.Bounds);
            }

            string text = _listBox.Items[e.Index]?.ToString() ?? "";
            if (string.IsNullOrEmpty(text)) return;

            bool isSelectedRow = (e.Index == _listBox.SelectedIndex);

            SizeF textSize = e.Graphics.MeasureString(text, ListFont, e.Bounds.Width);

            if (isSelectedRow)
            {
                long textWidthPx = (long)(textSize.Width + 10);
                long clientWidth = _listBox.ClientRectangle.Width;

                // Ensure initial values set once per selection change
                if (_scrollStartX == 0 && _scrollTargetX == 0)
                {
                    _scrollOffsetX = 0;
                    _scrollStartX = 0;
                    _scrollTargetX = Math.Max(clientWidth, textWidthPx);
                    ResetScrollAnimation();
                }

                int drawX = e.Bounds.X - _scrollOffsetX;
                using (var b = new SolidBrush(Color.White))
                {
                    e.Graphics.DrawString(text, ListFont, b, drawX, e.Bounds.Y + 2);
                }
            }
            else
            {
                using (var b = new SolidBrush(_listBox.ForeColor))
                {
                    e.Graphics.DrawString(text, ListFont, b, e.Bounds.X + 2, e.Bounds.Y + 2);
                }
            }
        }
    }
}

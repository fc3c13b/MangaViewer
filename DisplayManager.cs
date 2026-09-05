using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace MangaViewer
{
    /// <summary>
    /// Manages dynamic PictureBox array for displaying 2 or 8 images.
    /// </summary>
    public class DisplayManager : IDisposable
    {
        private Form _ownerForm;
        internal Settings _settings;
        private ImageService _imageService;

        /// <summary>PictureBox array</summary>
        internal PictureBox[] pictureBoxes;

        /// <summary>Current images displayed in each PictureBox</summary>
        private Image?[] currentImages;

        /// <summary>Number of images to display (2 or 8)</summary>
        public int DisplayCount => _settings.DisplayCount;

        /// <summary>Image paths for current folder</summary>
        internal List<string> ImagePaths { get; set; } = new List<string>();

        /// <summary>Current start index for display</summary>
        public int CurrentIndex { get; private set; } = 0;

        public DisplayManager(Form ownerForm, Settings settings, ImageService imageService)
        {
            _ownerForm = ownerForm;
            _settings = settings;
            _imageService = imageService;
            pictureBoxes = new PictureBox[0];
            currentImages = Array.Empty<Image?>();
        }

        /// <summary>
        /// Initialize PictureBox array based on DisplayCount setting.
        /// Call this when first loading or when DisplayCount changes.
        /// </summary>
        public void InitializePictureBoxes()
        {
            DisposePictureBoxes();
            int count = DisplayCount;
            pictureBoxes = new PictureBox[count];
            currentImages = new Image?[count];

            for (int i = 0; i < count; i++)
            {
                var pb = new PictureBox
                {
                    SizeMode = PictureBoxSizeMode.Zoom,
                    BackColor = Color.Black,
                };
                pictureBoxes[i] = pb;
                _ownerForm.Controls.Add(pb);
            }
        }

        /// <summary>
        /// Dispose existing PictureBoxes and clear Controls.
        /// </summary>
        public void DisposePictureBoxes()
        {
            if (pictureBoxes == null) return;
            foreach (var pb in pictureBoxes)
            {
                if (pb.Image != null)
                {
                    ImageService.DisposeImage(pb.Image);
                    pb.Image = null;
                }
                _ownerForm.Controls.Remove(pb);
                pb.Dispose();
            }
            pictureBoxes = new PictureBox[0];
        }

        /// <summary>
        /// Display images starting from the given index.
        /// </summary>
        public void DisplayImages(int startIndex)
        {
            LogWriter.Log($"[DisplayManager] DisplayImages(startIndex={startIndex}, DisplayCount={DisplayCount}, ImagePaths.Count={ImagePaths.Count})");
            CurrentIndex = startIndex;
            int count = DisplayCount;

            if (pictureBoxes.Length == 0 || pictureBoxes.Length != count)
                InitializePictureBoxes();

            LogWriter.Log($"[DisplayManager] pictureBoxes.Length={pictureBoxes.Length}, count={count}");

            if (ImagePaths.Count == 0)
            {
                foreach (var pb in pictureBoxes) pb.Image = null;
                return;
            }

            // Manga reading order: right-to-left, top-to-bottom
            // Panel position (col,row) maps to page index: row * cols + (cols - 1 - col)
            int cols = count == 8 ? 4 : (count == 1 ? 1 : 2);
            LogWriter.Log($"[DisplayManager] cols={cols}, count={count}");
            for (int i = 0; i < count; i++)
            {
                int col = i % cols;
                int row = i / cols;
                int pageOffset = row * cols + (cols - 1 - col);
                int imgIdx = startIndex + pageOffset;
                string? imagePath = imgIdx < ImagePaths.Count ? ImagePaths[imgIdx] : null;
                LogWriter.Log($"[DisplayManager] i={i}, imgIdx={imgIdx}, imagePath={(string.IsNullOrEmpty(imagePath) ? "(null)" : Path.GetFileName(imagePath))}");
                LoadImageIntoPictureBox(pictureBoxes[i], ref currentImages![i], imagePath);
            }

            // Preload next batch into cache (closest in sort order first)
            // 2-display: 8, 8-display: 16
            int prefetchCount = DisplayCount == 2 ? 8 : 16;
            int nextIdx = startIndex + count;
            if (nextIdx < ImagePaths.Count)
            {
                System.Threading.Tasks.Task.Run(() =>
                {
                    for (int i = 0; i < prefetchCount && (nextIdx + i) < ImagePaths.Count; i++)
                        _imageService.LoadOrGetCachedImage(ImagePaths[nextIdx + i]);
                });
            }
        }

        /// <summary>
        /// Get info text for the label based on current display.
        /// </summary>
        public string GetInfoText(string currentFolder, int folderIndex, List<FolderEntry>? folderList)
        {
            if (ImagePaths.Count == 0) return "";

            int count = DisplayCount;
            string folderName = Path.GetFileName(currentFolder);
            string folderInfo = !string.IsNullOrEmpty(folderName) ? $"[{folderName}] " : "";
            int folderCount = folderList?.Count ?? 0;
            string folderIndexInfo = folderCount > 1 ? $"{folderIndex + 1}/{folderCount}話 " : "";

            var pageNumbers = new List<int>();
            for (int i = 0; i < count; i++)
            {
                int imgIdx = CurrentIndex + i;
                if (imgIdx < ImagePaths.Count)
                    pageNumbers.Add(FolderService.ExtractNumberFromFileName(ImagePaths[imgIdx]));
            }

            string pagesText;
            if (count == 2 && pageNumbers.Count >= 2)
                pagesText = $"右: {pageNumbers[0]} | 左: {pageNumbers[1]}";
            else if (count == 2 && pageNumbers.Count > 0)
                pagesText = $"右: {pageNumbers[0]}";
            else
                pagesText = string.Join(" / ", pageNumbers);

            int spreadIndex = CurrentIndex / count + 1;
            int totalSpreads = (ImagePaths.Count + count - 1) / count;

            return $"{folderInfo}{folderIndexInfo}{pagesText} | {spreadIndex}/{totalSpreads}";
        }

        private void LoadImageIntoPictureBox(PictureBox pb, ref Image? currentImage, string? imagePath)
        {
            LogWriter.Log($"[DisplayManager] LoadImageIntoPictureBox(imagePath={imagePath})");
            if (currentImage != null)
            {
                ImageService.DisposeImage(currentImage);
                currentImage = null;
            }

            if (string.IsNullOrEmpty(imagePath) || !File.Exists(imagePath))
            {
                pb.Image = null;
                return;
            }

            try
            {
                var img = _imageService.LoadOrGetCachedImage(imagePath);
                // LoadOrGetCachedImage may return null for invalid/corrupted images.
                if (img != null)
                {
                    currentImage = img;
                    pb.Image = currentImage;
                }
                else
                {
                    // Show blank/black panel instead of crashing.
                    pb.Image = null;
                }
            }
            catch (OutOfMemoryException)
            {
                // Unsupported or too large image: show error once, then continue.
                MessageBox.Show("画像の読み込みに失敗しました:\n" + imagePath, "エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
                pb.Image = null;
                if (currentImage != null)
                {
                    ImageService.DisposeImage(currentImage);
                    currentImage = null;
                }
            }
            catch (Exception ex)
            {
                // Unexpected error: show dialog but do not crash.
                MessageBox.Show("画像の読み込みに失敗しました:\n" + imagePath + "\n\n" + ex.Message, "エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
                pb.Image = null;
                if (currentImage != null)
                {
                    ImageService.DisposeImage(currentImage);
                    currentImage = null;
                }
            }
        }

        /// <summary>
        /// Calculate bounds for all PictureBoxes and the folder list panel.
        /// </summary>
        public Rectangle[] CalculatePictureBoxBounds(int clientWidth, int clientHeight, out Rectangle listPanelBounds, bool fullScreenMode = false)
        {
            // フルサイズモード時はgap=0、画像領域比率を最大に
            int gap = fullScreenMode ? 0 : 2;
            int count = DisplayCount;

            double imageAreaRatio;
            if (fullScreenMode)
            {
                // 設定：全画面表示の画像領域幅比率（%）
                imageAreaRatio = _settings.FullScreenModeImageAreaPercent / 100.0;
            }
            else if (count == 2)
            {
                // 設定：ノーマル表示の画像領域幅比率（%）
                imageAreaRatio = _settings.NormalModeImageAreaPercent / 100.0;
            }
            else if (count == 1)
            {
                // 1枚表示: 中央に全画面に近いサイズで表示
                imageAreaRatio = 0.95;
            }
            else
            {
                imageAreaRatio = 0.85;
            }

            int imageTotalWidth = (int)(clientWidth * imageAreaRatio);
            int listW = clientWidth - imageTotalWidth;
            int height = clientHeight - 30;

            int rows, cols;
            if (count == 8) { rows = 2; cols = 4; }
            else if (count == 1) { rows = 1; cols = 1; }
            else { rows = 1; cols = 2; }

            int pbWidth = (imageTotalWidth - gap * (cols + 1)) / cols;
            int pbHeight = (height - gap * (rows + 1)) / rows;

            var bounds = new Rectangle[count];
            for (int i = 0; i < count; i++)
            {
                int col = i % cols;
                int row = i / cols;
                int x = gap * (col + 1) + col * pbWidth;
                int y = gap * (row + 1) + row * pbHeight;
                bounds[i] = new Rectangle(x, y, pbWidth, pbHeight);
            }

            listPanelBounds = new Rectangle(imageTotalWidth, 0, listW, clientHeight);
            return bounds;
        }

        /// <summary>
        /// Get the step size for navigation (forward/backward).
        /// </summary>
        public int NavigationStep => DisplayCount;

        /// <summary>
        /// Update settings reference (called after SettingsDialog OK).
        /// </summary>
        public void UpdateSettings(Settings settings)
        {
            _settings = settings;
        }

        public void Dispose()
        {
            if (pictureBoxes != null)
                DisposePictureBoxes();
            currentImages = Array.Empty<Image?>();
        }
    }
}
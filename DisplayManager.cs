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
        private System.Threading.CancellationTokenSource? _renderCts;
        private int _renderGeneration;

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

        private void CancelRender()
        {
            try
            {
                _renderCts?.Cancel();
            }
            catch
            {
                // ignore cancellation failures
            }
        }

        private void ClearPictureBoxes()
        {
            if (pictureBoxes == null) return;

            foreach (var pb in pictureBoxes)
            {
                if (pb == null) continue;
                var oldImage = pb.Image;
                pb.Image = null;
                if (oldImage != null)
                    ImageService.DisposeImage(oldImage);
            }
        }

        private void QueueAssignImage(int generation, int index, Image? image)
        {
            if (_ownerForm.IsDisposed || !_ownerForm.IsHandleCreated)
            {
                image?.Dispose();
                return;
            }

            try
            {
                _ownerForm.BeginInvoke(new Action(() =>
                {
                    try
                    {
                        if (generation != System.Threading.Volatile.Read(ref _renderGeneration) ||
                            index < 0 || index >= pictureBoxes.Length)
                        {
                            image?.Dispose();
                            return;
                        }

                        var pb = pictureBoxes[index];
                        if (pb == null || pb.IsDisposed)
                        {
                            image?.Dispose();
                            return;
                        }

                        var oldImage = pb.Image;
                        pb.Image = image;
                        if (oldImage != null)
                            ImageService.DisposeImage(oldImage);
                    }
                    catch (Exception ex)
                    {
                        StartupHandler.WriteStartupLog($"[DisplayManager] assign image failed index={index} error={ex.GetType().Name}");
                        image?.Dispose();
                    }
                }));
            }
            catch
            {
                image?.Dispose();
            }
        }

        private void RenderImagesAsync(int generation, int startIndex, System.Threading.CancellationToken token)
        {
            int count = DisplayCount;
            int cols = count == 8 ? 4 : (count == 1 ? 1 : 2);

            for (int i = 0; i < count; i++)
            {
                if (token.IsCancellationRequested || generation != System.Threading.Volatile.Read(ref _renderGeneration))
                    return;

                int col = i % cols;
                int row = i / cols;
                int pageOffset = row * cols + (cols - 1 - col);
                int imgIdx = startIndex + pageOffset;
                string? imagePath = (imgIdx >= 0 && imgIdx < ImagePaths.Count) ? ImagePaths[imgIdx] : null;

                Image? loadedImage = null;
                try
                {
                    if (!string.IsNullOrEmpty(imagePath) && File.Exists(imagePath))
                        loadedImage = _imageService.LoadOrGetCachedImage(imagePath);
                }
                catch (Exception ex)
                {
                    StartupHandler.WriteStartupLog($"[DisplayManager] render load failed imagePath={imagePath} error={ex.GetType().Name}");
                    loadedImage = null;
                }

                if (token.IsCancellationRequested || generation != System.Threading.Volatile.Read(ref _renderGeneration))
                {
                    loadedImage?.Dispose();
                    return;
                }

                QueueAssignImage(generation, i, loadedImage);
            }

            int prefetchCount = DisplayCount == 2 ? 8 : 16;
            int nextIdx = startIndex + count;
            if (nextIdx < ImagePaths.Count)
            {
                System.Threading.Tasks.Task.Run(() =>
                {
                    for (int i = 0; i < prefetchCount && (nextIdx + i) < ImagePaths.Count; i++)
                    {
                        if (token.IsCancellationRequested || generation != System.Threading.Volatile.Read(ref _renderGeneration))
                            return;
                        _imageService.LoadOrGetCachedImage(ImagePaths[nextIdx + i]);
                    }
                }, token);
            }
        }

        /// <summary>
        /// Display images starting from the given index.
        /// </summary>
        public void DisplayImages(int startIndex)
        {
            StartupHandler.WriteStartupLog(string.Format("[DisplayManager] DisplayImages(startIndex={0}, DisplayCount={1}, ImagePaths.Count={2})",
                startIndex, DisplayCount, ImagePaths != null ? ImagePaths.Count : 0));


            CurrentIndex = startIndex;
            int count = DisplayCount;

            if (pictureBoxes.Length == 0 || pictureBoxes.Length != count)
                InitializePictureBoxes();

            StartupHandler.WriteStartupLog($"[DisplayManager] pictureBoxes.Length={pictureBoxes.Length}, count={count}");

            CancelRender();
            var renderCts = new System.Threading.CancellationTokenSource();
            _renderCts = renderCts;
            int generation = System.Threading.Interlocked.Increment(ref _renderGeneration);

            ClearPictureBoxes();

            if (ImagePaths.Count == 0)
                return;

            StartupHandler.WriteStartupLog($"[DisplayManager] render generation={generation} cols={(count == 8 ? 4 : (count == 1 ? 1 : 2))}");
            _ = System.Threading.Tasks.Task.Run(() => RenderImagesAsync(generation, startIndex, renderCts.Token), renderCts.Token);
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
                if (imgIdx >= 0 && imgIdx < ImagePaths.Count)
                    pageNumbers.Add(FolderService.ExtractNumberFromFileName(ImagePaths[imgIdx]));
            }

            string pagesText;
            if (count == 2 && pageNumbers.Count >= 2)
                pagesText = $"右: {pageNumbers[0]} | 左: {pageNumbers[1]}";
            else if (count == 2 && pageNumbers.Count > 0)
                pagesText = $"右: {pageNumbers[0]}";
            else
                pagesText = string.Join(" / ", pageNumbers);

            int spreadIndex = (CurrentIndex < 0) ? 1 : (CurrentIndex / count + 1);
            int totalSpreads = (ImagePaths.Count + count - 1) / count;

            return $"{folderInfo}{folderIndexInfo}{pagesText} | {spreadIndex}/{totalSpreads}";
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
            CancelRender();
            if (pictureBoxes != null)
                DisposePictureBoxes();
            _renderCts?.Dispose();
            _renderCts = null;
        }
    }
}

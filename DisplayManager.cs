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
        private Settings _settings;
        private ImageService _imageService;

        /// <summary>PictureBox array</summary>
        internal PictureBox[] pictureBoxes;

        /// <summary>Current images displayed in each PictureBox</summary>
        private Image[]? currentImages;

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
            currentImages = null;
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
            currentImages = new Image[count];

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
            CurrentIndex = startIndex;
            int count = DisplayCount;

            if (pictureBoxes.Length == 0 || pictureBoxes.Length != count)
                InitializePictureBoxes();

            if (ImagePaths.Count == 0)
            {
                foreach (var pb in pictureBoxes) pb.Image = null;
                return;
            }

            for (int i = 0; i < count; i++)
            {
                int imgIdx = startIndex + i;
                string? imagePath = imgIdx < ImagePaths.Count ? ImagePaths[imgIdx] : null;
                LoadImageIntoPictureBox(pictureBoxes[i], ref currentImages![i], imagePath);
            }

            // Preload next batch into cache
            int prefetchCount = count;
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
        public string GetInfoText(string currentFolder, int folderIndex, List<string> folderList)
        {
            if (ImagePaths.Count == 0) return "";

            int count = DisplayCount;
            string folderName = Path.GetFileName(currentFolder);
            string folderInfo = !string.IsNullOrEmpty(folderName) ? $"[{folderName}] " : "";
            string folderIndexInfo = folderList.Count > 1 ? $"{folderIndex + 1}/{folderList.Count}話 " : "";

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
                currentImage = _imageService.LoadOrGetCachedImage(imagePath);
                pb.Image = currentImage;
            }
            catch (OutOfMemoryException)
            {
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
        public Rectangle[] CalculatePictureBoxBounds(int clientWidth, int clientHeight, out Rectangle listPanelBounds)
        {
            int gap = 2;
            int count = DisplayCount;

            double imageAreaRatio, _;
            if (count == 2)
            {
                imageAreaRatio = (double)(Constants.RatioLeftImg + Constants.RatioRightImg) / Constants.TotalRatio;
            }
            else
            {
                imageAreaRatio = 0.85;
            }

            int imageTotalWidth = (int)(clientWidth * imageAreaRatio);
            int listW = clientWidth - imageTotalWidth;
            int height = clientHeight - 30;

            int rows, cols;
            if (count == 8) { rows = 4; cols = 2; }
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

        public void Dispose()
        {
            if (pictureBoxes != null)
                DisposePictureBoxes();
            currentImages = null;
        }
    }
}
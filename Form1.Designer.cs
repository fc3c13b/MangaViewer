namespace MangaViewer
{
    partial class Form1
    {
        /// <summary>
        /// 必要なデザイナー変数です。
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// 使用中のリソースをすべてクリーンアップします。
        /// </summary>
        /// <param name="disposing">マネージド リソースが破棄される場合 true、その他の場合は false。</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (components != null)
                {
                    components.Dispose();
                }
                // Dispose current images to prevent memory leak on form close
                if (currentImageRight != null)
                {
                    currentImageRight.Dispose();
                    currentImageRight = null;
                }
                if (currentImageLeft != null)
                {
                    currentImageLeft.Dispose();
                    currentImageLeft = null;
                }
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer で生成されたコード

        /// <summary>
        /// デザイナー サポートに必要なメソッドです。
        /// このメソッドの内容をコード エディターで変更しないでください。
        /// </summary>
        private void InitializeComponent()
        {
            this.SuspendLayout();
            // 
            // Form1
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(800, 450);
            this.Name = "Form1";
            this.Text = "Form1";
            this.ResumeLayout(false);

        }

        #endregion
    }
}
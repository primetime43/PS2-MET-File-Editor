using System.Drawing;
using System.Windows.Forms;

namespace PS2_DATA_File_Extractor
{
    public partial class ImageViewer : Form
    {
        public ImageViewer()
        {
            InitializeComponent();
            pictureBox1.SizeMode = PictureBoxSizeMode.Zoom;
        }

        public void SetImage(Image image)
        {
            pictureBox1.Image = image;
        }
    }
}

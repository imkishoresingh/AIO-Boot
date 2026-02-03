using System.Collections.Generic;
using System.Linq;
using System.Windows;

namespace AIOWIMCreatorGUI
{
    public partial class SelectImagesWindow : Window
    {
        public List<ImageInfo> AllImages { get; set; }
        public List<ImageInfo> SelectedImages => AllImages.Where(i => i.Selected).ToList();

        public SelectImagesWindow(List<ImageInfo> images)
        {
            InitializeComponent();
            AllImages = images;
            ImagesListBox.ItemsSource = AllImages;
        }

        private void OKButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }
    }
}
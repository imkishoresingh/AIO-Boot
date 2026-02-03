using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.IO;
using Microsoft.Win32;
using System.Windows.Media;

namespace AIOWIMCreatorGUI
{
    public partial class BuildWindow : Window
    {
        public List<SourceItem> SelectedSources { get; set; }
        public string DestinationPath { get; set; } = "";

        private string placeholder = "Enter destination WIM path (e.g., C:\\path\\to\\install.wim)";

        public BuildWindow(List<SourceItem> selectedSources)
        {
            InitializeComponent();
            SelectedSources = selectedSources;
            ImagesListBox.ItemsSource = SelectedSources;
            DestTextBlock.Text = placeholder;
            DestTextBlock.Foreground = Brushes.Gray;
        }

        private void BrowseButton_Click(object sender, RoutedEventArgs e)
        {
            SaveFileDialog dialog = new SaveFileDialog
            {
                Filter = "WIM files (*.wim)|*.wim",
                DefaultExt = "wim",
                FileName = "install.wim"
            };
            if (dialog.ShowDialog() == true)
            {
                DestTextBlock.Text = dialog.FileName;
            }
        }

        private void DestTextBlock_GotFocus(object sender, RoutedEventArgs e)
        {
            if (DestTextBlock.Text == placeholder)
            {
                DestTextBlock.Text = "";
                DestTextBlock.Foreground = Brushes.Black;
            }
        }

        private void DestTextBlock_LostFocus(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(DestTextBlock.Text))
            {
                DestTextBlock.Text = placeholder;
                DestTextBlock.Foreground = Brushes.Gray;
            }
        }

        private void BuildButton_Click(object sender, RoutedEventArgs e)
        {
            string dest = DestTextBlock.Text.Trim();
            if (string.IsNullOrEmpty(dest) || dest == "No path selected" || dest.EndsWith("\\") || !Path.HasExtension(dest))
            {
                MessageBox.Show("Please select a valid destination file path using the Browse button.", "Invalid Path", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            DestinationPath = Path.GetFullPath(dest);
            DialogResult = true;
        }
    }
}
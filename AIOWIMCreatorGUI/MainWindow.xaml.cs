using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;

namespace AIOWIMCreatorGUI
{
    public partial class MainWindow : Window
    {
        public ObservableCollection<SourceItem> Sources { get; set; } = new ObservableCollection<SourceItem>();

        public MainWindow()
        {
            InitializeComponent();
            SourcesListBox.ItemsSource = Sources;
        }

        private async void AddWIMButton_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog dialog = new OpenFileDialog { Filter = "WIM and ESD files (*.wim;*.esd)|*.wim;*.esd" };
            if (dialog.ShowDialog() == true)
            {
                string path = dialog.FileName;
                var (success, images, error) = await LoadImagesFromFile(path);
                if (success && images.Any())
                {
                    SelectImagesWindow selectWindow = new SelectImagesWindow(images);
                    if (selectWindow.ShowDialog() == true)
                    {
                        foreach (var img in selectWindow.SelectedImages)
                        {
                            Sources.Add(new SourceItem { Path = path, Index = img.Index, Name = img.Name, Description = img.Description });
                        }
                        UpdateBuildButton();
                    }
                }
                else
                {
                    string msg = success ? "No valid images found in the file." : $"Failed to load images: {ParseError(error)}";
                    MessageBox.Show(msg, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private async Task<(bool success, List<ImageInfo> images, string error)> LoadImagesFromFile(string path)
        {
            return await Task.Run(() =>
            {
                string args = $"/Get-ImageInfo /ImageFile:\"{path}\"";
                var (success, output, error) = RunDISM(args);
                List<ImageInfo> images = new List<ImageInfo>();
                if (success)
                {
                    ParseImages(output, images);
                }
                return (success, images, error);
            });
        }

        private void UpdateBuildButton()
        {
            BuildButton.IsEnabled = Sources.Any() && Sources.All(s => string.IsNullOrEmpty(s.Error));
        }

        private void BuildButton_Click(object sender, RoutedEventArgs e)
        {
            var selectedSources = Sources.ToList();
            BuildWindow buildWindow = new BuildWindow(selectedSources);
            if (buildWindow.ShowDialog() == true)
            {
                var exports = selectedSources.Select(s => (s.Path, new ImageInfo { Index = s.Index, Name = s.Name, Description = s.Description })).ToList();
                OutputWindow outputWindow = new OutputWindow(buildWindow.DestinationPath, exports);
                outputWindow.Show();
            }
        }

        private void RemoveButton_Click(object sender, RoutedEventArgs e)
        {
            if (SourcesListBox.SelectedItem != null)
            {
                Sources.Remove((SourceItem)SourcesListBox.SelectedItem);
                UpdateBuildButton();
            }
        }

        private (bool success, string output, string error) RunDISM(string arguments)
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo
                {
                    FileName = "DISM.exe",
                    Arguments = arguments,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    Verb = "runas"
                };

                using (Process process = Process.Start(psi))
                {
                    string output = process.StandardOutput.ReadToEnd();
                    string error = process.StandardError.ReadToEnd();
                    process.WaitForExit();

                    bool success = process.ExitCode == 0;
                    return (success, output, error);
                }
            }
            catch (Exception ex)
            {
                return (false, "", ex.Message);
            }
        }

        private string ParseError(string result)
        {
            // Simple parsing for human-readable errors
            if (result.Contains("0x80070002")) return "File not found.";
            if (result.Contains("0x80070005")) return "Access denied. Run as administrator.";
            if (result.Contains("0xc1420117")) return "Invalid WIM file.";
            return "Unknown error: " + result;
        }

        private void ParseImages(string output, List<ImageInfo> images)
        {
            var lines = output.Split('\n');
            ImageInfo current = null;
            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                int colonIndex = trimmed.IndexOf(':');
                if (colonIndex > 0)
                {
                    var key = trimmed.Substring(0, colonIndex).Trim();
                    var value = trimmed.Substring(colonIndex + 1).Trim();
                    if (key == "Index" && int.TryParse(value, out int index))
                    {
                        if (current != null) images.Add(current);
                        current = new ImageInfo { Index = index };
                    }
                    else if (key == "Name" && current != null)
                    {
                        current.Name = value;
                    }
                    else if (key == "Description" && current != null)
                    {
                        current.Description = value;
                    }
                }
            }
            if (current != null) images.Add(current);
        }
    }

    public class SourceItem
    {
        public string Path { get; set; }
        public int Index { get; set; }
        public ObservableCollection<ImageInfo> Images { get; set; } = new ObservableCollection<ImageInfo>();
        public string Error { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
    }

    public class ImageInfo
    {
        public int Index { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public bool Selected { get; set; }
    }
}
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Text.RegularExpressions;
using System.IO;
using DiscUtils.Iso9660;
using DiscUtils.Udf;

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

        private static void AddFsEntries(DiscUtils.DiscFileSystem fs, List<string> dirs, List<string> files)
        {
            // Manual recursive enumeration - DiscUtils SearchOption.AllDirectories doesn't always work
            void EnumerateRecursive(string path)
            {
                try
                {
                    foreach (var dir in fs.GetDirectories(path))
                    {
                        var normalized = dir.Replace('\\', '/').Trim('/');
                        if (!string.IsNullOrEmpty(normalized))
                        {
                            dirs.Add(normalized);
                            EnumerateRecursive(dir);
                        }
                    }
                }
                catch { }

                try
                {
                    foreach (var file in fs.GetFiles(path))
                    {
                        var normalized = file.Replace('\\', '/').Trim('/');
                        if (!string.IsNullOrEmpty(normalized))
                        {
                            files.Add(normalized);
                        }
                    }
                }
                catch { }
            }

            EnumerateRecursive("");
        }

        private void UpdateBuildButton()
        {
            BuildButton.IsEnabled = Sources.Any() && Sources.All(s => string.IsNullOrEmpty(s.Error));
        }

        private void BuildButton_Click(object sender, RoutedEventArgs e)
        {
            var selectedSources = Sources.ToList();
            var tempPaths = selectedSources
                .Where(s => s.IsTemp)
                .Select(s => Path.GetDirectoryName(s.Path))
                .Where(p => !string.IsNullOrEmpty(p))
                .Select(p => p!)
                .Distinct()
                .ToList();
            BuildWindow buildWindow = new BuildWindow(selectedSources);
            if (buildWindow.ShowDialog() == true)
            {
                var exports = selectedSources.Select(s => (s.Path, new ImageInfo { Index = s.Index, Name = s.Name, Description = s.Description })).ToList();
                OutputWindow outputWindow = new OutputWindow(buildWindow.DestinationPath, exports, tempPaths);
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
                var indexMatch = Regex.Match(trimmed, @"Index\s*:\s*(\d+)");
                if (indexMatch.Success)
                {
                    if (current != null) images.Add(current);
                    current = new ImageInfo { Index = int.Parse(indexMatch.Groups[1].Value) };
                }
                else
                {
                    var nameMatch = Regex.Match(trimmed, @"(Name|Image Name)\s*:\s*(.+)");
                    if (nameMatch.Success && current != null)
                    {
                        current.Name = nameMatch.Groups[2].Value.Trim();
                    }
                    var descMatch = Regex.Match(trimmed, @"Description\s*:\s*(.+)");
                    if (descMatch.Success && current != null)
                    {
                        current.Description = descMatch.Groups[1].Value.Trim();
                    }
                }
            }
            if (current != null) images.Add(current);
        }

        private async void AddFromISOButton_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog dialog = new OpenFileDialog { Filter = "ISO files (*.iso)|*.iso" };
            if (dialog.ShowDialog() == true)
            {
                string isoPath = dialog.FileName;
                List<string> isoDirs = new List<string>();
                List<string> isoFiles = new List<string>();
                try
                {
                    using (FileStream fs = File.OpenRead(isoPath))
                    {
                        // Prefer UDF: modern Windows ISOs are typically UDF.
                        UdfReader udf = new UdfReader(fs);
                        AddFsEntries(udf, isoDirs, isoFiles);

                        // Fallback: try ISO9660 (some images may contain only ISO9660)
                        if (isoDirs.Count == 0 && isoFiles.Count == 0)
                        {
                            fs.Position = 0;
                            CDReader iso = new CDReader(fs, true, true);
                            AddFsEntries(iso, isoDirs, isoFiles);
                        }
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Failed to read ISO: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                isoDirs = isoDirs
                    .Where(p => !string.IsNullOrWhiteSpace(p))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                isoFiles = isoFiles
                    .Where(p => !string.IsNullOrWhiteSpace(p))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                if (isoDirs.Count == 0 && isoFiles.Count == 0)
                {
                    MessageBox.Show("No files found in the ISO.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                SelectFileWindow selectWindow = new SelectFileWindow(isoDirs, isoFiles);
                if (selectWindow.ShowDialog() == true)
                {
                    string selectedFile = selectWindow.SelectedFile;
                    string tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
                    Directory.CreateDirectory(tempDir);
                    string tempWimPath = Path.Combine(tempDir, Path.GetFileName(selectedFile));
                    try
                    {
                        using (FileStream isoStream = File.OpenRead(isoPath))
                        {
                            Stream source = null;
                            // Try different path formats
                            string[] pathVariants = new string[]
                            {
                                selectedFile,
                                "\\" + selectedFile,
                                "/" + selectedFile,
                                selectedFile.Replace('/', '\\'),
                                "\\" + selectedFile.Replace('/', '\\')
                            };

                            UdfReader udf = new UdfReader(isoStream);
                            foreach (var pathVariant in pathVariants)
                            {
                                try
                                {
                                    source = udf.OpenFile(pathVariant, FileMode.Open);
                                    break;
                                }
                                catch { }
                            }

                            if (source == null)
                            {
                                isoStream.Position = 0;
                                CDReader iso = new CDReader(isoStream, true, true);
                                foreach (var pathVariant in pathVariants)
                                {
                                    try
                                    {
                                        source = iso.OpenFile(pathVariant, FileMode.Open);
                                        break;
                                    }
                                    catch { }
                                }
                            }

                            if (source == null)
                            {
                                throw new FileNotFoundException($"Could not open file with any path variant. Selected: {selectedFile}");
                            }

                            using (source)
                            using (FileStream dest = File.Create(tempWimPath))
                            {
                                source.CopyTo(dest);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"Failed to extract file: {ex.Message}\n\nPath tried: {selectedFile}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                        Directory.Delete(tempDir, true);
                        return;
                    }
                    var (success, images, error) = await LoadImagesFromFile(tempWimPath);
                    if (success && images.Any())
                    {
                        SelectImagesWindow selectImgWindow = new SelectImagesWindow(images);
                        if (selectImgWindow.ShowDialog() == true)
                        {
                            foreach (var img in selectImgWindow.SelectedImages)
                            {
                                Sources.Add(new SourceItem { Path = tempWimPath, Index = img.Index, Name = img.Name, Description = img.Description, IsTemp = true });
                            }
                            UpdateBuildButton();
                        }
                        else
                        {
                            Directory.Delete(tempDir, true);
                        }
                    }
                    else
                    {
                        string msg = success ? "No valid images found in the file." : $"Failed to load images: {ParseError(error)}";
                        MessageBox.Show(msg, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                        Directory.Delete(tempDir, true);
                    }
                }
            }
        }

    }

    public class SourceItem
    {
        public string Path { get; set; } = string.Empty;
        public int Index { get; set; }
        public ObservableCollection<ImageInfo> Images { get; set; } = new ObservableCollection<ImageInfo>();
        public string Error { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public bool IsTemp { get; set; } = false;
    }

    public class ImageInfo
    {
        public int Index { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public bool Selected { get; set; }
    }
}
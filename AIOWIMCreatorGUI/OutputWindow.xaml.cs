using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Collections.Generic;

namespace AIOWIMCreatorGUI
{
    public partial class OutputWindow : Window
    {
        private List<string> tempPaths;

        public OutputWindow(string destPath, List<(string sourcePath, ImageInfo image)> exports, List<string> tempPaths)
        {
            this.tempPaths = tempPaths;
            InitializeComponent();
            ProgressBar.Visibility = Visibility.Visible;
            ProgressTextBox.Text = "Initializing build...\n";
            // Ensure destination directory exists
            string dir = Path.GetDirectoryName(destPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }
            // Delete existing destination file if it exists to start fresh
            if (File.Exists(destPath))
            {
                try
                {
                    File.Delete(destPath);
                }
                catch (Exception ex)
                {
                    ProgressTextBox.Text += $"Error deleting existing destination file: {ex.Message}\nBuild cancelled.\n";
                    return;
                }
            }
            ProgressTextBox.Text += "Starting build...\n";
            RunBuildAsync(destPath, exports);
        }

        private async void RunBuildAsync(string destPath, List<(string sourcePath, ImageInfo image)> exports)
        {
            try
            {
                ProgressTextBox.Text += "Building AIO WIM...\n";

                for (int i = 0; i < exports.Count; i++)
                {
                    var (sourcePath, image) = exports[i];
                    var (success, output, error) = RunDISM($"/Export-Image /SourceImageFile:\"{sourcePath}\" /SourceIndex:{image.Index} /DestinationImageFile:\"{destPath}\" /Compress:max /CheckIntegrity");
                    if (!success)
                    {
                        ProgressTextBox.Text += $"Failed: {ParseError(error)}\n";
                        return;
                    }
                }

                ProgressTextBox.Text += "Success!\n";
                ProgressBar.Visibility = Visibility.Hidden;
                MessageBoxResult result = MessageBox.Show("Build completed successfully. Do you want to open the output folder?", "Completed", MessageBoxButton.YesNo, MessageBoxImage.Information);
                if (result == MessageBoxResult.Yes)
                {
                    Process.Start("explorer.exe", Path.GetDirectoryName(destPath));
                }
                // Delete temp folders
                foreach (var temp in tempPaths)
                {
                    try
                    {
                        Directory.Delete(temp, true);
                    }
                    catch (Exception ex)
                    {
                        ProgressTextBox.Text += $"Warning: Failed to delete temp folder {temp}: {ex.Message}\n";
                    }
                }
            }
            catch (Exception ex)
            {
                ProgressTextBox.Text += $"Unexpected error: {ex.Message}\n";
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
                    if (!success && string.IsNullOrEmpty(error))
                    {
                        error = output; // Sometimes DISM outputs errors to stdout
                    }
                    return (success, output, error);
                }
            }
            catch (Exception ex)
            {
                return (false, "", ex.Message);
            }
        }

        private string ParseError(string error)
        {
            // Simple parsing for human-readable errors
            if (error.Contains("0x80070002")) return "File not found.";
            if (error.Contains("0x80070005")) return "Access denied. Run as administrator.";
            if (error.Contains("0xc1420117")) return "Invalid WIM file.";
            if (error.Contains("0x800f0806")) return "Source image not found or invalid.";
            if (error.Contains("0xc1420127")) return "The image file is corrupted.";
            if (error.Contains("0x80070020")) return "The file is being used by another process.";
            if (error.Contains("0x8007007b")) return "Invalid file name or path.";
            return "Unknown error: " + error;
        }
    }
}
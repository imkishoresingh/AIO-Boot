using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace AIOWIMCreatorGUI
{
    public partial class SelectFileWindow : Window
    {
        private readonly List<string> dirEntries;
        private readonly List<string> fileEntries;
        private readonly HashSet<string> directorySet;

        public string? SelectedFile { get; private set; }

        public SelectFileWindow(List<string> directories, List<string> files)
        {
            InitializeComponent();
            dirEntries = (directories ?? new List<string>())
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .Select(NormalizePath)
                .Where(p => p.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            fileEntries = (files ?? new List<string>())
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .Select(NormalizePath)
                .Where(p => p.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            directorySet = new HashSet<string>(dirEntries, StringComparer.OrdinalIgnoreCase);
            BuildFolderTree();
            SelectRoot();
        }

        private static string NormalizePath(string path)
        {
            return path.Replace('\\', '/').Trim('/');
        }

        private sealed class FolderNode
        {
            public string Name { get; }
            public string FullPath { get; }
            public Dictionary<string, FolderNode> Children { get; } = new Dictionary<string, FolderNode>(StringComparer.OrdinalIgnoreCase);

            public FolderNode(string name, string fullPath)
            {
                Name = name;
                FullPath = fullPath;
            }
        }

        private sealed class FileItem
        {
            public string Name { get; }
            public string FullPath { get; } // original path from DiscUtils
            public string NormalizedPath { get; } // normalized for UI matching
            public string Type { get; }

            public FileItem(string name, string fullPath, string normalizedPath)
            {
                Name = name;
                FullPath = fullPath;
                NormalizedPath = normalizedPath;
                Type = System.IO.Path.GetExtension(name).TrimStart('.').ToLowerInvariant();
            }
        }

        private void BuildFolderTree()
        {
            var rootNode = new FolderNode("/", "");

            // Include explicit directory entries
            foreach (var dir in dirEntries)
            {
                AddPathToTree(rootNode, dir, isDirectory: true);
            }

            // Also derive directories from file paths
            foreach (var file in fileEntries)
            {
                int idx = file.LastIndexOf('/');
                while (idx > 0)
                {
                    var parent = file.Substring(0, idx);
                    AddPathToTree(rootNode, parent, isDirectory: true);
                    idx = parent.LastIndexOf('/');
                }
            }

            FoldersTree.Items.Clear();
            var rootItem = CreateTreeItem(rootNode);
            rootItem.IsExpanded = true;
            FoldersTree.Items.Add(rootItem);
        }

        private static void AddPathToTree(FolderNode root, string path, bool isDirectory)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            var normalized = NormalizePath(path);
            var parts = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0)
            {
                return;
            }

            var current = root;
            for (int i = 0; i < parts.Length; i++)
            {
                var folderName = parts[i];
                var nextPath = string.IsNullOrEmpty(current.FullPath) ? folderName : current.FullPath + "/" + folderName;
                if (!current.Children.TryGetValue(folderName, out var next))
                {
                    next = new FolderNode(folderName, nextPath);
                    current.Children[folderName] = next;
                }
                current = next;
            }
        }

        private TreeViewItem CreateTreeItem(FolderNode node)
        {
            var tvi = new TreeViewItem
            {
                Header = node.Name,
                Tag = node.FullPath
            };

            foreach (var child in node.Children.Values.OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase))
            {
                tvi.Items.Add(CreateTreeItem(child));
            }

            return tvi;
        }

        private void SelectRoot()
        {
            if (FoldersTree.Items.Count == 0)
            {
                return;
            }

            var root = (TreeViewItem)FoldersTree.Items[0];
            root.IsSelected = true;
            PopulateFilesForFolder("");
        }

        private void PopulateFilesForFolder(string folder)
        {
            CurrentPathTextBox.Text = string.IsNullOrEmpty(folder) ? "/" : "/" + folder;

            string prefix = string.IsNullOrEmpty(folder) ? "" : folder.TrimEnd('/') + "/";
            var items = new List<FileItem>();

            foreach (var file in fileEntries)
            {
                var normalized = NormalizePath(file);
                if (!normalized.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var remainder = normalized.Substring(prefix.Length);
                if (remainder.Contains('/'))
                {
                    continue; // not in this folder
                }

                items.Add(new FileItem(remainder, file, normalized));
            }

            FilesList.ItemsSource = items.OrderBy(i => i.Name, StringComparer.OrdinalIgnoreCase).ToList();
            SelectedFile = null;
            OpenButton.IsEnabled = false;
        }

        private static bool IsWimOrEsd(string fullPath)
        {
            return fullPath.EndsWith(".wim", StringComparison.OrdinalIgnoreCase)
                || fullPath.EndsWith(".esd", StringComparison.OrdinalIgnoreCase);
        }

        private void FoldersTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            if (FoldersTree.SelectedItem is TreeViewItem tvi)
            {
                PopulateFilesForFolder((string)(tvi.Tag ?? ""));
            }
        }

        private void FilesList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (FilesList.SelectedItem is FileItem file && IsWimOrEsd(file.FullPath))
            {
                SelectedFile = file.FullPath; // use original path for extraction
                DialogResult = true;
            }
        }

        private void FilesList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (FilesList.SelectedItem is FileItem file)
            {
                OpenButton.IsEnabled = IsWimOrEsd(file.FullPath);
            }
            else
            {
                OpenButton.IsEnabled = false;
            }
        }

        private void OKButton_Click(object sender, RoutedEventArgs e)
        {
            if (FilesList.SelectedItem is FileItem file && IsWimOrEsd(file.FullPath))
            {
                SelectedFile = file.FullPath; // use original path for extraction
                DialogResult = true;
            }
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }
    }
}
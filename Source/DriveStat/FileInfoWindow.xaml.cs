namespace DriveStat;

/// <summary>
/// Window displaying detailed directory or file information, styled like Process Explorer Properties with Mica.
/// </summary>
public sealed partial class FileInfoWindow : Window
{
    private readonly DirectoryNode _node;
    private readonly Dictionary<FileAttributes, CheckBox> _attributeCheckBoxes = [];

    public FileInfoWindow(DirectoryNode node)
    {
        InitializeComponent();
        _node = node;
        WindowChrome.TryApplyMica(this);
        WindowChrome.TryExtendTitleBar(this, AppTitleBar);
        Title = $"{node.Name} — Properties";
        TitleText.Text = $"{node.Name} — Properties";
        ItemIconText.Text = node.IsDirectory ? "📁" : "📄";
        PopulateGeneral(node);
        PopulateIcons(node);
        PopulateDetails(node);
        PopulateSecurity(node);
        PopulateSignatures(node);
    }

    private void PopulateGeneral(DirectoryNode node)
    {
        ItemNameText.Text = node.Name;
        ItemTypeText.Text = node.IsDirectory ? "File folder" : $"File ({node.Extension})";
        PathBox.Text = node.FullPath;

        AddPropertyRow("Size", DirectoryNode.FormatSize(node.Size));

        if (!node.IsDirectory && !string.IsNullOrEmpty(node.Extension))
        {
            AddPropertyRow("Extension", node.Extension);
        }

        try
        {
            if (node.IsDirectory && Directory.Exists(node.FullPath))
            {
                var dirInfo = new DirectoryInfo(node.FullPath);
                AddPropertyRow("Created", dirInfo.CreationTime.ToString("yyyy-MM-dd hh:mm:ss tt"));
                AddPropertyRow("Last modified", dirInfo.LastWriteTime.ToString("yyyy-MM-dd hh:mm:ss tt"));
                AddPropertyRow("Last accessed", dirInfo.LastAccessTime.ToString("yyyy-MM-dd hh:mm:ss tt"));
                AddPropertyRow("Contains", $"{node.Children.Count:N0} items");
                PopulateAttributes(dirInfo.Attributes);
                AttributesSection.Visibility = Visibility.Visible;
                ApplyButton.Visibility = Visibility.Visible;
            }
            else if (!node.IsDirectory && File.Exists(node.FullPath))
            {
                var fileInfo = new FileInfo(node.FullPath);
                AddPropertyRow("Created", fileInfo.CreationTime.ToString("yyyy-MM-dd hh:mm:ss tt"));
                AddPropertyRow("Last modified", fileInfo.LastWriteTime.ToString("yyyy-MM-dd hh:mm:ss tt"));
                AddPropertyRow("Last accessed", fileInfo.LastAccessTime.ToString("yyyy-MM-dd hh:mm:ss tt"));
                PopulateAttributes(fileInfo.Attributes);
                AttributesSection.Visibility = Visibility.Visible;
                ApplyButton.Visibility = Visibility.Visible;
            }
        }
        catch (UnauthorizedAccessException)
        {
            AddPropertyRow("Note", "Some details unavailable (access denied)");
        }
        catch (Exception ex)
        {
            AddPropertyRow("Note", $"Could not read details: {ex.Message}");
        }
    }

    private void AddPropertyRow(string label, string value)
    {
        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120, GridUnitType.Pixel) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var labelBlock = new TextBlock
        {
            Text = $"{label}:",
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 0, 16, 0)
        };
        var valueBlock = new TextBlock
        {
            Text = value,
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            IsTextSelectionEnabled = true
        };
        Grid.SetColumn(labelBlock, 0);
        Grid.SetColumn(valueBlock, 1);
        row.Children.Add(labelBlock);
        row.Children.Add(valueBlock);
        PropertiesPanel.Children.Add(row);
    }

    private void PopulateAttributes(FileAttributes attributes)
    {
        _attributeCheckBoxes.Clear();
        AttributesPanel.Children.Clear();

        var attrs = new[] { FileAttributes.ReadOnly, FileAttributes.Hidden, FileAttributes.Archive, FileAttributes.System };
        foreach (var attr in attrs)
        {
            var cb = new CheckBox
            {
                Content = attr.ToString(),
                IsChecked = (attributes & attr) != 0
            };
            cb.Checked += (_, _) => ApplyButton.IsEnabled = true;
            cb.Unchecked += (_, _) => ApplyButton.IsEnabled = true;
            _attributeCheckBoxes[attr] = cb;
            AttributesPanel.Children.Add(cb);
        }
        ApplyButton.IsEnabled = false;
    }

    private void ApplyButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var current = File.GetAttributes(_node.FullPath);

            var editable = FileAttributes.ReadOnly | FileAttributes.Hidden | FileAttributes.Archive | FileAttributes.System;
            var preserved = current & ~editable;
            var fromCheckboxes = (FileAttributes)0;
            foreach (var (attr, cb) in _attributeCheckBoxes)
            {
                if (cb.IsChecked == true)
                {
                    fromCheckboxes |= attr;
                }
            }
            var newAttrs = preserved | fromCheckboxes;

            File.SetAttributes(_node.FullPath, newAttrs);

            ApplyButton.IsEnabled = false;
        }
        catch (UnauthorizedAccessException)
        {
            _ = ShowMessageAsync("Cannot change attributes. The file may be in use or you may need administrator rights.");
        }
        catch (Exception ex)
        {
            _ = ShowMessageAsync($"Could not apply attributes: {ex.Message}");
        }
    }

    private async System.Threading.Tasks.Task ShowMessageAsync(string message)
    {
        var dialog = new ContentDialog
        {
            Title = "Attributes",
            Content = message,
            CloseButtonText = "OK",
            XamlRoot = Content.XamlRoot
        };
        await dialog.ShowAsync();
    }

    private async void PopulateIcons(DirectoryNode node)
    {
        IconsPanel.Children.Clear();

        if (!File.Exists(node.FullPath) && !Directory.Exists(node.FullPath))
        {
            IconsPanel.Children.Add(new TextBlock { Text = "File or folder not found.", Opacity = 0.8, FontSize = 12 });
            return;
        }

        try
        {
            if (node.IsDirectory)
            {
                var folder = await Windows.Storage.StorageFolder.GetFolderFromPathAsync(node.FullPath);
                await AddIconFromStorageAsync(IconsPanel, folder.GetThumbnailAsync(Windows.Storage.FileProperties.ThumbnailMode.SingleItem, 48), "Small (48×48)");
                await AddIconFromStorageAsync(IconsPanel, folder.GetThumbnailAsync(Windows.Storage.FileProperties.ThumbnailMode.SingleItem, 256), "Large (256×256)");
            }
            else
            {
                var file = await Windows.Storage.StorageFile.GetFileFromPathAsync(node.FullPath);
                await AddIconFromStorageAsync(IconsPanel, file.GetThumbnailAsync(Windows.Storage.FileProperties.ThumbnailMode.SingleItem, 48), "Small (48×48)");
                await AddIconFromStorageAsync(IconsPanel, file.GetThumbnailAsync(Windows.Storage.FileProperties.ThumbnailMode.SingleItem, 256), "Large (256×256)");
            }
        }
        catch (Exception ex)
        {
            IconsPanel.Children.Add(new TextBlock
            {
                Text = $"Could not load icon: {ex.Message}",
                TextWrapping = TextWrapping.Wrap,
                Opacity = 0.8,
                FontSize = 12
            });
        }

        if (IconsPanel.Children.Count == 0)
        {
            IconsPanel.Children.Add(new TextBlock { Text = "No icon available for this item.", Opacity = 0.8, FontSize = 12 });
        }
    }

    private static async System.Threading.Tasks.Task AddIconFromStorageAsync(StackPanel panel, Windows.Foundation.IAsyncOperation<Windows.Storage.FileProperties.StorageItemThumbnail> task, string label)
    {
        var thumbnail = await task;
        if (thumbnail == null || thumbnail.Size == 0)
        {
            return;
        }

        var size = label.Contains("256") ? 256 : 48;
        var bitmapImage = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage();
        await bitmapImage.SetSourceAsync(thumbnail.CloneStream());

        var section = new StackPanel { Spacing = 8 };
        section.Children.Add(new TextBlock { Text = label, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, FontSize = 12 });
        section.Children.Add(new Image
        {
            Source = bitmapImage,
            Width = size,
            Height = size,
            Stretch = Microsoft.UI.Xaml.Media.Stretch.Uniform
        });
        panel.Children.Add(section);
    }

    private void PopulateDetails(DirectoryNode node)
    {
        DetailsPanel.Children.Clear();
        AddDetailRow("Name", node.Name);
        AddDetailRow("Full path", node.FullPath);
        AddDetailRow("Type", node.IsDirectory ? "Directory" : "File");
        AddDetailRow("Size", $"{DirectoryNode.FormatSize(node.Size)} ({node.Size:N0} bytes)");
        AddDetailRow("Extension", string.IsNullOrEmpty(node.Extension) ? "(none)" : node.Extension);

        try
        {
            if (node.IsDirectory && Directory.Exists(node.FullPath))
            {
                var dirInfo = new DirectoryInfo(node.FullPath);
                AddDetailRow("Created", dirInfo.CreationTime.ToString("yyyy-MM-dd HH:mm:ss"));
                AddDetailRow("Last modified", dirInfo.LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss"));
                AddDetailRow("Last accessed", dirInfo.LastAccessTime.ToString("yyyy-MM-dd HH:mm:ss"));
                AddDetailRow("Attributes", dirInfo.Attributes.ToString());
                AddDetailRow("Child count", node.Children.Count.ToString("N0"));
            }
            else if (!node.IsDirectory && File.Exists(node.FullPath))
            {
                var fileInfo = new FileInfo(node.FullPath);
                AddDetailRow("Created", fileInfo.CreationTime.ToString("yyyy-MM-dd HH:mm:ss"));
                AddDetailRow("Last modified", fileInfo.LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss"));
                AddDetailRow("Last accessed", fileInfo.LastAccessTime.ToString("yyyy-MM-dd HH:mm:ss"));
                AddDetailRow("Attributes", fileInfo.Attributes.ToString());
            }
        }
        catch (UnauthorizedAccessException)
        {
            AddDetailRow("Note", "Some details unavailable (access denied)");
        }
        catch (Exception ex)
        {
            AddDetailRow("Note", $"Could not read details: {ex.Message}");
        }
    }

    private void AddDetailRow(string label, string value)
    {
        var section = new StackPanel { Spacing = 4 };
        section.Children.Add(new TextBlock
        {
            Text = label,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            FontSize = 11,
            Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 100, 100, 100))
        });
        section.Children.Add(new TextBlock
        {
            Text = value,
            TextWrapping = TextWrapping.Wrap,
            IsTextSelectionEnabled = true,
            FontSize = 12
        });
        DetailsPanel.Children.Add(section);
    }

    private void PopulateSecurity(DirectoryNode node)
    {
        SecurityPanel.Children.Clear();

        if (!File.Exists(node.FullPath) && !Directory.Exists(node.FullPath))
        {
            AddSecurityNote("File or folder not found.");
            return;
        }

        try
        {
            System.Security.AccessControl.FileSystemSecurity security;
            if (node.IsDirectory)
            {
                var dirInfo = new DirectoryInfo(node.FullPath);
                security = dirInfo.GetAccessControl();
            }
            else
            {
                var fileInfo = new FileInfo(node.FullPath);
                security = fileInfo.GetAccessControl();
            }

            var owner = security.GetOwner(typeof(System.Security.Principal.SecurityIdentifier));
            var ownerName = security.GetOwner(typeof(System.Security.Principal.NTAccount));
            AddSecurityRow("Owner", ownerName?.Value ?? owner?.Value ?? "(unknown)");

            SecurityPanel.Children.Add(new TextBlock
            {
                Text = "Permissions",
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                FontSize = 12,
                Margin = new Thickness(0, 16, 0, 4)
            });

            var rules = security.GetAccessRules(true, true, typeof(System.Security.Principal.NTAccount));
            foreach (System.Security.AccessControl.FileSystemAccessRule rule in rules)
            {
                var identity = rule.IdentityReference?.Value ?? "(unknown)";
                var rights = rule.FileSystemRights.ToString();
                var type = rule.AccessControlType == System.Security.AccessControl.AccessControlType.Allow ? "Allow" : "Deny";
                var inherited = rule.IsInherited ? " (inherited)" : "";
                AddSecurityRow($"{identity}", $"{type}: {rights}{inherited}");
            }
        }
        catch (UnauthorizedAccessException)
        {
            AddSecurityNote("Access denied. You may need administrator rights to view security settings.");
        }
        catch (Exception ex)
        {
            AddSecurityNote($"Could not read security: {ex.Message}");
        }
    }

    private void AddSecurityRow(string label, string value)
    {
        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140, GridUnitType.Pixel) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var labelBlock = new TextBlock
        {
            Text = $"{label}:",
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 0, 16, 0),
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        var valueBlock = new TextBlock
        {
            Text = value,
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            IsTextSelectionEnabled = true
        };
        Grid.SetColumn(labelBlock, 0);
        Grid.SetColumn(valueBlock, 1);
        row.Children.Add(labelBlock);
        row.Children.Add(valueBlock);
        SecurityPanel.Children.Add(row);
    }

    private void AddSecurityNote(string text)
    {
        SecurityPanel.Children.Add(new TextBlock
        {
            Text = text,
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.8,
            FontSize = 12
        });
    }

    private void PopulateSignatures(DirectoryNode node)
    {
        SignaturesPanel.Children.Clear();

        if (node.IsDirectory)
        {
            AddSignatureRow("Directories cannot be digitally signed.", isNote: true);
            return;
        }

        if (!File.Exists(node.FullPath))
        {
            AddSignatureRow("File not found.", isNote: true);
            return;
        }

        try
        {
#pragma warning disable SYSLIB0057 // CreateFromSignedFile is obsolete
            var cert = System.Security.Cryptography.X509Certificates.X509Certificate.CreateFromSignedFile(node.FullPath);
#pragma warning restore SYSLIB0057
            var cert2 = new System.Security.Cryptography.X509Certificates.X509Certificate2(cert);

            AddSignatureRow("Subject", cert2.Subject);
            AddSignatureRow("Issuer", cert2.Issuer);
            AddSignatureRow("Serial number", cert2.SerialNumber);
            AddSignatureRow("Valid from", cert2.NotBefore.ToString("yyyy-MM-dd HH:mm:ss"));
            AddSignatureRow("Valid to", cert2.NotAfter.ToString("yyyy-MM-dd HH:mm:ss"));
            AddSignatureRow("Thumbprint", cert2.Thumbprint);
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
            AddSignatureRow("This file is not digitally signed.", isNote: true);
        }
        catch (Exception ex)
        {
            AddSignatureRow($"Could not read signature: {ex.Message}", isNote: true);
        }
    }

    private void AddSignatureRow(string labelOrText, string? value = null, bool isNote = false)
    {
        if (isNote)
        {
            SignaturesPanel.Children.Add(new TextBlock
            {
                Text = labelOrText,
                TextWrapping = TextWrapping.Wrap,
                Opacity = 0.8,
                FontSize = 12
            });
            return;
        }

        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120, GridUnitType.Pixel) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var labelBlock = new TextBlock
        {
            Text = $"{labelOrText}:",
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 0, 16, 0)
        };
        var valueBlock = new TextBlock
        {
            Text = value ?? "",
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            IsTextSelectionEnabled = true
        };
        Grid.SetColumn(labelBlock, 0);
        Grid.SetColumn(valueBlock, 1);
        row.Children.Add(labelBlock);
        row.Children.Add(valueBlock);
        SignaturesPanel.Children.Add(row);
    }

    private async void RevealButton_Click(object sender, RoutedEventArgs e) => await RevealInExplorerAsync();
    private async void ExplorePathButton_Click(object sender, RoutedEventArgs e) => await RevealInExplorerAsync();

    private async System.Threading.Tasks.Task RevealInExplorerAsync()
    {
        try
        {
            if (_node.IsDirectory && Directory.Exists(_node.FullPath))
            {
                await Windows.System.Launcher.LaunchFolderPathAsync(_node.FullPath);
            }
            else if (!_node.IsDirectory && File.Exists(_node.FullPath))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"/select,\"{_node.FullPath}\"",
                    UseShellExecute = true
                });
            }
        }
        catch { }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
}

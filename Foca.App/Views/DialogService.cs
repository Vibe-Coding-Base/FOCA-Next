using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Platform.Storage;

namespace Foca.App.Views;

/// <summary>File pickers and message boxes, behind an interface so view models stay testable.</summary>
public interface IDialogService
{
    Task<string?> PickFolderAsync(string title);
    Task<IReadOnlyList<string>> PickFilesAsync(string title);
    Task<string?> SaveFileAsync(string title, string suggestedName, string extension);
    Task<bool> ConfirmAsync(string title, string message);
    Task ShowAsync(string title, string message);
}

public sealed class DialogService : IDialogService
{
    private readonly Window owner;

    public DialogService(Window owner)
    {
        this.owner = owner;
    }

    public async Task<string?> PickFolderAsync(string title)
    {
        IReadOnlyList<IStorageFolder> folders = await owner.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = title });
        return folders.Count == 0 ? null : folders[0].TryGetLocalPath();
    }

    public async Task<IReadOnlyList<string>> PickFilesAsync(string title)
    {
        IReadOnlyList<IStorageFile> files = await owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = title, AllowMultiple = true });
        return files.Select(f => f.TryGetLocalPath()).OfType<string>().ToList();
    }

    public async Task<string?> SaveFileAsync(string title, string suggestedName, string extension)
    {
        IStorageFile? file = await owner.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = title,
            SuggestedFileName = suggestedName,
            DefaultExtension = extension,
            FileTypeChoices = new[] { new FilePickerFileType(extension.ToUpperInvariant()) { Patterns = new[] { "*." + extension } } },
        });
        return file?.TryGetLocalPath();
    }

    public Task<bool> ConfirmAsync(string title, string message) => ShowDialogAsync(title, message, yesNo: true);

    public Task ShowAsync(string title, string message) => ShowDialogAsync(title, message, yesNo: false);

    private async Task<bool> ShowDialogAsync(string title, string message, bool yesNo)
    {
        var dialog = new Window
        {
            Title = title,
            Width = 460,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
        };
        bool result = false;
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8 };
        if (yesNo)
        {
            var yes = new Button { Content = "Yes", IsDefault = true };
            yes.Click += (_, _) => { result = true; dialog.Close(); };
            var no = new Button { Content = "No", IsCancel = true };
            no.Click += (_, _) => dialog.Close();
            buttons.Children.Add(yes);
            buttons.Children.Add(no);
        }
        else
        {
            var ok = new Button { Content = "OK", IsDefault = true, IsCancel = true };
            ok.Click += (_, _) => dialog.Close();
            buttons.Children.Add(ok);
        }
        dialog.Content = new StackPanel
        {
            Margin = new Avalonia.Thickness(20),
            Spacing = 16,
            Children = { new TextBlock { Text = message, TextWrapping = Avalonia.Media.TextWrapping.Wrap }, buttons },
        };
        await dialog.ShowDialog(owner);
        return result;
    }
}

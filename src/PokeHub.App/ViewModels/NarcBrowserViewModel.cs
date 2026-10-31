using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using PokeHub.Core.Gen4;
using PokeHub.Core.Graphics;
using PokeHub.Core.Localization;
using PokeHub.Core.Projects;

namespace PokeHub.App.ViewModels;

/// <summary>Uma linha da lista de arquivos do NARC. A miniatura só é desenhada quando a linha aparece.</summary>
public sealed class NarcMemberItem(NarcBrowser browser, NarcMember member)
{
    private BitmapSource? _thumbnail;
    private bool _rendered;
    public NarcMember Member { get; } = member;
    public string Label => $"{Member.Index:D4}  {Member.Kind}  {Member.Size:N0} B";
    public BitmapSource? Thumbnail
    {
        get
        {
            if (!_rendered) { _rendered = true; _thumbnail = browser.Render(Member.Index)?.ToBitmap(); }
            return _thumbnail;
        }
    }
}

/// <summary>Navegador de NARCs: procurar, ver miniaturas, extrair um arquivo e trocá-lo de volta.</summary>
public sealed partial class NarcBrowserViewModel : ObservableObject
{
    private readonly Project _project;
    private readonly Func<Project, string, Task> _snapshot;
    private readonly IReadOnlyList<NarcEntry> _all;
    private NarcBrowser? _browser;

    public NarcBrowserViewModel(Project project, Func<Project, string, Task> snapshot)
    {
        _project = project;
        _snapshot = snapshot;
        _all = NarcBrowser.Scan(Gen4Game.For(project).FilesRoot);
        ApplyFilter();
    }

    public ObservableCollection<NarcEntry> Narcs { get; } = [];
    public ObservableCollection<NarcMemberItem> Members { get; } = [];
    [ObservableProperty] private string _filter = "";
    [ObservableProperty] private bool _onlyGraphics = true;
    [ObservableProperty] private NarcEntry? _selectedNarc;
    [ObservableProperty, NotifyCanExecuteChangedFor(nameof(ExtractCommand), nameof(ReplaceCommand))] private NarcMemberItem? _selectedMember;
    [ObservableProperty] private BitmapSource? _preview;
    [ObservableProperty] private string _status = "";

    partial void OnFilterChanged(string value) => ApplyFilter();
    partial void OnOnlyGraphicsChanged(bool value) => FillMembers();

    private void ApplyFilter()
    {
        Narcs.Clear();
        foreach (var n in _all.Where(n => n.RelativePath.Contains(Filter.Trim(), StringComparison.OrdinalIgnoreCase))) Narcs.Add(n);
        Status = Loc.T("narc.count", Narcs.Count, _all.Count);
    }

    partial void OnSelectedNarcChanged(NarcEntry? value)
    {
        _browser = null;
        if (value is not null)
        {
            try { _browser = NarcBrowser.Open(value.FullPath); }
            catch (Exception ex) when (ex is InvalidDataException or IOException) { Status = ex.Message; }
        }
        FillMembers();
    }

    private void FillMembers()
    {
        Members.Clear();
        Preview = null;
        if (_browser is null) return;
        var shown = _browser.Members.Where(m => !OnlyGraphics || m.Kind.StartsWith("NCGR") || m.Kind.StartsWith("NSCR") || m.Kind.StartsWith("NCLR")).ToList();
        foreach (var m in shown) Members.Add(new NarcMemberItem(_browser, m));
        Status = Loc.T("narc.members", shown.Count, _browser.Members.Count);
        SelectedMember = Members.FirstOrDefault();
    }

    partial void OnSelectedMemberChanged(NarcMemberItem? value) => Preview = value?.Thumbnail;

    private bool HasMember => SelectedMember is not null;

    private string DefaultName(NarcMemberItem item)
    {
        var baseName = SelectedNarc!.RelativePath.Replace('/', '_');
        return $"{baseName}_{item.Member.Index:D4}.{item.Member.Kind.Split(' ')[0].ToLowerInvariant()}";
    }

    [RelayCommand(CanExecute = nameof(HasMember))]
    private void Extract()
    {
        var item = SelectedMember!;
        var dialog = new SaveFileDialog { FileName = DefaultName(item), Title = Loc.T("narc.extract") };
        if (dialog.ShowDialog() != true) return;
        File.WriteAllBytes(dialog.FileName, _browser!.Raw(item.Member.Index));
        Status = Loc.T("narc.extracted", dialog.FileName);
    }

    [RelayCommand(CanExecute = nameof(HasMember))]
    private async Task ReplaceAsync()
    {
        var item = SelectedMember!;
        var dialog = new OpenFileDialog { Title = Loc.T("narc.replace") };
        if (dialog.ShowDialog() != true) return;
        int index = item.Member.Index;
        var path = SelectedNarc!.RelativePath;
        try
        {
            await _snapshot(_project, Loc.T("narc.snapshotBefore", path, index));
            _browser!.Replace(index, File.ReadAllBytes(dialog.FileName));
            await _snapshot(_project, Loc.T("narc.snapshotAfter", path, index));
            var narc = SelectedNarc;
            SelectedNarc = null;
            SelectedNarc = narc;
            SelectedMember = Members.FirstOrDefault(m => m.Member.Index == index);
            Status = Loc.T("narc.replaced", index);
        }
        catch (InvalidDataException ex) { Status = Loc.T("narc.wrongKind", ex.Message); }
        catch (IOException ex) { Status = ex.Message; }
    }
}

using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PokeHub.Core.Gen4.Scripts;
using PokeHub.Core.Localization;

namespace PokeHub.App.ViewModels;

/// <summary>Uma linha da listagem. Offset identifica o comando nas operações.</summary>
public sealed record ScriptLine(int Offset, string Entry, string Position, string Command, string Args, string Note);

public sealed record ScriptCommandOption(int Opcode, string Name, IReadOnlyList<string> Types)
{
    public override string ToString() => Name;
}

public sealed partial class ScriptArgInput(string type) : ObservableObject
{
    public string Type { get; } = type;
    [ObservableProperty] private string _text = "0";
}

public sealed partial class ScriptEditorViewModel : ObservableObject
{
    private readonly ScriptArchive _archive;
    private readonly Action<string> _log;
    private readonly Func<string, Task> _snapshot;
    private readonly List<ScriptCommandOption> _allCommands;

    public ObservableCollection<string> Files { get; } = [];
    public ObservableCollection<ScriptLine> Lines { get; } = [];
    public ObservableCollection<ScriptCommandOption> Commands { get; } = [];
    public ObservableCollection<ScriptArgInput> Inputs { get; } = [];

    [ObservableProperty] private string _fileFilter = "";
    [ObservableProperty] private string? _selectedFile;
    [ObservableProperty] private ScriptLine? _selectedLine;
    [ObservableProperty] private string _commandFilter = "";
    [ObservableProperty] private ScriptCommandOption? _selectedCommand;
    [ObservableProperty] private bool _isDirty;
    [ObservableProperty] private string _status = "";

    public ScriptEditorViewModel(ScriptArchive archive, Action<string> log, Func<string, Task> snapshot)
    {
        _archive = archive; _log = log; _snapshot = snapshot;
        _allCommands = archive.Table.Commands.Select((c, i) => new ScriptCommandOption(i, c.Name, c.Args.Select(ScriptCommandTable.ArgType).ToList())).OrderBy(c => c.Name).ToList();
        ApplyCommandFilter();
        ApplyFileFilter();
        SelectedFile = Files.FirstOrDefault();
    }

    private int FileIndex => SelectedFile is { } name && int.TryParse(name.AsSpan(0, 4), out var i) ? i : -1;

    partial void OnFileFilterChanged(string value) => ApplyFileFilter();
    partial void OnCommandFilterChanged(string value) => ApplyCommandFilter();
    partial void OnSelectedFileChanged(string? value) => LoadLines();

    private void ApplyFileFilter()
    {
        var keep = SelectedFile;
        Files.Clear();
        foreach (var n in _archive.Names.Where(n => string.IsNullOrWhiteSpace(FileFilter) || n.Contains(FileFilter, StringComparison.OrdinalIgnoreCase))) Files.Add(n);
        if (keep is not null && Files.Contains(keep)) SelectedFile = keep;
    }

    private void ApplyCommandFilter()
    {
        var keep = SelectedCommand;
        Commands.Clear();
        foreach (var c in _allCommands.Where(c => string.IsNullOrWhiteSpace(CommandFilter) || c.Name.Contains(CommandFilter, StringComparison.OrdinalIgnoreCase))) Commands.Add(c);
        SelectedCommand = keep is not null && Commands.Contains(keep) ? keep : Commands.FirstOrDefault();
    }

    partial void OnSelectedCommandChanged(ScriptCommandOption? value)
    {
        Inputs.Clear();
        if (value is null) return;
        foreach (var t in value.Types) Inputs.Add(new ScriptArgInput(t));
    }

    private void LoadLines(int? keepOffset = null)
    {
        Lines.Clear();
        if (FileIndex < 0) return;
        var editor = _archive.Open(FileIndex);
        var entries = new Dictionary<int, string>();
        for (int i = 0; i < editor.File.EntryOffsets.Count; i++) entries.TryAdd(editor.File.EntryOffsets[i], Loc.T("scr.entry", i + 1));
        foreach (var i in editor.Instructions())
        {
            var message = i.Arg("message") is { } m ? _archive.Message(FileIndex, (int)m.Value) : null;
            Lines.Add(new ScriptLine(i.Offset, entries.GetValueOrDefault(i.Offset, ""), $"{i.Offset:X4}", i.Name,
                string.Join(", ", i.Args.Select(a => a.Type is "script" or "movement" or "addr" ? $"{a.Type}: {a.Value:X4}" : $"{a.Type}: {a.Value}")), message ?? ""));
        }
        SelectedLine = keepOffset is { } at ? Lines.FirstOrDefault(l => l.Offset == at) : Lines.FirstOrDefault();
    }

    private static long Parse(ScriptArgInput input)
    {
        var text = input.Text.Trim();
        // saltos aparecem em hexadecimal na listagem, então é assim que são digitados
        bool jump = input.Type is "script" or "movement" or "addr";
        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) return long.Parse(text.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        return jump ? long.Parse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture) : long.Parse(text, CultureInfo.InvariantCulture);
    }

    [RelayCommand]
    private void Insert()
    {
        if (SelectedLine is not { } line || SelectedCommand is not { } command || FileIndex < 0) return;
        try
        {
            _archive.Open(FileIndex).Insert(line.Offset, command.Opcode, Inputs.Select(Parse).ToList());
            IsDirty = true;
            Status = Loc.T("common.unsaved");
            LoadLines(line.Offset);
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException or OverflowException) { Status = ex.Message; }
    }

    [RelayCommand]
    private void Remove()
    {
        if (SelectedLine is not { } line || FileIndex < 0) return;
        try
        {
            _archive.Open(FileIndex).Remove(line.Offset);
            IsDirty = true;
            Status = Loc.T("common.unsaved");
            LoadLines(line.Offset);
        }
        catch (ArgumentException ex) { Status = ex.Message; }
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        try
        {
            _archive.Save();
            IsDirty = false;
            Status = Loc.T("common.savedAt", DateTime.Now.ToString("HH:mm:ss"));
            _log(Loc.T("scr.saved"));
            await _snapshot(Loc.T("scr.snapshot"));
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException) { Status = Loc.T("common.saveError", ex.Message); }
    }
}

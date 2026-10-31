using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PokeHub.Core.CodeInjection;
using PokeHub.Core.Localization;
using PokeHub.Core.Projects;

namespace PokeHub.App.ViewModels;

public sealed partial class HookRow : ObservableObject
{
    [ObservableProperty] private string _target = "arm9";
    [ObservableProperty] private string _address = "";
    [ObservableProperty] private string _type = "call";
    [ObservableProperty] private string _symbol = "";
    [ObservableProperty] private string _bytes = "";
    [ObservableProperty] private string _mode = "thumb";
    [ObservableProperty] private string _expect = "";

    public PatchHook ToHook() => new()
    {
        Target = Target.Trim(),
        Address = Address.Trim(),
        Type = Type,
        Symbol = Type == "bytes" ? null : Symbol.Trim(),
        Bytes = Type == "bytes" ? Bytes.Trim() : null,
        Mode = Mode,
        Expect = string.IsNullOrWhiteSpace(Expect) ? null : Expect.Trim(),
    };
}

public sealed partial class SymbolRow : ObservableObject
{
    [ObservableProperty] private string _name = "";
    [ObservableProperty] private string _value = "";
}

/// <summary>Formulário do patch.json: funções do jogo e ganchos, com a leitura dos bytes originais para a conferência.</summary>
public sealed partial class PatchHooksViewModel : ObservableObject
{
    private readonly Project _project;
    private readonly CodePatch _patch;

    public PatchHooksViewModel(Project project, CodePatch patch)
    {
        _project = project;
        _patch = patch;
        Title = Loc.T("hooks.title", patch.Name);
        Description = patch.Manifest.Description;
        foreach (var (name, value) in patch.Manifest.Symbols) Symbols.Add(new SymbolRow { Name = name, Value = value });
        foreach (var h in patch.Manifest.Hooks)
            Hooks.Add(new HookRow { Target = h.Target, Address = h.Address, Type = h.Type, Symbol = h.Symbol ?? "", Bytes = h.Bytes ?? "", Mode = h.Mode, Expect = h.Expect ?? "" });
    }

    public string Title { get; }
    public IReadOnlyList<string> Types { get; } = ["call", "jump", "pointer", "bytes"];
    public IReadOnlyList<string> Modes { get; } = ["thumb", "arm"];
    public ObservableCollection<HookRow> Hooks { get; } = [];
    public ObservableCollection<SymbolRow> Symbols { get; } = [];
    [ObservableProperty] private string _description = "";
    [ObservableProperty, NotifyCanExecuteChangedFor(nameof(ReadBytesCommand), nameof(RemoveHookCommand))] private HookRow? _selectedHook;
    [ObservableProperty, NotifyCanExecuteChangedFor(nameof(RemoveSymbolCommand))] private SymbolRow? _selectedSymbol;
    [ObservableProperty] private string _status = "";
    public bool Saved { get; private set; }

    private bool HasHook => SelectedHook is not null;
    private bool HasSymbol => SelectedSymbol is not null;

    [RelayCommand]
    private void AddHook()
    {
        var row = new HookRow();
        Hooks.Add(row);
        SelectedHook = row;
    }

    [RelayCommand(CanExecute = nameof(HasHook))]
    private void RemoveHook() => Hooks.Remove(SelectedHook!);

    [RelayCommand]
    private void AddSymbol()
    {
        var row = new SymbolRow();
        Symbols.Add(row);
        SelectedSymbol = row;
    }

    [RelayCommand(CanExecute = nameof(HasSymbol))]
    private void RemoveSymbol() => Symbols.Remove(SelectedSymbol!);

    [RelayCommand(CanExecute = nameof(HasHook))]
    private void ReadBytes()
    {
        var row = SelectedHook!;
        try
        {
            if (!CodePatch.IsNumber(row.Address.Trim())) throw new InvalidDataException($"address: \"{row.Address}\"");
            var bytes = CodePatchService.OriginalBytes(_project, row.ToHook());
            row.Expect = Convert.ToHexString(bytes);
            Status = Loc.T("hooks.read", row.Address.Trim(), row.Expect);
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or InvalidOperationException or FormatException or ArgumentException) { Status = ex.Message; }
    }

    /// <summary>Valida antes de gravar; com erro, o patch.json fica como estava.</summary>
    [RelayCommand]
    private void Save()
    {
        var manifest = new PatchManifest
        {
            Description = Description,
            Enabled = _patch.Manifest.Enabled,
            GameCodes = _patch.Manifest.GameCodes,
            Symbols = Symbols.Where(s => s.Name.Trim().Length > 0).ToDictionary(s => s.Name.Trim(), s => s.Value.Trim()),
            Hooks = Hooks.Select(h => h.ToHook()).ToList(),
        };
        try
        {
            var candidate = new CodePatch { Name = _patch.Name, Directory = _patch.Directory, Manifest = manifest };
            candidate.Validate();
            candidate.Save();
            _patch.Manifest.Description = manifest.Description;
            _patch.Manifest.Symbols = manifest.Symbols;
            _patch.Manifest.Hooks = manifest.Hooks;
            Saved = true;
            Status = Loc.T("hooks.saved");
        }
        catch (Exception ex) when (ex is InvalidDataException or ArgumentException) { Status = ex.Message; }
    }
}

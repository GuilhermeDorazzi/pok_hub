using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PokeHub.Core.Gen4;
using PokeHub.Core.Localization;

namespace PokeHub.App.ViewModels;

public sealed record BankItem(int Id, string Title, int Count)
{
    public string Display => $"{Title}  ({Count})";
}

public sealed partial class MessageItem(int index, string text, bool compressed) : ObservableObject
{
    public int Index { get; } = index;
    public bool Compressed { get; } = compressed;
    [ObservableProperty] private string _text = text;
    public string Preview => $"{Index:D4}  {Text.Replace("\\n", " ⏎ ")}";
    partial void OnTextChanged(string value) => OnPropertyChanged(nameof(Preview));
}

public sealed partial class TextEditorViewModel : ObservableObject
{
    private readonly TextRepository _repo;
    private readonly Action<string> _log;
    private readonly Func<string, Task> _snapshot;
    private readonly Gen4Font? _font;

    public ObservableCollection<BankItem> Banks { get; } = [];
    public ObservableCollection<MessageItem> Messages { get; } = [];
    public ObservableCollection<TextSearchHit> SearchHits { get; } = [];

    [ObservableProperty] private BankItem? _selectedBank;
    [ObservableProperty] private MessageItem? _selectedMessage;
    [ObservableProperty] private TextSearchHit? _selectedHit;
    [ObservableProperty] private string _editText = "";
    [ObservableProperty] private string _searchQuery = "";
    [ObservableProperty] private bool _isDirty;
    [ObservableProperty] private string _status = "";
    [ObservableProperty] private BitmapSource? _previewImage;
    [ObservableProperty] private string _previewNote = "";

    public string GameLabel => Loc.T("txt.gameLabel", _repo.Game.Game, _repo.BankCount);
    public string Hint => Loc.T("txt.hint");

    public TextEditorViewModel(TextRepository repo, Action<string> log, Func<string, Task> snapshot)
    {
        _repo = repo; _log = log; _snapshot = snapshot;
        _font = Gen4Font.Load(repo.Game);
        for (int i = 0; i < repo.BankCount; i++)
            Banks.Add(new BankItem(i, repo.LabelOf(i) is { } l ? $"{i:D4}  {l}" : $"{i:D4}", repo.MessageCount(i)));
        SelectedBank = Banks.FirstOrDefault(b => b.Id == repo.Game.SpeciesBank) ?? Banks.FirstOrDefault();
    }

    partial void OnSelectedBankChanged(BankItem? value)
    {
        Messages.Clear();
        if (value is null) return;
        var bank = _repo.Bank(value.Id);
        for (int i = 0; i < bank.Texts.Count; i++) Messages.Add(new MessageItem(i, bank.Texts[i], bank.Archive.Messages[i].IsCompressed));
        SelectedMessage = Messages.FirstOrDefault();
    }

    partial void OnSelectedMessageChanged(MessageItem? value)
    {
        EditText = value?.Text ?? "";
        OnPropertyChanged(nameof(CompressedNote));
    }

    partial void OnEditTextChanged(string value)
    {
        if (_font is null) { PreviewNote = Loc.T("txt.preview.noFont"); return; }
        try
        {
            var preview = TextPreview.Render(Gen4TextArchive.Encode(value), _font);
            PreviewImage = preview.Image.ToBitmap();
            var over = preview.Lines.Where(l => l.TooLong).ToList();
            PreviewNote = over.Count == 0
                ? Loc.T("txt.preview.fits", preview.Lines.Max(l => l.Width), TextPreview.BoxWidth)
                : Loc.T("txt.preview.over", string.Join(", ", over.Select(l => $"{l.Number} ({l.Width} px)")), TextPreview.BoxWidth);
        }
        catch (Exception ex) when (ex is InvalidDataException or FormatException or OverflowException or ArgumentException)
        {
            PreviewNote = Loc.T("txt.invalid", ex.Message); // ainda digitando um comando, por exemplo: a imagem anterior fica
        }
    }

    public string CompressedNote => SelectedMessage is { Compressed: true } ? Loc.T("txt.compressed") : "";

    partial void OnSelectedHitChanged(TextSearchHit? value)
    {
        if (value is null) return;
        SelectedBank = Banks[value.Bank];
        SelectedMessage = Messages.FirstOrDefault(m => m.Index == value.Index);
    }

    /// <summary>Aplica o texto editado à mensagem selecionada; codifica na hora para validar os caracteres.</summary>
    [RelayCommand]
    private void Apply()
    {
        if (SelectedBank is null || SelectedMessage is null) return;
        try
        {
            if (_repo.SetText(SelectedBank.Id, SelectedMessage.Index, EditText))
            {
                SelectedMessage.Text = EditText;
                IsDirty = true;
                Status = Loc.T("common.unsaved");
            }
        }
        catch (Exception ex)
        {
            Status = Loc.T("txt.invalid", ex.Message);
        }
    }

    [RelayCommand]
    private void Search()
    {
        SearchHits.Clear();
        foreach (var hit in _repo.Search(SearchQuery)) SearchHits.Add(hit);
        Status = Loc.T("txt.searchResult", SearchHits.Count);
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        Apply();
        try
        {
            _repo.Save();
            IsDirty = false;
            Status = Loc.T("common.savedAt", DateTime.Now.ToString("HH:mm:ss"));
            _log(Loc.T("txt.savedLog"));
            await _snapshot(Loc.T("txt.snapshot"));
        }
        catch (Exception ex)
        {
            Status = Loc.T("common.saveError", ex.Message);
            _log(Loc.T("txt.saveErrorLog", ex.Message));
        }
    }
}

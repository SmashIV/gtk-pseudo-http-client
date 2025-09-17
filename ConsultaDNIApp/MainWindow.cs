using Gtk;
using System;
using System.Collections.Generic;
using System.Threading;
using Consulta_DNI.REST;

public sealed class MainWindow : Window
{
    private readonly Entry _urlEntry;
    private readonly Entry _tokenEntry;
    private readonly RadioButton _getRadio;
    private readonly RadioButton _postRadio;
    private readonly RadioButton _putRadio;
    private readonly RadioButton _patchRadio;
    private readonly RadioButton _deleteRadio;
    
    private readonly Button _fetchButton;
    private readonly TextView _output;
    private readonly Label _status;

    private readonly Notebook _bodyNotebook;
    private readonly TextView _rawJsonView;
    private readonly VBox _builderBox;
    private readonly Button _addFieldButton;
    private readonly List<(Entry key, Entry val, HBox row)> _fieldRows = new();
    private readonly ApiClient _api = new();
    private CancellationTokenSource? _cts;

    private readonly int _methodFlag;
    
    public MainWindow() : base("Consulta JSON - GTK")
    {
        DefaultWidth = 1000;
        DefaultHeight = 700;

        var outer = new VBox(false, 6) { BorderWidth = 8 };

        _urlEntry = new Entry { PlaceholderText = "https://apiperu.dev/api/dni" };
        _tokenEntry = new Entry { PlaceholderText = "Bearer Token (opcional)" };

        _getRadio = new RadioButton("GET");
        _postRadio = new RadioButton(_getRadio, "POST") { Active = true};
        _putRadio = new RadioButton(_postRadio, "PUT");
        _patchRadio = new RadioButton(_putRadio, "PATCH");
        _deleteRadio = new RadioButton(_patchRadio, "DELETE");
        
        var methodBox = new HBox(false, 6);
        methodBox.PackStart(_getRadio, false, false, 0);
        methodBox.PackStart(_postRadio, false, false, 0);
        methodBox.PackStart(_putRadio, false, false, 0);
        methodBox.PackStart(_patchRadio, false, false, 0);
        methodBox.PackStart(_deleteRadio, false, false, 0);
        
        _bodyNotebook = new Notebook();

        _rawJsonView = new TextView { Monospace = true };
        var rawScroll = new ScrolledWindow();
        rawScroll.Add(_rawJsonView);
        _bodyNotebook.AppendPage(rawScroll, new Label("JSON Body"));

        _builderBox = new VBox(false, 4);
        var builderScroll = new ScrolledWindow();
        builderScroll.AddWithViewport(_builderBox);

        _addFieldButton = new Button("Agregar campo");
        _addFieldButton.Clicked += (_, __) => AddFieldRow();
        var builderHeader = new HBox(false, 6);
        builderHeader.PackStart(_addFieldButton, false, false, 0);

        var builderContainer = new VBox(false, 6);
        builderContainer.PackStart(builderHeader, false, false, 0);
        builderContainer.PackStart(builderScroll, true, true, 0);
        _bodyNotebook.AppendPage(builderContainer, new Label("Constructor"));

        AddFieldRow("", "");

        var bodyFrame = new Frame("Cuerpo");
        bodyFrame.BorderWidth = 8;
        bodyFrame.Add(_bodyNotebook);

        _fetchButton = new Button("Enviar");
        _status = new Label();
        _output = new TextView { Editable = false, Monospace = true, WrapMode = WrapMode.None };

        var responseFrame = new Frame("Respuesta");
        responseFrame.BorderWidth = 4;
        var responseScroll = new ScrolledWindow();
        responseScroll.Add(_output);
        responseFrame.Add(responseScroll);

        AddRow(outer, "URL:", _urlEntry);
        AddRow(outer, "Token:", _tokenEntry);
        outer.PackStart(methodBox, false, false, 0);
        outer.PackStart(bodyFrame, true, true, 0);
        outer.PackStart(_fetchButton, false, false, 0);
        outer.PackStart(_status, false, false, 0);
        outer.PackStart(responseFrame, true, true, 0);

        _getRadio.Toggled += (_, __) => UpdateBodySensitivity();
        _deleteRadio.Toggled += (_, __) => UpdateBodySensitivity();
        UpdateBodySensitivity();

        _fetchButton.Clicked += OnFetchClicked;
        DeleteEvent += (_, __) => Application.Quit();

        Add(outer);
        ShowAll();
    }

    private void UpdateBodySensitivity()
    {
        bool isGet = _getRadio.Active || _deleteRadio.Active;
        _bodyNotebook.Sensitive = !isGet;
    }

    private void AddFieldRow(string? key = "", string? value = "")
    {
        var row = new HBox(false, 4);
        var keyEntry = new Entry { Text = key ?? "", PlaceholderText = "clave" };
        var valEntry = new Entry { Text = value ?? "", PlaceholderText = "valor" };
        var removeBtn = new Button("X");
        removeBtn.Clicked += (_, __) =>
        {
            _builderBox.Remove(row);
            _fieldRows.RemoveAll(t => t.row == row);
            row.Dispose();
            _builderBox.ShowAll();
        };
        row.PackStart(keyEntry, true, true, 0);
        row.PackStart(valEntry, true, true, 0);
        row.PackStart(removeBtn, false, false, 0);
        _builderBox.PackStart(row, false, false, 0);
        _fieldRows.Add((keyEntry, valEntry, row));
        _builderBox.ShowAll();
    }

    private async void OnFetchClicked(object? sender, EventArgs e)
    {
        if (_cts is not null)
        {
            _cts.Cancel();
            return;
        }

        var url = _urlEntry.Text?.Trim();
        if (string.IsNullOrWhiteSpace(url))
        {
            SetStatus("URL requerida.");
            return;
        }

        bool isGet = _getRadio.Active;
        string? token = _tokenEntry.Text?.Trim();
        string? bodyJson = null;

        if (!isGet)
        {
            int page = _bodyNotebook.CurrentPage;
            if (page == 0)
            {
                bodyJson = _rawJsonView.Buffer.Text?.Trim();
                if (string.IsNullOrWhiteSpace(bodyJson))
                {
                    SetStatus("JSON vacío.");
                    return;
                }
                try
                {
                    using var _ = System.Text.Json.JsonDocument.Parse(bodyJson);
                }
                catch (Exception ex)
                {
                    SetStatus("JSON inválido: " + ex.Message);
                    return;
                }
            }
            else
            {
                var dict = new Dictionary<string, string>();
                foreach (var (k, v, _) in _fieldRows)
                {
                    var key = k.Text?.Trim();
                    var val = v.Text?.Trim();
                    if (string.IsNullOrEmpty(key)) continue;
                    dict[key] = val ?? "";
                }
                bodyJson = dict.Count == 0 ? "{}" : Newtonsoft.Json.JsonConvert.SerializeObject(dict);
            }
        }

        _cts = new CancellationTokenSource();
        _fetchButton.Label = "Cancelar...";
        SetStatus("Enviando...");
        SetOutput("");

        try
        {
            //TODO: Mejorar la asignacion del resultado
            string? result = null;
            if (_postRadio.Active)
            {
                result = await _api.PostRawJsonGetStringAsync(url, bodyJson, token, _cts.Token);
            } else if (_putRadio.Active)
            {
                result = await _api.PutRawJsonStringAsync(url, bodyJson, token, _cts.Token);
            }
            else if (_patchRadio.Active)
            {
                result = await _api.PatchRawJsonStringAsync(url, bodyJson, token, _cts.Token);
            }
            else if (_deleteRadio.Active)
            {
                result = await _api.DeleteStringAsync(url, token, _cts.Token);
            }
            else
            {
                result = await _api.GetStringAsync(url, token, _cts.Token);
            }
                        

            if (result is null)
            {
                SetStatus("Respuesta vacía.");
                return;
            }

            SetOutput(PrettyJson(result));
            SetStatus("OK");
        }
        catch (OperationCanceledException)
        {
            SetStatus("Cancelado.");
        }
        catch (Exception ex)
        {
            SetStatus("Error: " + ex.Message);
        }
        finally
        {
            _cts.Dispose();
            _cts = null;
            _fetchButton.Label = "Enviar";
        }
    }

    private static string PrettyJson(string raw)
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(raw);
            return System.Text.Json.JsonSerializer.Serialize(doc.RootElement,
                new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
        }
        catch
        {
            return raw;
        }
    }

    private void SetStatus(string text)
        => Application.Invoke((_, __) => _status.Text = text);

    private void SetOutput(string text)
        => Application.Invoke((_, __) => _output.Buffer.Text = text);

    private static void AddRow(VBox outer, string label, Widget field)
    {
        var box = new HBox(false, 6);
        box.PackStart(new Label(label) { Xalign = 0 }, false, false, 0);
        box.PackStart(field, true, true, 0);
        outer.PackStart(box, false, false, 0);
    }
}

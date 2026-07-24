using FrostyConvert.Core.Convert;
using FrostyConvert.Core.FifaMod;
using FrostyConvert.Core.Legacy;
using FrostyConvert.Core.Project;

namespace FrostyConvert.FifaGui;

internal sealed class MainForm : Form
{
    private readonly TextBox _inputBox;
    private readonly TextBox _outputBox;
    private readonly CheckBox _promoteTextures;
    private readonly Button _convertButton;
    private readonly TextBox _log;
    private readonly Label _status;

    public MainForm()
    {
        Text = "Frosty Converter";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(560, 420);
        Font = new Font("Segoe UI", 9.5f);
        BackColor = Color.FromArgb(250, 250, 250);
        Padding = new Padding(16);

        var title = new Label
        {
            Text = "Convert .fifamod → .fifaproject",
            Font = new Font("Segoe UI", 14f, FontStyle.Bold),
            AutoSize = true,
            Location = new Point(16, 16),
            ForeColor = Color.FromArgb(30, 30, 30),
        };

        var subtitle = new Label
        {
            Text = "Recover an editable FIFA Editor Tool project from a compiled mod.",
            AutoSize = true,
            Location = new Point(16, 48),
            ForeColor = Color.FromArgb(90, 90, 90),
        };

        var inputLabel = MakeFieldLabel("Input (.fifamod)", 80);
        _inputBox = MakePathBox(104);
        var browseInput = MakeBrowseButton(104, BrowseInput);

        var outputLabel = MakeFieldLabel("Output (.fifaproject)", 144);
        _outputBox = MakePathBox(168);
        var browseOutput = MakeBrowseButton(168, BrowseOutput);

        _promoteTextures = new CheckBox
        {
            // Optional: only for crest/UI mods with standalone legacy .dds.
            // Default off — most mods (faces, scoreboards, gameplay) convert without it,
            // and promote used to hard-fail when no Texture RES template / DDS existed.
            Text = "Optional: promote legacy .dds → Data Explorer (crest/UI only)",
            AutoSize = true,
            Location = new Point(16, 208),
            ForeColor = Color.FromArgb(40, 40, 40),
            Checked = false,
        };

        _convertButton = new Button
        {
            Text = "Convert",
            Location = new Point(16, 244),
            Size = new Size(120, 34),
            FlatStyle = FlatStyle.System,
            Enabled = false,
        };
        _convertButton.Click += async (_, _) => await ConvertAsync();

        _status = new Label
        {
            Text = "Select a .fifamod file to begin.",
            AutoSize = false,
            Location = new Point(148, 250),
            Size = new Size(396, 24),
            ForeColor = Color.FromArgb(90, 90, 90),
            TextAlign = ContentAlignment.MiddleLeft,
        };

        var logLabel = MakeFieldLabel("Log", 288);
        _log = new TextBox
        {
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            Location = new Point(16, 312),
            Size = new Size(528, 92),
            BackColor = Color.White,
            BorderStyle = BorderStyle.FixedSingle,
            Font = new Font("Consolas", 8.5f),
        };

        _inputBox.TextChanged += (_, _) => UpdateConvertEnabled();
        _outputBox.TextChanged += (_, _) => UpdateConvertEnabled();

        Controls.AddRange(
        [
            title, subtitle,
            inputLabel, _inputBox, browseInput,
            outputLabel, _outputBox, browseOutput,
            _promoteTextures, _convertButton, _status,
            logLabel, _log,
        ]);

        AllowDrop = true;
        DragEnter += OnDragEnter;
        DragDrop += OnDragDrop;
    }

    private static Label MakeFieldLabel(string text, int y) => new()
    {
        Text = text,
        AutoSize = true,
        Location = new Point(16, y),
        ForeColor = Color.FromArgb(60, 60, 60),
    };

    private static TextBox MakePathBox(int y) => new()
    {
        Location = new Point(16, y),
        Size = new Size(440, 28),
        BorderStyle = BorderStyle.FixedSingle,
        BackColor = Color.White,
    };

    private Button MakeBrowseButton(int y, EventHandler onClick)
    {
        var btn = new Button
        {
            Text = "Browse…",
            Location = new Point(464, y - 1),
            Size = new Size(80, 28),
            FlatStyle = FlatStyle.System,
        };
        btn.Click += onClick;
        return btn;
    }

    private void BrowseInput(object? sender, EventArgs e)
    {
        using var dlg = new OpenFileDialog
        {
            Title = "Select FIFA mod",
            Filter = "FIFA Mod (*.fifamod)|*.fifamod|All files (*.*)|*.*",
            CheckFileExists = true,
        };
        if (dlg.ShowDialog(this) != DialogResult.OK)
            return;

        SetInputPath(dlg.FileName);
    }

    private void BrowseOutput(object? sender, EventArgs e)
    {
        using var dlg = new SaveFileDialog
        {
            Title = "Save FIFA project",
            Filter = "FIFA Project (*.fifaproject)|*.fifaproject|All files (*.*)|*.*",
            DefaultExt = "fifaproject",
            AddExtension = true,
            FileName = string.IsNullOrWhiteSpace(_outputBox.Text)
                ? "recovered.fifaproject"
                : Path.GetFileName(_outputBox.Text),
            InitialDirectory = string.IsNullOrWhiteSpace(_outputBox.Text)
                ? null
                : Path.GetDirectoryName(_outputBox.Text),
        };
        if (dlg.ShowDialog(this) != DialogResult.OK)
            return;

        _outputBox.Text = dlg.FileName;
    }

    private void OnDragEnter(object? sender, DragEventArgs e)
    {
        if (e.Data?.GetDataPresent(DataFormats.FileDrop) == true)
            e.Effect = DragDropEffects.Copy;
    }

    private void OnDragDrop(object? sender, DragEventArgs e)
    {
        if (e.Data?.GetData(DataFormats.FileDrop) is not string[] files || files.Length == 0)
            return;

        string path = files[0];
        if (path.EndsWith(".fifamod", StringComparison.OrdinalIgnoreCase) || File.Exists(path))
            SetInputPath(path);
    }

    private void SetInputPath(string path)
    {
        _inputBox.Text = path;
        if (string.IsNullOrWhiteSpace(_outputBox.Text) ||
            _outputBox.Text.EndsWith(".fifaproject", StringComparison.OrdinalIgnoreCase))
        {
            string dir = Path.GetDirectoryName(path) ?? "";
            string name = Path.GetFileNameWithoutExtension(path) + ".fifaproject";
            _outputBox.Text = Path.Combine(dir, name);
        }
    }

    private void UpdateConvertEnabled()
    {
        _convertButton.Enabled =
            !string.IsNullOrWhiteSpace(_inputBox.Text) &&
            !string.IsNullOrWhiteSpace(_outputBox.Text);
    }

    private async Task ConvertAsync()
    {
        string input = _inputBox.Text.Trim();
        string output = _outputBox.Text.Trim();
        bool promote = _promoteTextures.Checked;

        if (!File.Exists(input))
        {
            SetStatus("Input file not found.", isError: true);
            return;
        }

        if (!input.EndsWith(".fifamod", StringComparison.OrdinalIgnoreCase) &&
            !FifamodReader.IsFifamod(input))
        {
            var confirm = MessageBox.Show(
                this,
                "This file does not look like a .fifamod. Continue anyway?",
                "Confirm",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);
            if (confirm != DialogResult.Yes)
                return;
        }

        SetBusy(true);
        _log.Clear();
        SetStatus("Converting…");
        AppendLog($"Input:  {input}");
        AppendLog($"Output: {output}");
        if (promote)
            AppendLog("Option: promote legacy textures");
        AppendLog("");

        try
        {
            var result = await Task.Run(() => ConvertFifamod(input, output, promote));
            AppendLog(result.Log);
            if (result.Success)
            {
                SetStatus("Done — open project in FET, load game, Save, then re-export.", isError: false);
                MessageBox.Show(
                    this,
                    $"Wrote:\n{output}\n\n" +
                    "Required next steps:\n" +
                    "1. Open FIFA Editor Tool and load the matching game\n" +
                    "2. File → Open Project → this .fifaproject\n" +
                    "3. File → Save (rebuilds collectors / live types)\n" +
                    "4. Export a NEW .fifamod and test in Mod Manager\n\n" +
                    (result.ReadinessText ?? ""),
                    "Conversion complete",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
            else
            {
                SetStatus(result.ErrorMessage ?? "Conversion failed.", isError: true);
                MessageBox.Show(
                    this,
                    result.ErrorMessage ?? "Conversion failed. See log for details.",
                    "Conversion failed",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }
        catch (Exception ex)
        {
            AppendLog($"error: {ex.Message}");
            SetStatus(ex.Message, isError: true);
            MessageBox.Show(this, ex.Message, "Conversion failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private static ConvertResult ConvertFifamod(string inputPath, string outputPath, bool promoteTextures)
    {
        var log = new System.Text.StringBuilder();

        if (!FifamodReader.IsFifamod(inputPath) &&
            !inputPath.EndsWith(".fifamod", StringComparison.OrdinalIgnoreCase))
        {
            return ConvertResult.Fail("Not a valid .fifamod file.", log.ToString());
        }

        var fifa = FifamodReader.Read(inputPath, loadResourceData: true, decompress: true);
        log.AppendLine($"game={fifa.GameName}  title={fifa.Details.Title}");
        log.AppendLine($"resources={fifa.Resources.Count}");

        IReadOnlyList<FifamodResource>? extra = null;
        if (promoteTextures)
        {
            var promote = TextureAssetPromoter.Promote(fifa, new TexturePromoteOptions
            {
                NamePrefix = "content/ui/legacy",
                PathFilter = "",
                MaxCount = 0,
            });
            // Never abort conversion when promote finds nothing — the base .fifamod
            // project is still valid (faces, scoreboards, EBX-only mods, etc.).
            if (promote.PromotedCount > 0)
                extra = promote.Resources;
            log.AppendLine(
                $"promote: {promote.PromotedCount} TextureAssets " +
                $"(ddsCandidates={promote.CandidateCount}, errors={promote.SkippedErrors})");
            foreach (var e in promote.Errors.Take(8))
                log.AppendLine($"  promote: {e}");
            foreach (var n in promote.NonTextureLegacyNotes.Take(4))
                log.AppendLine($"  promote note: {n}");
            if (promote.PromotedCount == 0)
            {
                log.AppendLine(
                    "promote: nothing to promote — writing normal project (leave this option off unless the mod has crest/UI .dds).");
            }
        }

        var writable = FifaprojectWriter.CountWritable(fifa, extra);
        FifaprojectWriter.Write(outputPath, fifa, extra);

        int err = fifa.Resources.Count(r => r.DecompressError is not null);
        long outLen = new FileInfo(outputPath).Length;
        log.AppendLine($"wrote {outLen:N0} bytes");
        log.AppendLine($"written: ebx={writable.Ebx}  res={writable.Res}  chunks={writable.Chunks}  decompress_errors={err}");

        if (writable.Ebx + writable.Res + writable.Chunks == 0)
            return ConvertResult.Fail("No assets were written (empty conversion).", log.ToString());

        try
        {
            var check = FifaprojectReader.ReadSummary(outputPath);
            log.AppendLine(
                $"verify: chunks={check.ChunkCount} legacy={check.LegacyChunkCount} " +
                $"res={check.ResCount} ebx={check.EbxCount}");
            foreach (var w in check.Warnings)
                log.AppendLine($"  verify warning: {w}");
        }
        catch (Exception ex)
        {
            return ConvertResult.Fail($"Project verify failed (may not load in FET): {ex.Message}", log.ToString());
        }

        var readiness = ConversionReadiness.ForFifamod(fifa, writable.Ebx, writable.Res, writable.Chunks, err);
        log.AppendLine();
        log.AppendLine(readiness.ToText());

        if (!readiness.Success)
            return ConvertResult.Fail(readiness.Blocking.FirstOrDefault() ?? "Readiness check failed.", log.ToString(), readiness.ToText());

        return ConvertResult.Ok(log.ToString(), readiness.ToText());
    }

    private void SetBusy(bool busy)
    {
        _convertButton.Enabled = !busy && !string.IsNullOrWhiteSpace(_inputBox.Text) && !string.IsNullOrWhiteSpace(_outputBox.Text);
        _inputBox.Enabled = !busy;
        _outputBox.Enabled = !busy;
        _promoteTextures.Enabled = !busy;
        UseWaitCursor = busy;
    }

    private void SetStatus(string text, bool isError = false)
    {
        _status.Text = text;
        _status.ForeColor = isError
            ? Color.FromArgb(180, 40, 40)
            : Color.FromArgb(40, 120, 60);
        if (!isError && text.StartsWith("Select", StringComparison.Ordinal))
            _status.ForeColor = Color.FromArgb(90, 90, 90);
        if (text.Contains("Converting", StringComparison.Ordinal))
            _status.ForeColor = Color.FromArgb(90, 90, 90);
    }

    private void AppendLog(string text)
    {
        if (_log.TextLength > 0)
            _log.AppendText(Environment.NewLine);
        _log.AppendText(text);
        _log.SelectionStart = _log.TextLength;
        _log.ScrollToCaret();
    }

    private sealed class ConvertResult
    {
        public bool Success { get; init; }
        public string Log { get; init; } = "";
        public string? ErrorMessage { get; init; }
        public string? ReadinessText { get; init; }

        public static ConvertResult Ok(string log, string? readiness = null) =>
            new() { Success = true, Log = log, ReadinessText = readiness };
        public static ConvertResult Fail(string error, string log, string? readiness = null) =>
            new() { Success = false, ErrorMessage = error, Log = log, ReadinessText = readiness };
    }
}

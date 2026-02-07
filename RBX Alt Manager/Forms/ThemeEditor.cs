using FastColoredTextBoxNS;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

namespace RBX_Alt_Manager.Forms
{
    public partial class ThemeEditor : Form
    {
        // Padrão: tema Escuro (cinza neutro frio, estilo VS Code Dark+)
        public static Color AccountBackground = Color.FromArgb(28, 28, 33);
        public static Color AccountForeground = Color.FromArgb(210, 210, 215);

        public static Color ButtonsBackground = Color.FromArgb(44, 44, 50);
        public static Color ButtonsForeground = Color.FromArgb(225, 225, 230);
        public static Color ButtonsBorder = Color.FromArgb(58, 58, 65);
        public static FlatStyle ButtonStyle = FlatStyle.Flat;

        public static Color FormsBackground = Color.FromArgb(24, 24, 28);
        public static Color FormsForeground = Color.FromArgb(220, 220, 225);
        public static bool UseDarkTopBar = true;
        public static bool ShowHeaders = true;

        public static Color TextBoxesBackground = Color.FromArgb(34, 34, 40);
        public static Color TextBoxesForeground = Color.FromArgb(215, 215, 220);
        public static Color TextBoxesBorder = Color.FromArgb(55, 55, 62);

        public static Color LabelBackground = Color.FromArgb(34, 34, 40);
        public static Color LabelForeground = Color.FromArgb(210, 210, 215);
        public static bool LabelTransparent = true;

        public static bool LightImages = true;
        // public static bool UseNormalTabControls = false;

        // Wallpaper
        public static string WallpaperPath = "";
        public static int WallpaperOpacity = 40; // 0=sem overlay, 100=overlay total esconde imagem
        public static int PanelTransparency = 100; // 0=painéis opacos, 100=painéis totalmente transparentes

        /// <summary>
        /// Retorna a cor do painel considerando wallpaper e transparência.
        /// WinForms não suporta alpha parcial — usa apenas Transparent ou opaco.
        /// O slider controla o limiar: >0 = transparente, 0 = opaco.
        /// </summary>
        public static Color GetPanelColor(Color baseColor)
        {
            if (string.IsNullOrEmpty(WallpaperPath)) return baseColor;
            if (PanelTransparency > 0) return Color.Transparent;
            return baseColor;
        }

        // Cores derivadas para painéis dinâmicos (inventário, amigos, estoque, diálogos)
        public static Color PanelBackground => DeriveColor(FormsBackground, 0.06f);
        public static Color HeaderBackground => DeriveColor(FormsBackground, -0.03f);
        public static Color ItemBackground => DeriveColor(FormsBackground, 0.10f);
        public static Color InputBackground => DeriveColor(FormsBackground, 0.12f);

        private static Color DeriveColor(Color c, float amount)
        {
            if (c.GetBrightness() < 0.5f)
                return ControlPaint.Light(c, Math.Abs(amount));
            else
                return amount > 0 ? ControlPaint.Light(c, amount) : ControlPaint.Dark(c, Math.Abs(amount));
        }

        /// <summary>
        /// Aplica tema a um Form dinâmico (dialogs criados em runtime).
        /// </summary>
        public static void ThemeDialog(Form form)
        {
            form.BackColor = FormsBackground;
            form.ForeColor = FormsForeground;
            ApplyThemeControls(form.Controls);
        }

        /// <summary>
        /// Aplica tema recursivamente a uma coleção de controles (para uso em painéis dinâmicos).
        /// </summary>
        public static void ApplyThemeControls(Control.ControlCollection controls)
        {
            foreach (Control c in controls)
            {
                if (c is Button btn)
                {
                    btn.ForeColor = ButtonsForeground;
                    if (btn.BackColor == SystemColors.Control) btn.BackColor = ButtonsBackground;
                    btn.FlatStyle = ButtonStyle;
                    btn.FlatAppearance.BorderSize = 1;
                    btn.FlatAppearance.BorderColor = ButtonsBorder;
                }
                else if (c is TextBox || c is RichTextBox)
                {
                    c.BackColor = TextBoxesBackground;
                    c.ForeColor = TextBoxesForeground;
                }
                else if (c is Label lbl)
                {
                    lbl.ForeColor = FormsForeground;
                }
                else if (c is ComboBox combo)
                {
                    combo.BackColor = TextBoxesBackground;
                    combo.ForeColor = TextBoxesForeground;
                }
                else if (c is NumericUpDown num)
                {
                    num.BackColor = TextBoxesBackground;
                    num.ForeColor = TextBoxesForeground;
                }

                if (c.HasChildren && !(c is TextBox) && !(c is RichTextBox) && !(c is ComboBox))
                    ApplyThemeControls(c.Controls);
            }
        }

        public static string ToHexString(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";

        private static IniFile ThemeIni;
        private static IniSection Theme;

        // === INSTANCE FIELDS ===
        private readonly List<Action> _refreshActions = new List<Action>();
        private bool _isRefreshing;

        private Panel _previewInnerPanel;
        private Label _previewLabel;
        private TextBox _previewTextBox;
        private Button _previewButton1;
        private Button _previewButton2;

        // Snapshot para "Restaurar Original"
        private struct ThemeSnapshot
        {
            public Color AccBg, AccFg, BtnBg, BtnFg, BtnBorder;
            public FlatStyle BtnStyle;
            public Color FormBg, FormFg;
            public bool DarkTop, Headers;
            public Color TbBg, TbFg, TbBorder;
            public Color LblBg, LblFg;
            public bool LblTransp, Light;
        }
        private ThemeSnapshot _original;

        private ThemeSnapshot CaptureSnapshot() => new ThemeSnapshot
        {
            AccBg = AccountBackground, AccFg = AccountForeground,
            BtnBg = ButtonsBackground, BtnFg = ButtonsForeground, BtnBorder = ButtonsBorder, BtnStyle = ButtonStyle,
            FormBg = FormsBackground, FormFg = FormsForeground, DarkTop = UseDarkTopBar, Headers = ShowHeaders,
            TbBg = TextBoxesBackground, TbFg = TextBoxesForeground, TbBorder = TextBoxesBorder,
            LblBg = LabelBackground, LblFg = LabelForeground, LblTransp = LabelTransparent, Light = LightImages
        };

        private void RestoreSnapshot(ThemeSnapshot s)
        {
            AccountBackground = s.AccBg; AccountForeground = s.AccFg;
            ButtonsBackground = s.BtnBg; ButtonsForeground = s.BtnFg; ButtonsBorder = s.BtnBorder; ButtonStyle = s.BtnStyle;
            FormsBackground = s.FormBg; FormsForeground = s.FormFg; UseDarkTopBar = s.DarkTop; ShowHeaders = s.Headers;
            TextBoxesBackground = s.TbBg; TextBoxesForeground = s.TbFg; TextBoxesBorder = s.TbBorder;
            LabelBackground = s.LblBg; LabelForeground = s.LblFg; LabelTransparent = s.LblTransp; LightImages = s.Light;
        }

        public ThemeEditor()
        {
            AccountManager.SetDarkBar(Handle);

            InitializeComponent();
            this.Rescale();
            BuildEditorPanel();
            SetupPreview();
            ApplyTheme();
        }

        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            if (Visible)
                _original = CaptureSnapshot();
        }

        public void ApplyTheme()
        {
            BackColor = FormsBackground;
            ForeColor = FormsForeground;

            pnlPresets.BackColor = HeaderBackground;
            pnlBottom.BackColor = FormsBackground;
            pnlMain.BackColor = FormsBackground;
            grpPreview.ForeColor = FormsForeground;

            foreach (Control c in pnlPresets.Controls)
            {
                if (c is Button btn)
                {
                    btn.FlatStyle = ButtonStyle;
                    btn.BackColor = ButtonsBackground;
                    btn.ForeColor = ButtonsForeground;
                    btn.FlatAppearance.BorderColor = ButtonsBorder;
                }
            }

            ThemeButton(btnSalvar);
            ThemeButton(btnFechar);

            RefreshAllSwatches();
            UpdatePreview();
        }

        private void ThemeButton(Button btn)
        {
            btn.FlatStyle = ButtonStyle;
            btn.BackColor = ButtonsBackground;
            btn.ForeColor = ButtonsForeground;
            btn.FlatAppearance.BorderColor = ButtonsBorder;
        }

        public static void LoadTheme()
        {
            ThemeIni ??= File.Exists(Path.Combine(Environment.CurrentDirectory, "RAMTheme.ini")) ? new IniFile("RAMTheme.ini") : new IniFile();

            Theme = ThemeIni.Section(Assembly.GetExecutingAssembly().GetName().Name);

            // bool.TryParse(Theme.Get("DisableCustomTabs"), out UseNormalTabControls);

            // if (!Theme.Exists("DisableCustomTabs")) { Theme.Set("DisableCustomTabs", "false"); ThemeIni.Save("RAMTheme.ini"); }

            if (Theme.Exists("AccountsBG")) AccountBackground = ColorTranslator.FromHtml(Theme.Get("AccountsBG"));
            if (Theme.Exists("AccountsFG")) AccountForeground = ColorTranslator.FromHtml(Theme.Get("AccountsFG"));

            if (Theme.Exists("ButtonsBG")) ButtonsBackground = ColorTranslator.FromHtml(Theme.Get("ButtonsBG"));
            if (Theme.Exists("ButtonsFG")) ButtonsForeground = ColorTranslator.FromHtml(Theme.Get("ButtonsFG"));
            if (Theme.Exists("ButtonsBC")) ButtonsBorder = ColorTranslator.FromHtml(Theme.Get("ButtonsBC"));
            if (Theme.Exists("ButtonStyle") && Enum.TryParse(Theme.Get("ButtonStyle"), out FlatStyle BS)) ButtonStyle = BS;

            if (Theme.Exists("FormsBG")) FormsBackground = ColorTranslator.FromHtml(Theme.Get("FormsBG"));
            if (Theme.Exists("FormsFG")) FormsForeground = ColorTranslator.FromHtml(Theme.Get("FormsFG"));
            if (Theme.Exists("DarkTopBar") && bool.TryParse(Theme.Get("DarkTopBar"), out bool DarkTopBar)) UseDarkTopBar = DarkTopBar;
            if (Theme.Exists("ShowHeaders") && bool.TryParse(Theme.Get("ShowHeaders"), out bool bShowHeaders)) ShowHeaders = bShowHeaders;

            if (Theme.Exists("TextBoxesBG")) TextBoxesBackground = ColorTranslator.FromHtml(Theme.Get("TextBoxesBG"));
            if (Theme.Exists("TextBoxesFG")) TextBoxesForeground = ColorTranslator.FromHtml(Theme.Get("TextBoxesFG"));
            if (Theme.Exists("TextBoxesBC")) TextBoxesBorder = ColorTranslator.FromHtml(Theme.Get("TextBoxesBC"));

            if (Theme.Exists("TextBoxesBG") && !Theme.Exists("LabelsTransparent")) LabelTransparent = false; // support old themes
            if (Theme.Exists("LabelsBC")) LabelBackground = ColorTranslator.FromHtml(Theme.Get("LabelsBC")); else LabelBackground = TextBoxesBackground;
            if (Theme.Exists("LabelsFC")) LabelForeground = ColorTranslator.FromHtml(Theme.Get("LabelsFC")); else LabelForeground = TextBoxesForeground;
            if (Theme.Exists("LabelsTransparent") && bool.TryParse(Theme.Get("LabelsTransparent"), out bool bLabelTransparent)) LabelTransparent = bLabelTransparent;

            if (!Theme.Exists("LightImages")) Theme.Set("LightImages", FormsBackground.GetBrightness() < 0.5 ? "true" : "false");
            if (bool.TryParse(Theme.Get("LightImages"), out bool bLightImages)) LightImages = bLightImages;

            if (Theme.Exists("WallpaperPath")) WallpaperPath = Theme.Get("WallpaperPath");
            if (Theme.Exists("WallpaperOpacity") && int.TryParse(Theme.Get("WallpaperOpacity"), out int wpOp)) WallpaperOpacity = Math.Max(0, Math.Min(100, wpOp));
            if (Theme.Exists("PanelTransparency") && int.TryParse(Theme.Get("PanelTransparency"), out int ptVal)) PanelTransparency = Math.Max(0, Math.Min(100, ptVal));
        }

        public static void SaveTheme()
        {
            ThemeIni ??= File.Exists(Path.Combine(Environment.CurrentDirectory, "RAMTheme.ini")) ? new IniFile("RAMTheme.ini") : new IniFile();
            Theme ??= ThemeIni.Section(Assembly.GetExecutingAssembly().GetName().Name);

            Theme.Set("AccountsBG", ToHexString(AccountBackground));
            Theme.Set("AccountsFG", ToHexString(AccountForeground));

            Theme.Set("ButtonsBG", ToHexString(ButtonsBackground));
            Theme.Set("ButtonsFG", ToHexString(ButtonsForeground));
            Theme.Set("ButtonsBC", ToHexString(ButtonsBorder));
            Theme.Set("ButtonStyle", ButtonStyle.ToString());

            Theme.Set("FormsBG", ToHexString(FormsBackground));
            Theme.Set("FormsFG", ToHexString(FormsForeground));
            Theme.Set("DarkTopBar", UseDarkTopBar.ToString());
            Theme.Set("ShowHeaders", ShowHeaders.ToString());

            Theme.Set("TextBoxesBG", ToHexString(TextBoxesBackground));
            Theme.Set("TextBoxesFG", ToHexString(TextBoxesForeground));
            Theme.Set("TextBoxesBC", ToHexString(TextBoxesBorder));

            Theme.Set("LabelsBC", ToHexString(LabelBackground));
            Theme.Set("LabelsFC", ToHexString(LabelForeground));
            Theme.Set("LabelsTransparent", LabelTransparent.ToString());

            Theme.Set("LightImages", LightImages.ToString());

            Theme.Set("WallpaperPath", WallpaperPath ?? "");
            Theme.Set("WallpaperOpacity", WallpaperOpacity.ToString());
            Theme.Set("PanelTransparency", PanelTransparency.ToString());

            ThemeIni.Save("RAMTheme.ini");
        }

        // === EVENT HANDLERS ===

        private void ThemeEditor_FormClosing(object sender, FormClosingEventArgs e)
        {
            Hide();
            e.Cancel = true;
        }

        private void btnPresetEscuro_Click(object sender, EventArgs e) => ApplyPresetEscuro();
        private void btnPresetClaro_Click(object sender, EventArgs e) => ApplyPresetClaro();
        private void btnPresetAzul_Click(object sender, EventArgs e) => ApplyPresetAzul();
        private void btnPresetRoxo_Click(object sender, EventArgs e) => ApplyPresetRoxo();
        private void btnPresetVerde_Click(object sender, EventArgs e) => ApplyPresetVerde();
        private void btnRestaurar_Click(object sender, EventArgs e)
        {
            RestoreSnapshot(_original);
            OnColorChanged();
        }

        private void btnSalvar_Click(object sender, EventArgs e)
        {
            SaveTheme();
            AccountManager.Instance.ApplyTheme();
            Close();
        }

        private void btnFechar_Click(object sender, EventArgs e)
        {
            Close();
        }

        // === BUILD DYNAMIC EDITOR UI ===

        private void BuildEditorPanel()
        {
            int y = 10;

            // CONTAS
            y = AddCategoryHeader("CONTAS", y);
            y = AddColorRowPair("Fundo", () => AccountBackground, c => AccountBackground = c,
                                "Texto", () => AccountForeground, c => AccountForeground = c, y);
            y += 8;

            // BOTÕES
            y = AddCategoryHeader("BOTÕES", y);
            y = AddColorRowPair("Fundo", () => ButtonsBackground, c => ButtonsBackground = c,
                                "Texto", () => ButtonsForeground, c => ButtonsForeground = c, y);
            y = AddColorRow("Borda", () => ButtonsBorder, c => ButtonsBorder = c, y);
            y = AddButtonStyleRow(y);
            y += 8;

            // FORMULÁRIOS
            y = AddCategoryHeader("FORMULÁRIOS", y);
            y = AddColorRowPair("Fundo", () => FormsBackground, c => { FormsBackground = c; LightImages = c.GetBrightness() < 0.5; },
                                "Texto", () => FormsForeground, c => FormsForeground = c, y);
            y = AddCheckboxRow("Barra de título escura", () => UseDarkTopBar, v => UseDarkTopBar = v, y);
            y = AddCheckboxRow("Mostrar cabeçalhos", () => ShowHeaders, v => ShowHeaders = v, y);
            y += 8;

            // CAIXAS DE TEXTO
            y = AddCategoryHeader("CAIXAS DE TEXTO", y);
            y = AddColorRowPair("Fundo", () => TextBoxesBackground, c => TextBoxesBackground = c,
                                "Texto", () => TextBoxesForeground, c => TextBoxesForeground = c, y);
            y = AddColorRow("Borda", () => TextBoxesBorder, c => TextBoxesBorder = c, y);
            y += 8;

            // LABELS
            y = AddCategoryHeader("LABELS", y);
            y = AddColorRowPair("Fundo", () => LabelBackground, c => LabelBackground = c,
                                "Texto", () => LabelForeground, c => LabelForeground = c, y);
            y = AddCheckboxRow("Fundo transparente", () => LabelTransparent, v => LabelTransparent = v, y);
            y += 8;

            // IMAGENS
            y = AddCategoryHeader("IMAGENS", y);
            y = AddCheckboxRow("Ícones claros", () => LightImages, v => LightImages = v, y);
            y += 8;

            // WALLPAPER
            y = AddCategoryHeader("WALLPAPER", y);
            y = AddWallpaperSection(y);
            y += 8;

            // CORES DERIVADAS (somente leitura)
            y = AddCategoryHeader("CORES DERIVADAS (automáticas)", y);
            y = AddReadOnlyColorPair("Painel", () => PanelBackground, "Cabeçalho", () => HeaderBackground, y);
            y = AddReadOnlyColorPair("Item", () => ItemBackground, "Input", () => InputBackground, y);
        }

        // --- UI Helper Methods ---

        private int AddCategoryHeader(string text, int y)
        {
            var lbl = new Label
            {
                Text = text,
                Font = new Font(Font.FontFamily, 9f, FontStyle.Bold),
                ForeColor = FormsForeground,
                AutoSize = true,
                Location = new Point(10, y),
            };

            pnlMain.Controls.Add(lbl);
            _refreshActions.Add(() => lbl.ForeColor = FormsForeground);
            return y + 22;
        }

        private int AddColorRow(string labelText, Func<Color> getter, Action<Color> setter, int y)
        {
            AddColorSwatchAt(labelText, getter, setter, 25, y);
            return y + 28;
        }

        private int AddColorRowPair(string label1, Func<Color> getter1, Action<Color> setter1,
                                     string label2, Func<Color> getter2, Action<Color> setter2, int y)
        {
            AddColorSwatchAt(label1, getter1, setter1, 25, y);
            AddColorSwatchAt(label2, getter2, setter2, 240, y);
            return y + 28;
        }

        private void AddColorSwatchAt(string labelText, Func<Color> getter, Action<Color> setter, int x, int y)
        {
            var lbl = new Label
            {
                Text = labelText,
                ForeColor = FormsForeground,
                AutoSize = false,
                Size = new Size(65, 24),
                Location = new Point(x, y),
                TextAlign = ContentAlignment.MiddleLeft
            };

            var swatch = new Panel
            {
                Size = new Size(24, 24),
                Location = new Point(x + 68, y),
                BackColor = getter(),
                BorderStyle = BorderStyle.FixedSingle,
                Cursor = Cursors.Hand
            };

            var hexLbl = new Label
            {
                Text = ToHexString(getter()),
                ForeColor = FormsForeground,
                AutoSize = false,
                Size = new Size(70, 24),
                Location = new Point(x + 96, y),
                TextAlign = ContentAlignment.MiddleLeft,
                Font = new Font("Consolas", 8.5f)
            };

            swatch.Click += (s, e) =>
            {
                SelectColor.Color = getter();
                if (SelectColor.ShowDialog() == DialogResult.OK)
                {
                    setter(SelectColor.Color);
                    OnColorChanged();
                }
            };

            pnlMain.Controls.Add(lbl);
            pnlMain.Controls.Add(swatch);
            pnlMain.Controls.Add(hexLbl);

            _refreshActions.Add(() =>
            {
                lbl.ForeColor = FormsForeground;
                swatch.BackColor = getter();
                hexLbl.Text = ToHexString(getter());
                hexLbl.ForeColor = FormsForeground;
            });
        }

        private int AddReadOnlyColorPair(string label1, Func<Color> getter1, string label2, Func<Color> getter2, int y)
        {
            AddReadOnlySwatchAt(label1, getter1, 25, y);
            AddReadOnlySwatchAt(label2, getter2, 240, y);
            return y + 28;
        }

        private void AddReadOnlySwatchAt(string labelText, Func<Color> getter, int x, int y)
        {
            var lbl = new Label
            {
                Text = labelText,
                ForeColor = FormsForeground,
                AutoSize = false,
                Size = new Size(65, 24),
                Location = new Point(x, y),
                TextAlign = ContentAlignment.MiddleLeft
            };

            var swatch = new Panel
            {
                Size = new Size(24, 24),
                Location = new Point(x + 68, y),
                BackColor = getter(),
                BorderStyle = BorderStyle.FixedSingle,
            };

            var hexLbl = new Label
            {
                Text = ToHexString(getter()),
                ForeColor = FormsForeground,
                AutoSize = false,
                Size = new Size(70, 24),
                Location = new Point(x + 96, y),
                TextAlign = ContentAlignment.MiddleLeft,
                Font = new Font("Consolas", 8.5f)
            };

            pnlMain.Controls.Add(lbl);
            pnlMain.Controls.Add(swatch);
            pnlMain.Controls.Add(hexLbl);

            _refreshActions.Add(() =>
            {
                lbl.ForeColor = FormsForeground;
                swatch.BackColor = getter();
                hexLbl.Text = ToHexString(getter());
                hexLbl.ForeColor = FormsForeground;
            });
        }

        private int AddCheckboxRow(string text, Func<bool> getter, Action<bool> setter, int y)
        {
            var cb = new CheckBox
            {
                Text = text,
                Checked = getter(),
                ForeColor = FormsForeground,
                AutoSize = true,
                Location = new Point(25, y + 2),
            };

            cb.CheckedChanged += (s, e) =>
            {
                if (_isRefreshing) return;
                setter(cb.Checked);
                OnColorChanged();
            };

            pnlMain.Controls.Add(cb);

            _refreshActions.Add(() =>
            {
                cb.ForeColor = FormsForeground;
                cb.Checked = getter();
            });

            return y + 26;
        }

        private Label _wallpaperFileLabel;

        private int AddWallpaperSection(int y)
        {
            // Label do arquivo atual
            _wallpaperFileLabel = new Label
            {
                Text = string.IsNullOrEmpty(WallpaperPath) ? "(nenhum)" : WallpaperPath,
                ForeColor = FormsForeground,
                AutoSize = false,
                Size = new Size(300, 20),
                Location = new Point(25, y),
                TextAlign = ContentAlignment.MiddleLeft
            };
            pnlMain.Controls.Add(_wallpaperFileLabel);
            y += 24;

            // Botão Selecionar
            var btnSelect = new Button
            {
                Text = "Selecionar Imagem",
                ForeColor = ButtonsForeground,
                BackColor = ButtonsBackground,
                FlatStyle = FlatStyle.Flat,
                Size = new Size(140, 28),
                Location = new Point(25, y),
                Cursor = Cursors.Hand
            };
            btnSelect.FlatAppearance.BorderColor = ButtonsBorder;
            btnSelect.Click += (s, e) =>
            {
                using (var dlg = new OpenFileDialog())
                {
                    dlg.Filter = "Imagens e Vídeos|*.jpg;*.jpeg;*.png;*.bmp;*.gif;*.mp4;*.webm;*.avi;*.mkv;*.wmv;*.mov|Imagens|*.jpg;*.jpeg;*.png;*.bmp;*.gif|Vídeos|*.mp4;*.webm;*.avi;*.mkv;*.wmv;*.mov|Todos|*.*";
                    dlg.Title = "Selecionar Wallpaper";
                    if (dlg.ShowDialog() == DialogResult.OK)
                    {
                        var wpDir = Path.Combine(Environment.CurrentDirectory, "wallpapers");
                        if (!Directory.Exists(wpDir)) Directory.CreateDirectory(wpDir);

                        var fileName = Path.GetFileName(dlg.FileName);
                        var destPath = Path.Combine(wpDir, fileName);

                        if (!string.Equals(dlg.FileName, destPath, StringComparison.OrdinalIgnoreCase))
                            File.Copy(dlg.FileName, destPath, true);

                        WallpaperPath = fileName;
                        _wallpaperFileLabel.Text = fileName;
                        OnColorChanged();
                    }
                }
            };
            pnlMain.Controls.Add(btnSelect);

            // Botão Remover
            var btnRemove = new Button
            {
                Text = "Remover",
                ForeColor = ButtonsForeground,
                BackColor = ButtonsBackground,
                FlatStyle = FlatStyle.Flat,
                Size = new Size(80, 28),
                Location = new Point(175, y),
                Cursor = Cursors.Hand
            };
            btnRemove.FlatAppearance.BorderColor = ButtonsBorder;
            btnRemove.Click += (s, e) =>
            {
                WallpaperPath = "";
                _wallpaperFileLabel.Text = "(nenhum)";
                OnColorChanged();
            };
            pnlMain.Controls.Add(btnRemove);
            y += 34;

            // Label Opacidade
            var lblOpacity = new Label
            {
                Text = "Escurecimento:",
                ForeColor = FormsForeground,
                AutoSize = false,
                Size = new Size(100, 20),
                Location = new Point(25, y + 4),
                TextAlign = ContentAlignment.MiddleLeft
            };
            pnlMain.Controls.Add(lblOpacity);

            var lblOpVal = new Label
            {
                Text = $"{WallpaperOpacity}%",
                ForeColor = FormsForeground,
                AutoSize = false,
                Size = new Size(40, 20),
                Location = new Point(340, y + 4),
                TextAlign = ContentAlignment.MiddleLeft
            };
            pnlMain.Controls.Add(lblOpVal);

            var slider = new TrackBar
            {
                Minimum = 0,
                Maximum = 100,
                Value = WallpaperOpacity,
                TickFrequency = 10,
                SmallChange = 5,
                LargeChange = 10,
                Size = new Size(200, 30),
                Location = new Point(130, y),
                BackColor = FormsBackground
            };
            slider.ValueChanged += (s, e) =>
            {
                WallpaperOpacity = slider.Value;
                lblOpVal.Text = $"{slider.Value}%";
                OnColorChanged();
            };
            pnlMain.Controls.Add(slider);
            y += 36;

            // Slider: Transparência dos painéis
            var lblPanelTr = new Label
            {
                Text = "Painéis:",
                ForeColor = FormsForeground,
                AutoSize = false,
                Size = new Size(100, 20),
                Location = new Point(25, y + 4),
                TextAlign = ContentAlignment.MiddleLeft
            };
            pnlMain.Controls.Add(lblPanelTr);

            var lblPanelTrVal = new Label
            {
                Text = $"{PanelTransparency}%",
                ForeColor = FormsForeground,
                AutoSize = false,
                Size = new Size(40, 20),
                Location = new Point(340, y + 4),
                TextAlign = ContentAlignment.MiddleLeft
            };
            pnlMain.Controls.Add(lblPanelTrVal);

            var sliderPanel = new TrackBar
            {
                Minimum = 0,
                Maximum = 100,
                Value = PanelTransparency,
                TickFrequency = 10,
                SmallChange = 5,
                LargeChange = 10,
                Size = new Size(200, 30),
                Location = new Point(130, y),
                BackColor = FormsBackground
            };
            sliderPanel.ValueChanged += (s, e) =>
            {
                PanelTransparency = sliderPanel.Value;
                lblPanelTrVal.Text = $"{sliderPanel.Value}%";
                OnColorChanged();
            };
            pnlMain.Controls.Add(sliderPanel);
            y += 36;

            _refreshActions.Add(() =>
            {
                _wallpaperFileLabel.Text = string.IsNullOrEmpty(WallpaperPath) ? "(nenhum)" : WallpaperPath;
                _wallpaperFileLabel.ForeColor = FormsForeground;
                lblOpacity.ForeColor = FormsForeground;
                lblOpVal.ForeColor = FormsForeground;
                lblOpVal.Text = $"{WallpaperOpacity}%";
                slider.Value = WallpaperOpacity;
                slider.BackColor = FormsBackground;
                lblPanelTr.ForeColor = FormsForeground;
                lblPanelTrVal.ForeColor = FormsForeground;
                lblPanelTrVal.Text = $"{PanelTransparency}%";
                sliderPanel.Value = PanelTransparency;
                sliderPanel.BackColor = FormsBackground;
                btnSelect.ForeColor = ButtonsForeground;
                btnSelect.BackColor = ButtonsBackground;
                btnSelect.FlatAppearance.BorderColor = ButtonsBorder;
                btnRemove.ForeColor = ButtonsForeground;
                btnRemove.BackColor = ButtonsBackground;
                btnRemove.FlatAppearance.BorderColor = ButtonsBorder;
            });

            return y;
        }

        private int AddButtonStyleRow(int y)
        {
            var lbl = new Label
            {
                Text = "Estilo:",
                ForeColor = FormsForeground,
                AutoSize = false,
                Size = new Size(65, 24),
                Location = new Point(25, y),
                TextAlign = ContentAlignment.MiddleLeft
            };

            var combo = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Location = new Point(93, y),
                Size = new Size(130, 24),
                BackColor = TextBoxesBackground,
                ForeColor = TextBoxesForeground,
            };

            combo.Items.AddRange(Enum.GetNames(typeof(FlatStyle)));
            combo.SelectedItem = ButtonStyle.ToString();

            combo.SelectedIndexChanged += (s, e) =>
            {
                if (_isRefreshing) return;
                if (Enum.TryParse(combo.SelectedItem?.ToString(), out FlatStyle style))
                {
                    ButtonStyle = style;
                    OnColorChanged();
                }
            };

            pnlMain.Controls.Add(lbl);
            pnlMain.Controls.Add(combo);

            _refreshActions.Add(() =>
            {
                lbl.ForeColor = FormsForeground;
                combo.BackColor = TextBoxesBackground;
                combo.ForeColor = TextBoxesForeground;
                combo.SelectedItem = ButtonStyle.ToString();
            });

            return y + 30;
        }

        // === REFRESH / PREVIEW / COLOR CHANGED ===

        private void OnColorChanged()
        {
            SaveTheme();
            AccountManager.Instance.ApplyTheme();
        }

        private void RefreshAllSwatches()
        {
            _isRefreshing = true;
            foreach (var action in _refreshActions)
                action();
            _isRefreshing = false;
        }

        private void SetupPreview()
        {
            _previewInnerPanel = new Panel
            {
                Location = new Point(10, 18),
                Size = new Size(grpPreview.ClientSize.Width - 20, grpPreview.ClientSize.Height - 28),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom,
                BackColor = FormsBackground,
                BorderStyle = BorderStyle.FixedSingle
            };

            _previewLabel = new Label
            {
                Text = "Label exemplo",
                Location = new Point(10, 14),
                AutoSize = true,
            };

            _previewTextBox = new TextBox
            {
                Text = "Texto de exemplo",
                Location = new Point(120, 12),
                Size = new Size(130, 22),
            };

            _previewButton1 = new Button
            {
                Text = "Botão 1",
                Location = new Point(260, 10),
                Size = new Size(75, 26),
            };

            _previewButton2 = new Button
            {
                Text = "Botão 2",
                Location = new Point(340, 10),
                Size = new Size(75, 26),
            };

            _previewInnerPanel.Controls.AddRange(new Control[] { _previewLabel, _previewTextBox, _previewButton1, _previewButton2 });
            grpPreview.Controls.Add(_previewInnerPanel);
        }

        private void UpdatePreview()
        {
            if (_previewInnerPanel == null) return;

            _previewInnerPanel.BackColor = FormsBackground;
            _previewLabel.ForeColor = LabelTransparent ? FormsForeground : LabelForeground;
            _previewLabel.BackColor = LabelTransparent ? Color.Transparent : LabelBackground;
            _previewTextBox.BackColor = TextBoxesBackground;
            _previewTextBox.ForeColor = TextBoxesForeground;
            _previewButton1.BackColor = ButtonsBackground;
            _previewButton1.ForeColor = ButtonsForeground;
            _previewButton1.FlatStyle = ButtonStyle;
            _previewButton1.FlatAppearance.BorderColor = ButtonsBorder;
            _previewButton2.BackColor = ButtonsBackground;
            _previewButton2.ForeColor = ButtonsForeground;
            _previewButton2.FlatStyle = ButtonStyle;
            _previewButton2.FlatAppearance.BorderColor = ButtonsBorder;
        }

        // === PRESETS ===

        // Escuro — cinza neutro frio, inspirado em editores de código modernos (VS Code Dark+)
        private void ApplyPresetEscuro()
        {
            FormsBackground = Color.FromArgb(24, 24, 28);
            FormsForeground = Color.FromArgb(220, 220, 225);
            AccountBackground = Color.FromArgb(28, 28, 33);
            AccountForeground = Color.FromArgb(210, 210, 215);
            ButtonsBackground = Color.FromArgb(44, 44, 50);
            ButtonsForeground = Color.FromArgb(225, 225, 230);
            ButtonsBorder = Color.FromArgb(58, 58, 65);
            ButtonStyle = FlatStyle.Flat;
            TextBoxesBackground = Color.FromArgb(34, 34, 40);
            TextBoxesForeground = Color.FromArgb(215, 215, 220);
            TextBoxesBorder = Color.FromArgb(55, 55, 62);
            LabelBackground = Color.FromArgb(34, 34, 40);
            LabelForeground = Color.FromArgb(210, 210, 215);
            LabelTransparent = true;
            UseDarkTopBar = true;
            ShowHeaders = true;
            LightImages = true;
            OnColorChanged();
        }

        // Claro — branco acinzentado com contraste suave, estilo Apple/Material Design
        private void ApplyPresetClaro()
        {
            FormsBackground = Color.FromArgb(246, 246, 249);
            FormsForeground = Color.FromArgb(28, 28, 32);
            AccountBackground = Color.FromArgb(238, 238, 242);
            AccountForeground = Color.FromArgb(32, 32, 36);
            ButtonsBackground = Color.FromArgb(228, 228, 234);
            ButtonsForeground = Color.FromArgb(32, 32, 36);
            ButtonsBorder = Color.FromArgb(195, 195, 205);
            ButtonStyle = FlatStyle.Flat;
            TextBoxesBackground = Color.FromArgb(255, 255, 255);
            TextBoxesForeground = Color.FromArgb(32, 32, 36);
            TextBoxesBorder = Color.FromArgb(200, 200, 210);
            LabelBackground = Color.White;
            LabelForeground = Color.FromArgb(40, 40, 45);
            LabelTransparent = true;
            UseDarkTopBar = false;
            ShowHeaders = true;
            LightImages = false;
            OnColorChanged();
        }

        // Azul — navy profundo, estilo Discord/Slack
        private void ApplyPresetAzul()
        {
            FormsBackground = Color.FromArgb(18, 25, 38);
            FormsForeground = Color.FromArgb(185, 200, 225);
            AccountBackground = Color.FromArgb(22, 30, 44);
            AccountForeground = Color.FromArgb(180, 195, 220);
            ButtonsBackground = Color.FromArgb(35, 48, 72);
            ButtonsForeground = Color.FromArgb(210, 220, 240);
            ButtonsBorder = Color.FromArgb(50, 65, 95);
            ButtonStyle = FlatStyle.Flat;
            TextBoxesBackground = Color.FromArgb(26, 36, 54);
            TextBoxesForeground = Color.FromArgb(200, 212, 235);
            TextBoxesBorder = Color.FromArgb(45, 58, 85);
            LabelBackground = Color.FromArgb(26, 36, 54);
            LabelForeground = Color.FromArgb(185, 200, 225);
            LabelTransparent = true;
            UseDarkTopBar = true;
            ShowHeaders = true;
            LightImages = true;
            OnColorChanged();
        }

        // Roxo — violeta escuro, estilo Dracula/Cyberpunk
        private void ApplyPresetRoxo()
        {
            FormsBackground = Color.FromArgb(22, 18, 32);
            FormsForeground = Color.FromArgb(205, 195, 225);
            AccountBackground = Color.FromArgb(26, 22, 38);
            AccountForeground = Color.FromArgb(200, 190, 220);
            ButtonsBackground = Color.FromArgb(42, 35, 58);
            ButtonsForeground = Color.FromArgb(220, 210, 240);
            ButtonsBorder = Color.FromArgb(58, 48, 80);
            ButtonStyle = FlatStyle.Flat;
            TextBoxesBackground = Color.FromArgb(30, 25, 44);
            TextBoxesForeground = Color.FromArgb(210, 200, 230);
            TextBoxesBorder = Color.FromArgb(52, 44, 72);
            LabelBackground = Color.FromArgb(30, 25, 44);
            LabelForeground = Color.FromArgb(205, 195, 225);
            LabelTransparent = true;
            UseDarkTopBar = true;
            ShowHeaders = true;
            LightImages = true;
            OnColorChanged();
        }

        // Verde — floresta escura, estilo terminal/matrix
        private void ApplyPresetVerde()
        {
            FormsBackground = Color.FromArgb(18, 26, 22);
            FormsForeground = Color.FromArgb(190, 215, 200);
            AccountBackground = Color.FromArgb(22, 30, 26);
            AccountForeground = Color.FromArgb(185, 210, 195);
            ButtonsBackground = Color.FromArgb(34, 50, 42);
            ButtonsForeground = Color.FromArgb(210, 230, 218);
            ButtonsBorder = Color.FromArgb(48, 68, 56);
            ButtonStyle = FlatStyle.Flat;
            TextBoxesBackground = Color.FromArgb(26, 38, 32);
            TextBoxesForeground = Color.FromArgb(200, 222, 210);
            TextBoxesBorder = Color.FromArgb(42, 60, 50);
            LabelBackground = Color.FromArgb(26, 38, 32);
            LabelForeground = Color.FromArgb(190, 215, 200);
            LabelTransparent = true;
            UseDarkTopBar = true;
            ShowHeaders = true;
            LightImages = true;
            OnColorChanged();
        }
    }
}

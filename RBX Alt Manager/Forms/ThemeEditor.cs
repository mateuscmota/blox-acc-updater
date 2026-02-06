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
        public static Color AccountBackground = SystemColors.Control;
        public static Color AccountForeground = SystemColors.ControlText;

        public static Color ButtonsBackground = SystemColors.Control;
        public static Color ButtonsForeground = SystemColors.ControlText;
        public static Color ButtonsBorder = SystemColors.Control;
        public static FlatStyle ButtonStyle = FlatStyle.Standard;

        public static Color FormsBackground = SystemColors.Control;
        public static Color FormsForeground = SystemColors.ControlText;
        public static bool UseDarkTopBar = true;
        public static bool ShowHeaders = true;

        public static Color TextBoxesBackground = SystemColors.Control;
        public static Color TextBoxesForeground = SystemColors.ControlText;
        public static Color TextBoxesBorder = Color.FromArgb(0x7A7A7A);

        public static Color LabelBackground = SystemColors.Control;
        public static Color LabelForeground = SystemColors.ControlText;
        public static bool LabelTransparent = true;

        public static bool LightImages = false;
        // public static bool UseNormalTabControls = false;

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
        private void btnPresetResetar_Click(object sender, EventArgs e) => ApplyPresetResetar();
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

        private void ApplyPresetEscuro()
        {
            AccountBackground = Color.FromArgb(30, 31, 40);
            AccountForeground = Color.White;
            ButtonsBackground = Color.FromArgb(55, 55, 65);
            ButtonsForeground = Color.White;
            ButtonsBorder = Color.FromArgb(70, 70, 80);
            ButtonStyle = FlatStyle.Flat;
            FormsBackground = Color.FromArgb(30, 31, 40);
            FormsForeground = Color.White;
            UseDarkTopBar = true;
            ShowHeaders = true;
            TextBoxesBackground = Color.FromArgb(40, 42, 54);
            TextBoxesForeground = Color.FromArgb(230, 230, 230);
            TextBoxesBorder = Color.FromArgb(70, 70, 80);
            LabelBackground = Color.FromArgb(40, 42, 54);
            LabelForeground = Color.White;
            LabelTransparent = true;
            LightImages = true;
            OnColorChanged();
        }

        private void ApplyPresetClaro()
        {
            AccountBackground = Color.FromArgb(240, 240, 245);
            AccountForeground = Color.FromArgb(30, 30, 30);
            ButtonsBackground = Color.FromArgb(225, 225, 230);
            ButtonsForeground = Color.FromArgb(30, 30, 30);
            ButtonsBorder = Color.FromArgb(180, 180, 185);
            ButtonStyle = FlatStyle.Flat;
            FormsBackground = Color.FromArgb(245, 245, 250);
            FormsForeground = Color.FromArgb(30, 30, 30);
            UseDarkTopBar = false;
            ShowHeaders = true;
            TextBoxesBackground = Color.White;
            TextBoxesForeground = Color.FromArgb(30, 30, 30);
            TextBoxesBorder = Color.FromArgb(180, 180, 185);
            LabelBackground = Color.White;
            LabelForeground = Color.FromArgb(30, 30, 30);
            LabelTransparent = true;
            LightImages = false;
            OnColorChanged();
        }

        private void ApplyPresetAzul()
        {
            AccountBackground = Color.FromArgb(25, 35, 55);
            AccountForeground = Color.FromArgb(200, 220, 255);
            ButtonsBackground = Color.FromArgb(40, 60, 100);
            ButtonsForeground = Color.White;
            ButtonsBorder = Color.FromArgb(60, 80, 130);
            ButtonStyle = FlatStyle.Flat;
            FormsBackground = Color.FromArgb(20, 30, 50);
            FormsForeground = Color.FromArgb(200, 220, 255);
            UseDarkTopBar = true;
            ShowHeaders = true;
            TextBoxesBackground = Color.FromArgb(30, 45, 70);
            TextBoxesForeground = Color.FromArgb(220, 230, 255);
            TextBoxesBorder = Color.FromArgb(50, 70, 110);
            LabelBackground = Color.FromArgb(30, 45, 70);
            LabelForeground = Color.FromArgb(200, 220, 255);
            LabelTransparent = true;
            LightImages = true;
            OnColorChanged();
        }

        private void ApplyPresetResetar()
        {
            AccountBackground = SystemColors.Control;
            AccountForeground = SystemColors.ControlText;
            ButtonsBackground = SystemColors.Control;
            ButtonsForeground = SystemColors.ControlText;
            ButtonsBorder = SystemColors.Control;
            ButtonStyle = FlatStyle.Standard;
            FormsBackground = SystemColors.Control;
            FormsForeground = SystemColors.ControlText;
            UseDarkTopBar = true;
            ShowHeaders = true;
            TextBoxesBackground = SystemColors.Control;
            TextBoxesForeground = SystemColors.ControlText;
            TextBoxesBorder = Color.FromArgb(0x7A7A7A);
            LabelBackground = SystemColors.Control;
            LabelForeground = SystemColors.ControlText;
            LabelTransparent = true;
            LightImages = false;
            OnColorChanged();
        }
    }
}

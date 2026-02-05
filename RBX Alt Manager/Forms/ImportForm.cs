using RBX_Alt_Manager.Forms;
using System;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace RBX_Alt_Manager
{
    public partial class ImportForm : Form
    {
        public ImportForm()
        {
            AccountManager.SetDarkBar(Handle);

            InitializeComponent();
            this.Rescale();
        }

        public void ApplyTheme()
        {
            BackColor = ThemeEditor.FormsBackground;
            ForeColor = ThemeEditor.FormsForeground;

            foreach (Control control in this.Controls)
            {
                if (control is Button || control is CheckBox)
                {
                    if (control is Button)
                    {
                        Button b = control as Button;
                        b.FlatStyle = ThemeEditor.ButtonStyle;
                        b.FlatAppearance.BorderColor = ThemeEditor.ButtonsBorder;
                    }

                    if (!(control is CheckBox)) control.BackColor = ThemeEditor.ButtonsBackground;
                    control.ForeColor = ThemeEditor.ButtonsForeground;
                }
                else if (control is TextBox || control is RichTextBox)
                {
                    control.BackColor = ThemeEditor.TextBoxesBackground;
                    control.ForeColor = ThemeEditor.TextBoxesForeground;
                }
                else if (control is Label)
                {
                    control.BackColor = ThemeEditor.LabelTransparent ? Color.Transparent : ThemeEditor.LabelBackground;
                    control.ForeColor = ThemeEditor.LabelForeground;
                }
            }
        }

        private void ImportForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            e.Cancel = true;
            Hide();
        }

        private async void ImportButton_Click(object sender, EventArgs e)
        {
            string[] List = Accounts.Text.Split('\n');
            bool doBypass = BypassCheckBox.Checked;

            ImportButton.Enabled = false;
            BypassCheckBox.Enabled = false;

            int total = List.Where(t => !string.IsNullOrWhiteSpace(t)).Count();
            int current = 0;
            int successCount = 0;
            int failCount = 0;
            System.Collections.Generic.List<string> addedUsernames = new System.Collections.Generic.List<string>();

            Accounts.Text = doBypass 
                ? $"Processando {total} cookie(s) com bypass...\nVeja o progresso no painel de debug." 
                : $"Importando {total} cookie(s)...";

            if (doBypass)
            {
                AccountManager.AddLog($"═══════════════════════════════════════");
                AccountManager.AddLog($"🔄 BYPASS EM MASSA: {total} cookie(s)");
                AccountManager.AddLog($"═══════════════════════════════════════");
            }

            foreach (string Token in List)
            {
                string cleanToken = Token.Trim();
                if (string.IsNullOrEmpty(cleanToken)) continue;

                current++;
                string finalToken = cleanToken;

                if (doBypass)
                {
                    AccountManager.AddLog($"[{current}/{total}] 🔄 Processando cookie...");

                    try
                    {
                        var bypass = new Classes.CookieBypass();
                        
                        var cts = new CancellationTokenSource(TimeSpan.FromMinutes(3));
                        var bypassTask = bypass.AutoBypassAsync(cleanToken);
                        
                        var completedTask = await Task.WhenAny(bypassTask, Task.Delay(180000, cts.Token));
                        
                        if (completedTask == bypassTask)
                        {
                            var (success, newCookie, country, error) = await bypassTask;

                            if (success && !string.IsNullOrEmpty(newCookie))
                            {
                                finalToken = newCookie;
                                AccountManager.AddLogSuccess($"Bypass OK ({country})");
                            }
                            else
                            {
                                AccountManager.AddLogWarning($"Bypass falhou: {error}");
                            }
                        }
                        else
                        {
                            AccountManager.AddLogWarning("Timeout geral (3min)");
                        }
                        
                        cts.Cancel();
                    }
                    catch (Exception ex)
                    {
                        AccountManager.AddLogError($"Erro: {ex.Message}");
                    }
                }

                Account NewAccount = AccountManager.AddAccount(finalToken);

                if (NewAccount != null)
                {
                    AccountManager.AddLogSuccess($"Adicionado: {NewAccount.Username}");
                    addedUsernames.Add(NewAccount.Username);
                    successCount++;
                }
                else
                {
                    AccountManager.AddLogError("Falha ao adicionar conta");
                    failCount++;
                }
            }

            AccountManager.AddLog($"═══════════════════════════════════════");
            AccountManager.AddLog($"📊 CONCLUÍDO: {successCount} OK, {failCount} falhas");
            AccountManager.AddLog($"═══════════════════════════════════════");

            // Mostrar lista de usernames adicionados no final
            if (addedUsernames.Count > 0)
            {
                AccountManager.AddLog($"📋 CONTAS ADICIONADAS:");
                foreach (var username in addedUsernames)
                {
                    AccountManager.AddLog($"   • {username}");
                }
            }

            Accounts.Text = $"✅ Concluído!\n{successCount} adicionadas, {failCount} falhas\n\nLimpando em 5 segundos...";
            
            ImportButton.Enabled = true;
            BypassCheckBox.Enabled = true;

            await Task.Delay(5000);
            Accounts.Text = string.Empty;
        }
    }
}

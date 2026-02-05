using System;
using System.Drawing;
using System.Windows.Forms;
using RBX_Alt_Manager.Classes;

namespace RBX_Alt_Manager.Forms
{
    public partial class LoginForm : Form
    {
        public bool LoginSuccessful { get; private set; }

        private Button btnGoogle;
        private Label lblStatus;

        public LoginForm()
        {
            InitializeComponents();
        }

        private void InitializeComponents()
        {
            // Form settings
            this.Text = "Login - Blox Manager";
            this.Size = new Size(400, 280);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.BackColor = Color.FromArgb(30, 30, 30);
            this.ForeColor = Color.White;
            this.Icon = Properties.Resources.bloxbrasil;

            // Logo/Title
            var titleLabel = new Label
            {
                Text = "\U0001f3ae BLOX MANAGER",
                Font = new Font("Segoe UI", 18F, FontStyle.Bold),
                ForeColor = Color.LimeGreen,
                Location = new Point(20, 30),
                Size = new Size(360, 40),
                TextAlign = ContentAlignment.MiddleCenter
            };

            var subtitleLabel = new Label
            {
                Text = "Sistema de Gerenciamento de Contas",
                Font = new Font("Segoe UI", 9F),
                ForeColor = Color.Gray,
                Location = new Point(20, 70),
                Size = new Size(360, 20),
                TextAlign = ContentAlignment.MiddleCenter
            };

            // Status label
            lblStatus = new Label
            {
                Text = "",
                Location = new Point(50, 110),
                Size = new Size(280, 20),
                ForeColor = Color.Red,
                Font = new Font("Segoe UI", 9F),
                TextAlign = ContentAlignment.MiddleCenter
            };

            // Google login button
            btnGoogle = new Button
            {
                Text = "G  Entrar com Google",
                Location = new Point(50, 145),
                Size = new Size(280, 45),
                BackColor = Color.FromArgb(255, 255, 255),
                ForeColor = Color.FromArgb(60, 60, 60),
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 11F, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            btnGoogle.FlatAppearance.BorderColor = Color.FromArgb(200, 200, 200);
            btnGoogle.FlatAppearance.BorderSize = 1;
            btnGoogle.Click += BtnGoogle_Click;

            // Exit button
            var btnExit = new Button
            {
                Text = "Sair",
                Location = new Point(50, 200),
                Size = new Size(280, 30),
                BackColor = Color.FromArgb(80, 80, 80),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 9F),
                Cursor = Cursors.Hand
            };
            btnExit.FlatAppearance.BorderSize = 0;
            btnExit.Click += BtnExit_Click;

            // Add controls
            this.Controls.AddRange(new Control[] {
                titleLabel, subtitleLabel,
                lblStatus, btnGoogle, btnExit
            });
        }

        private async void BtnGoogle_Click(object sender, EventArgs e)
        {
            btnGoogle.Enabled = false;
            lblStatus.Text = "Abrindo login Google...";
            lblStatus.ForeColor = Color.Yellow;

            try
            {
                var oauth = new GoogleOAuthManager();
                var (email, displayName) = await oauth.LoginAsync();

                lblStatus.Text = "Verificando permissões...";

                var (success, message) = await SupabaseManager.Instance.LoginWithGoogleAsync(email, displayName);

                if (success)
                {
                    lblStatus.Text = message;
                    lblStatus.ForeColor = Color.LimeGreen;

                    // Salvar sessão
                    Properties.Settings.Default.GoogleEmail = email;
                    Properties.Settings.Default.Save();

                    LoginSuccessful = true;
                    this.DialogResult = DialogResult.OK;
                    this.Close();
                }
                else
                {
                    lblStatus.Text = message;
                    lblStatus.ForeColor = Color.Red;
                    btnGoogle.Enabled = true;
                }
            }
            catch (OperationCanceledException)
            {
                lblStatus.Text = "Login cancelado.";
                lblStatus.ForeColor = Color.Orange;
                btnGoogle.Enabled = true;
            }
            catch (TimeoutException)
            {
                lblStatus.Text = "Tempo limite excedido.";
                lblStatus.ForeColor = Color.Red;
                btnGoogle.Enabled = true;
            }
            catch (Exception ex)
            {
                lblStatus.Text = $"Erro: {ex.Message}";
                lblStatus.ForeColor = Color.Red;
                btnGoogle.Enabled = true;
            }
        }

        private void BtnExit_Click(object sender, EventArgs e)
        {
            LoginSuccessful = false;
            this.DialogResult = DialogResult.Cancel;
            this.Close();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (this.DialogResult != DialogResult.OK && this.DialogResult != DialogResult.Cancel)
            {
                LoginSuccessful = false;
                this.DialogResult = DialogResult.Cancel;
            }
            base.OnFormClosing(e);
        }

        private void InitializeComponent()
        {
            this.SuspendLayout();
            this.ClientSize = new System.Drawing.Size(384, 242);
            this.Name = "LoginForm";
            this.ResumeLayout(false);
        }
    }
}

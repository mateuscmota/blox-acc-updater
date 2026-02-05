using System;
using System.Drawing;
using System.Net;
using System.Threading.Tasks;
using System.Windows.Forms;
using RBX_Alt_Manager.Classes;

namespace RBX_Alt_Manager.Controls
{
    public class FriendItemControl : UserControl
    {
        private PictureBox avatarPicture;
        private Label displayNameLabel;
        private Label usernameLabel;
        private Label gameLabel;
        private Button followButton;

        public long UserId { get; set; }
        public long PlaceId { get; set; }
        public string JobId { get; set; }
        public Account Account { get; set; }

        public event EventHandler FollowClicked;

        public FriendItemControl()
        {
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            this.avatarPicture = new PictureBox();
            this.displayNameLabel = new Label();
            this.usernameLabel = new Label();
            this.gameLabel = new Label();
            this.followButton = new Button();
            this.SuspendLayout();

            // avatarPicture
            this.avatarPicture.Location = new Point(5, 5);
            this.avatarPicture.Size = new Size(50, 50);
            this.avatarPicture.SizeMode = PictureBoxSizeMode.StretchImage;
            this.avatarPicture.BorderStyle = BorderStyle.FixedSingle;

            // displayNameLabel
            this.displayNameLabel.Location = new Point(62, 5);
            this.displayNameLabel.Size = new Size(150, 18);
            this.displayNameLabel.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
            this.displayNameLabel.Text = "NomeExibicao";

            // usernameLabel
            this.usernameLabel.Location = new Point(62, 23);
            this.usernameLabel.Size = new Size(150, 15);
            this.usernameLabel.Font = new Font("Segoe UI", 8F);
            this.usernameLabel.ForeColor = Color.Gray;
            this.usernameLabel.Text = "@username";

            // gameLabel
            this.gameLabel.Location = new Point(62, 38);
            this.gameLabel.Size = new Size(200, 15);
            this.gameLabel.Font = new Font("Segoe UI", 8F);
            this.gameLabel.ForeColor = Color.DarkGreen;
            this.gameLabel.Text = "Jogo";

            // followButton
            this.followButton.Location = new Point(270, 15);
            this.followButton.Size = new Size(70, 28);
            this.followButton.Text = "SEGUIR";
            this.followButton.Font = new Font("Segoe UI", 8F, FontStyle.Bold);
            this.followButton.UseVisualStyleBackColor = true;
            this.followButton.Click += FollowButton_Click;

            // FriendItemControl
            this.Controls.Add(this.avatarPicture);
            this.Controls.Add(this.displayNameLabel);
            this.Controls.Add(this.usernameLabel);
            this.Controls.Add(this.gameLabel);
            this.Controls.Add(this.followButton);
            this.Size = new Size(350, 60);
            this.BorderStyle = BorderStyle.FixedSingle;
            this.ResumeLayout(false);
        }

        public void SetFriendInfo(long userId, string displayName, string username, string gameName, long placeId, string jobId, bool canFollow)
        {
            UserId = userId;
            PlaceId = placeId;
            JobId = jobId;

            displayNameLabel.Text = displayName ?? "Sem nome";
            usernameLabel.Text = $"@{username}";
            
            if (!string.IsNullOrEmpty(gameName))
            {
                gameLabel.Text = gameName;
                gameLabel.ForeColor = Color.DarkGreen;
                followButton.Enabled = canFollow;
            }
            else
            {
                gameLabel.Text = "Offline";
                gameLabel.ForeColor = Color.Gray;
                followButton.Enabled = false;
            }

            // Carregar avatar em background
            LoadAvatarAsync(userId);
        }

        private async void LoadAvatarAsync(long userId)
        {
            try
            {
                var url = $"https://thumbnails.roblox.com/v1/users/avatar-headshot?userIds={userId}&size=50x50&format=Png";
                
                using (var client = new WebClient())
                {
                    var json = await client.DownloadStringTaskAsync(url);
                    
                    // Parse JSON para obter URL da imagem
                    if (json.Contains("imageUrl"))
                    {
                        var startIndex = json.IndexOf("\"imageUrl\":\"") + 12;
                        var endIndex = json.IndexOf("\"", startIndex);
                        var imageUrl = json.Substring(startIndex, endIndex - startIndex);

                        if (!string.IsNullOrEmpty(imageUrl))
                        {
                            var imageData = await client.DownloadDataTaskAsync(imageUrl);
                            using (var ms = new System.IO.MemoryStream(imageData))
                            {
                                if (!this.IsDisposed && avatarPicture != null && !avatarPicture.IsDisposed)
                                {
                                    this.Invoke((MethodInvoker)delegate
                                    {
                                        avatarPicture.Image = Image.FromStream(ms);
                                    });
                                }
                            }
                        }
                    }
                }
            }
            catch { }
        }

        private void FollowButton_Click(object sender, EventArgs e)
        {
            FollowClicked?.Invoke(this, EventArgs.Empty);
        }
    }
}

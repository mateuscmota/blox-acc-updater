using RestSharp;
using System;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace RBX_Alt_Manager.Classes
{
    public partial class GameControl : UserControl
    {
        public Game Game;
        public FavoriteGame Favorite;
        public event EventHandler<GameArgs> Selected;
        public event EventHandler<GameArgs> DeleteRequested;
        public event EventHandler<EventArgs> Exited;

        private Button _deleteButton;

        public GameControl(Game game)
        {
            InitializeComponent();
            GameName.Rescale();

            Game = game;

            if (!string.IsNullOrEmpty(Game.Details?.name)) GameName.Text = Game.Details?.name;

            // Criar botão de deletar (X)
            CreateDeleteButton();

            ParentChanged += (s, e) => { if (Parent == null) Dispose(); };

            Task.Run(async () =>
            {
                await Game.WaitForDetails();

                if (Disposing) return;

                this.InvokeIfRequired(() => GameName.Text = Game.Details.name);
                
                GameImage.LoadAsync(Game.ImageUrl);
            });
        }

        /// <summary>
        /// Cria o botão X para deletar o jogo dos recentes
        /// </summary>
        private void CreateDeleteButton()
        {
            _deleteButton = new Button
            {
                Text = "✕",
                Font = new Font("Segoe UI", 8F, FontStyle.Bold),
                ForeColor = Color.White,
                BackColor = Color.FromArgb(180, 60, 60, 60),
                FlatStyle = FlatStyle.Flat,
                Size = new Size(20, 20),
                Location = new Point(GameImage.Right - 22, GameImage.Top + 2),
                Cursor = Cursors.Hand,
                Visible = false
            };
            _deleteButton.FlatAppearance.BorderSize = 0;
            _deleteButton.FlatAppearance.MouseOverBackColor = Color.FromArgb(200, 200, 50, 50);
            _deleteButton.Click += DeleteButton_Click;
            
            // Mostrar/esconder botão ao passar o mouse
            this.MouseEnter += (s, e) => _deleteButton.Visible = true;
            this.MouseLeave += (s, e) => { if (!ClientRectangle.Contains(PointToClient(Cursor.Position))) _deleteButton.Visible = false; };
            GameImage.MouseEnter += (s, e) => _deleteButton.Visible = true;
            GameImage.MouseLeave += (s, e) => { if (!ClientRectangle.Contains(PointToClient(Cursor.Position))) _deleteButton.Visible = false; };
            GameName.MouseEnter += (s, e) => _deleteButton.Visible = true;
            GameName.MouseLeave += (s, e) => { if (!ClientRectangle.Contains(PointToClient(Cursor.Position))) _deleteButton.Visible = false; };
            _deleteButton.MouseEnter += (s, e) => _deleteButton.Visible = true;
            _deleteButton.MouseLeave += (s, e) => { if (!ClientRectangle.Contains(PointToClient(Cursor.Position))) _deleteButton.Visible = false; };

            this.Controls.Add(_deleteButton);
            _deleteButton.BringToFront();
        }

        private void DeleteButton_Click(object sender, EventArgs e)
        {
            DeleteRequested?.Invoke(this, new GameArgs(Game));
        }

        public void Rename(string NewName) => GameName.Text = NewName;

        public void SetContext(ContextMenuStrip CMS)
        {
            GameName.ContextMenuStrip = CMS;
            GameImage.ContextMenuStrip = CMS;
        }

        private void MouseClicked(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
                Selected?.Invoke(this, new GameArgs(Game));
        }

        private void Exit(Action action)
        {
            Exited?.Invoke(this, EventArgs.Empty);

            action.Invoke();
        }

        private void copyPlaceIdToolStripMenuItem_Click(object sender, EventArgs e) =>
            Exit(() => Clipboard.SetText(Game.Details.placeId.ToString()));

        private void copyNameToolStripMenuItem_Click(object sender, EventArgs e) =>
            Exit(() => Clipboard.SetText(Game.Details.name));

        private void copyPlaceLinkToolStripMenuItem_Click(object sender, EventArgs e) =>
            Exit(() => Clipboard.SetText($"https://www.roblox.com/games/{Game.Details.placeId}/-"));

        private void copyPlaceDetailsToolStripMenuItem_Click(object sender, EventArgs e) => Exit(() =>
        {
            if (AccountManager.LastValidAccount == null)
            {
                MessageBox.Show("Select a valid account then try again", "Roblox Account Manager", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            RestRequest DetailsRequest = new RestRequest($"v1/games/multiget-place-details?placeIds={Game.Details.placeId}");
            DetailsRequest.AddCookie(".ROBLOSECURITY", AccountManager.LastValidAccount?.SecurityToken, "/", ".roblox.com");

            Clipboard.SetText(ServerList.GamesClient.Execute(DetailsRequest).Content);
        });
    }
}
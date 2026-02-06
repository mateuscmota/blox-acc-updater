using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RestSharp;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using System.Windows.Forms;
using RBX_Alt_Manager.Classes;

namespace RBX_Alt_Manager.Controls
{
    public class FriendsPanelControl : UserControl
    {
        private static readonly RestClient FriendsApi = new RestClient("https://friends.roblox.com");
        private static readonly RestClient PresenceApi = new RestClient("https://presence.roblox.com");
        private static readonly RestClient UsersApi = new RestClient("https://users.roblox.com");

        private FlowLayoutPanel friendsFlowPanel;
        private GroupBox addFriendGroupBox;
        private TextBox addFriendTextBox;
        private Button addFriendButton;
        private Button refreshButton;
        private Label statusLabel;

        private Account _account;
        private List<FriendData> _friends = new List<FriendData>();

        public FriendsPanelControl()
        {
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            this.friendsFlowPanel = new FlowLayoutPanel();
            this.addFriendGroupBox = new GroupBox();
            this.addFriendTextBox = new TextBox();
            this.addFriendButton = new Button();
            this.refreshButton = new Button();
            this.statusLabel = new Label();
            this.SuspendLayout();

            // friendsFlowPanel - Lista de amigos com scroll
            this.friendsFlowPanel.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            this.friendsFlowPanel.AutoScroll = true;
            this.friendsFlowPanel.FlowDirection = FlowDirection.TopDown;
            this.friendsFlowPanel.WrapContents = false;
            this.friendsFlowPanel.Location = new Point(5, 5);
            this.friendsFlowPanel.Size = new Size(380, 200);
            this.friendsFlowPanel.BorderStyle = BorderStyle.FixedSingle;

            // addFriendGroupBox
            this.addFriendGroupBox.Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            this.addFriendGroupBox.Location = new Point(5, 210);
            this.addFriendGroupBox.Size = new Size(380, 55);
            this.addFriendGroupBox.Text = "Adicionar Amigo";

            // addFriendTextBox
            this.addFriendTextBox.Location = new Point(10, 20);
            this.addFriendTextBox.Size = new Size(150, 20);

            // addFriendButton
            this.addFriendButton.Location = new Point(165, 18);
            this.addFriendButton.Size = new Size(100, 25);
            this.addFriendButton.Text = "Adicionar Amigo";
            this.addFriendButton.Click += AddFriendButton_Click;

            // refreshButton
            this.refreshButton.Location = new Point(275, 18);
            this.refreshButton.Size = new Size(95, 25);
            this.refreshButton.Text = "Atualizar Lista";
            this.refreshButton.Click += RefreshButton_Click;

            // statusLabel
            this.statusLabel.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            this.statusLabel.Location = new Point(5, 270);
            this.statusLabel.Size = new Size(380, 15);
            this.statusLabel.Text = "";

            // Add controls to groupbox
            this.addFriendGroupBox.Controls.Add(this.addFriendTextBox);
            this.addFriendGroupBox.Controls.Add(this.addFriendButton);
            this.addFriendGroupBox.Controls.Add(this.refreshButton);

            // FriendsPanelControl
            this.Controls.Add(this.friendsFlowPanel);
            this.Controls.Add(this.addFriendGroupBox);
            this.Controls.Add(this.statusLabel);
            this.Size = new Size(390, 290);
            this.ResumeLayout(false);
        }

        public void SetAccount(Account account)
        {
            _account = account;
            if (_account != null)
            {
                _ = LoadFriendsAsync();
            }
        }

        private async void RefreshButton_Click(object sender, EventArgs e)
        {
            if (_account == null) return;
            await LoadFriendsAsync();
        }

        private async Task LoadFriendsAsync()
        {
            if (_account == null) return;

            statusLabel.Text = "Carregando amigos...";
            refreshButton.Enabled = false;

            try
            {
                // Limpar lista atual
                friendsFlowPanel.Controls.Clear();
                _friends.Clear();

                // 1. Buscar lista de amigos
                var friendsClient = FriendsApi;
                var friendsRequest = new RestRequest($"/v1/users/{_account.UserID}/friends", Method.Get);
                friendsRequest.AddHeader("Cookie", $".ROBLOSECURITY={_account.SecurityToken}");

                var friendsResponse = await friendsClient.ExecuteAsync(friendsRequest);

                if (!friendsResponse.IsSuccessful)
                {
                    statusLabel.Text = "Erro ao carregar amigos";
                    refreshButton.Enabled = true;
                    return;
                }

                var friendsData = JsonConvert.DeserializeObject<JObject>(friendsResponse.Content);
                var friendsList = friendsData?["data"]?.ToObject<List<JObject>>();

                if (friendsList == null || friendsList.Count == 0)
                {
                    statusLabel.Text = "Nenhum amigo encontrado";
                    refreshButton.Enabled = true;
                    return;
                }

                // Extrair IDs dos amigos
                var friendIds = friendsList.Select(f => f["id"]?.Value<long>() ?? 0).Where(id => id > 0).ToList();

                // 2. Buscar presença de todos os amigos
                var presenceClient = PresenceApi;
                var presenceRequest = new RestRequest("/v1/presence/users", Method.Post);
                presenceRequest.AddHeader("Cookie", $".ROBLOSECURITY={_account.SecurityToken}");
                presenceRequest.AddHeader("Content-Type", "application/json");
                presenceRequest.AddJsonBody(new { userIds = friendIds });

                var presenceResponse = await presenceClient.ExecuteAsync(presenceRequest);
                var presenceData = presenceResponse.IsSuccessful 
                    ? JsonConvert.DeserializeObject<JObject>(presenceResponse.Content)?["userPresences"]?.ToObject<List<JObject>>()
                    : null;

                // 3. Buscar usernames (API de friends pode retornar vazio)
                var usersClient = UsersApi;
                var usersRequest = new RestRequest("/v1/users", Method.Post);
                usersRequest.AddHeader("Content-Type", "application/json");
                usersRequest.AddJsonBody(new { userIds = friendIds, excludeBannedUsers = false });

                var usersResponse = await usersClient.ExecuteAsync(usersRequest);
                var usersData = usersResponse.IsSuccessful
                    ? JsonConvert.DeserializeObject<JObject>(usersResponse.Content)?["data"]?.ToObject<List<JObject>>()
                    : null;

                // Criar dicionário de nomes
                var userNames = new Dictionary<long, (string name, string displayName)>();
                if (usersData != null)
                {
                    foreach (var user in usersData)
                    {
                        var id = user["id"]?.Value<long>() ?? 0;
                        var name = user["name"]?.ToString() ?? "";
                        var displayName = user["displayName"]?.ToString() ?? "";
                        if (id > 0)
                            userNames[id] = (name, displayName);
                    }
                }

                // Criar dicionário de presença
                var presences = new Dictionary<long, (int type, string lastLocation, long placeId, string gameId, long rootPlaceId)>();
                if (presenceData != null)
                {
                    foreach (var p in presenceData)
                    {
                        var id = p["userId"]?.Value<long>() ?? 0;
                        var type = p["userPresenceType"]?.Value<int>() ?? 0;
                        var location = p["lastLocation"]?.ToString() ?? "";
                        var placeId = p["placeId"]?.Value<long>() ?? 0;
                        var gameId = p["gameId"]?.ToString() ?? "";
                        var rootPlaceId = p["rootPlaceId"]?.Value<long>() ?? 0;
                        if (id > 0)
                            presences[id] = (type, location, placeId, gameId, rootPlaceId);
                    }
                }

                // Construir lista de amigos
                foreach (var friend in friendsList)
                {
                    var userId = friend["id"]?.Value<long>() ?? 0;
                    if (userId == 0) continue;

                    var friendData = new FriendData
                    {
                        UserId = userId,
                        Username = userNames.ContainsKey(userId) ? userNames[userId].name : friend["name"]?.ToString() ?? "",
                        DisplayName = userNames.ContainsKey(userId) ? userNames[userId].displayName : friend["displayName"]?.ToString() ?? "",
                        PresenceType = presences.ContainsKey(userId) ? presences[userId].type : 0,
                        GameName = presences.ContainsKey(userId) ? presences[userId].lastLocation : "",
                        PlaceId = presences.ContainsKey(userId) ? presences[userId].placeId : 0,
                        JobId = presences.ContainsKey(userId) ? presences[userId].gameId : ""
                    };

                    _friends.Add(friendData);
                }

                // Ordenar: Em jogo primeiro, depois online, depois offline
                _friends = _friends.OrderByDescending(f => f.PresenceType == 2) // Em jogo primeiro
                                   .ThenByDescending(f => f.PresenceType == 1) // Online
                                   .ThenBy(f => f.DisplayName)
                                   .ToList();

                // Criar controles para amigos em jogo
                int inGameCount = 0;
                foreach (var friend in _friends.Where(f => f.PresenceType == 2))
                {
                    var item = new FriendItemControl();
                    item.Account = _account;
                    item.SetFriendInfo(
                        friend.UserId,
                        friend.DisplayName,
                        friend.Username,
                        friend.GameName,
                        friend.PlaceId,
                        friend.JobId,
                        true
                    );
                    item.FollowClicked += FriendItem_FollowClicked;
                    friendsFlowPanel.Controls.Add(item);
                    inGameCount++;
                }

                statusLabel.Text = $"{inGameCount} amigo(s) em jogo de {_friends.Count} total";
            }
            catch (Exception ex)
            {
                statusLabel.Text = $"Erro: {ex.Message}";
                AccountManager.AddLogError($"❌ [FriendsPanel] Erro: {ex.Message}");
            }

            refreshButton.Enabled = true;
        }

        private void FriendItem_FollowClicked(object sender, EventArgs e)
        {
            var item = sender as FriendItemControl;
            if (item == null || _account == null) return;

            if (item.PlaceId > 0 && !string.IsNullOrEmpty(item.JobId))
            {
                // Seguir amigo
                AccountManager.AddLog($"🎮 [FriendsPanel] Seguindo amigo no jogo PlaceId={item.PlaceId}, JobId={item.JobId}");
                
                // Usar o método de join
                _account.JoinServer(item.PlaceId, item.JobId);
                
                statusLabel.ForeColor = Color.Green;
                statusLabel.Text = "Entrando no jogo...";
            }
        }

        private async void AddFriendButton_Click(object sender, EventArgs e)
        {
            var username = addFriendTextBox.Text.Trim();
            if (string.IsNullOrEmpty(username) || _account == null) return;

            addFriendButton.Enabled = false;
            statusLabel.Text = "Buscando usuário...";

            try
            {
                // Buscar ID do usuário
                var client = UsersApi;
                var request = new RestRequest("/v1/usernames/users", Method.Post);
                request.AddHeader("Content-Type", "application/json");
                request.AddJsonBody(new { usernames = new[] { username }, excludeBannedUsers = false });

                var response = await client.ExecuteAsync(request);

                if (response.IsSuccessful && !string.IsNullOrEmpty(response.Content))
                {
                    var data = JsonConvert.DeserializeObject<JObject>(response.Content);
                    var users = data?["data"]?.ToObject<List<JObject>>();

                    if (users != null && users.Count > 0)
                    {
                        var userId = users[0]["id"]?.Value<long>() ?? 0;
                        if (userId > 0)
                        {
                            // Abrir perfil no navegador para adicionar amigo
                            var profileUrl = $"https://www.roblox.com/users/{userId}/profile";

                            new AccountBrowser(_account, profileUrl, null, async (page) =>
                            {
                                try
                                {
                                    await Task.Delay(2000);

                                    var clicked = await page.EvaluateExpressionAsync<bool>(@"
                                        (function() {
                                            var buttons = document.querySelectorAll('button');
                                            for (var i = 0; i < buttons.length; i++) {
                                                var btn = buttons[i];
                                                var text = btn.innerText || btn.textContent || '';
                                                if (text.includes('Add Connection') || text.includes('Add Friend') || text.includes('Adicionar')) {
                                                    btn.click();
                                                    return true;
                                                }
                                            }
                                            return false;
                                        })()
                                    ");

                                    if (clicked)
                                    {
                                        await Task.Delay(1500);
                                    }

                                    await page.Browser.CloseAsync();
                                }
                                catch { }
                            });

                            statusLabel.ForeColor = Color.Green;
                            statusLabel.Text = $"Adicionando {username}...";
                            addFriendTextBox.Clear();
                        }
                    }
                    else
                    {
                        statusLabel.ForeColor = Color.Red;
                        statusLabel.Text = "Usuário não encontrado";
                    }
                }
            }
            catch (Exception ex)
            {
                statusLabel.ForeColor = Color.Red;
                statusLabel.Text = $"Erro: {ex.Message}";
            }

            addFriendButton.Enabled = true;
        }

        private class FriendData
        {
            public long UserId { get; set; }
            public string Username { get; set; }
            public string DisplayName { get; set; }
            public int PresenceType { get; set; }
            public string GameName { get; set; }
            public long PlaceId { get; set; }
            public string JobId { get; set; }
        }
    }
}

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RBX_Alt_Manager.Classes;
using RestSharp;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace RBX_Alt_Manager.Forms
{
    public partial class FollowFriendForm : Form
    {
        private Account _account;
        private List<FriendInfo> _friends = new List<FriendInfo>();
        private List<FriendInfo> _filteredFriends = new List<FriendInfo>();

        public FollowFriendForm(Account account)
        {
            InitializeComponent();
            _account = account;
            this.Text = $"Seguir Amigo - {account.Username}";
        }

        private void InitializeComponent()
        {
            this.friendsListView = new ListView();
            this.refreshButton = new Button();
            this.followButton = new Button();
            this.searchTextBox = new TextBox();
            this.searchLabel = new Label();
            this.statusLabel = new Label();
            this.filterComboBox = new ComboBox();
            this.addFriendGroupBox = new GroupBox();
            this.addFriendTextBox = new TextBox();
            this.addFriendButton = new Button();
            this.addFriendStatusLabel = new Label();
            this.SuspendLayout();

            // friendsListView
            this.friendsListView.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            this.friendsListView.FullRowSelect = true;
            this.friendsListView.GridLines = true;
            this.friendsListView.HideSelection = false;
            this.friendsListView.Location = new Point(12, 70);
            this.friendsListView.Name = "friendsListView";
            this.friendsListView.Size = new Size(560, 260);
            this.friendsListView.TabIndex = 0;
            this.friendsListView.UseCompatibleStateImageBehavior = false;
            this.friendsListView.View = View.Details;
            this.friendsListView.Columns.Add("Nome", 150);
            this.friendsListView.Columns.Add("Display Name", 120);
            this.friendsListView.Columns.Add("Status", 80);
            this.friendsListView.Columns.Add("Jogo", 200);
            this.friendsListView.DoubleClick += FriendsListView_DoubleClick;

            // searchLabel
            this.searchLabel.AutoSize = true;
            this.searchLabel.Location = new Point(12, 15);
            this.searchLabel.Name = "searchLabel";
            this.searchLabel.Size = new Size(56, 13);
            this.searchLabel.Text = "Buscar:";

            // searchTextBox
            this.searchTextBox.Location = new Point(70, 12);
            this.searchTextBox.Name = "searchTextBox";
            this.searchTextBox.Size = new Size(180, 20);
            this.searchTextBox.TabIndex = 1;
            this.searchTextBox.TextChanged += SearchTextBox_TextChanged;

            // filterComboBox
            this.filterComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
            this.filterComboBox.Location = new Point(260, 12);
            this.filterComboBox.Name = "filterComboBox";
            this.filterComboBox.Size = new Size(120, 21);
            this.filterComboBox.TabIndex = 2;
            this.filterComboBox.Items.AddRange(new object[] { "Todos", "Em Jogo", "Online", "Offline" });
            this.filterComboBox.SelectedIndex = 1; // Default: Em Jogo
            this.filterComboBox.SelectedIndexChanged += FilterComboBox_SelectedIndexChanged;

            // refreshButton
            this.refreshButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            this.refreshButton.Location = new Point(390, 10);
            this.refreshButton.Name = "refreshButton";
            this.refreshButton.Size = new Size(85, 25);
            this.refreshButton.TabIndex = 3;
            this.refreshButton.Text = "Atualizar";
            this.refreshButton.UseVisualStyleBackColor = true;
            this.refreshButton.Click += RefreshButton_Click;

            // followButton
            this.followButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            this.followButton.Location = new Point(485, 10);
            this.followButton.Name = "followButton";
            this.followButton.Size = new Size(85, 25);
            this.followButton.TabIndex = 4;
            this.followButton.Text = "Seguir";
            this.followButton.UseVisualStyleBackColor = true;
            this.followButton.Click += FollowButton_Click;

            // statusLabel
            this.statusLabel.AutoSize = true;
            this.statusLabel.Location = new Point(12, 45);
            this.statusLabel.Name = "statusLabel";
            this.statusLabel.Size = new Size(200, 13);
            this.statusLabel.Text = "Clique em Atualizar para carregar amigos";

            // addFriendGroupBox
            this.addFriendGroupBox.Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            this.addFriendGroupBox.Location = new Point(12, 340);
            this.addFriendGroupBox.Name = "addFriendGroupBox";
            this.addFriendGroupBox.Size = new Size(560, 55);
            this.addFriendGroupBox.TabIndex = 5;
            this.addFriendGroupBox.TabStop = false;
            this.addFriendGroupBox.Text = "Adicionar Amigo";

            // addFriendTextBox
            this.addFriendTextBox.Location = new Point(10, 22);
            this.addFriendTextBox.Name = "addFriendTextBox";
            this.addFriendTextBox.Size = new Size(200, 20);
            this.addFriendTextBox.TabIndex = 0;
            this.addFriendTextBox.ForeColor = Color.Gray;
            this.addFriendTextBox.Text = "Username do jogador";
            this.addFriendTextBox.GotFocus += AddFriendTextBox_GotFocus;
            this.addFriendTextBox.LostFocus += AddFriendTextBox_LostFocus;
            this.addFriendTextBox.KeyPress += AddFriendTextBox_KeyPress;

            // addFriendButton
            this.addFriendButton.Location = new Point(220, 20);
            this.addFriendButton.Name = "addFriendButton";
            this.addFriendButton.Size = new Size(100, 25);
            this.addFriendButton.TabIndex = 1;
            this.addFriendButton.Text = "Abrir Perfil";
            this.addFriendButton.UseVisualStyleBackColor = true;
            this.addFriendButton.Click += AddFriendButton_Click;

            // addFriendStatusLabel
            this.addFriendStatusLabel.AutoSize = true;
            this.addFriendStatusLabel.Location = new Point(350, 25);
            this.addFriendStatusLabel.Name = "addFriendStatusLabel";
            this.addFriendStatusLabel.Size = new Size(0, 13);
            this.addFriendStatusLabel.Text = "";

            // Add controls to groupbox
            this.addFriendGroupBox.Controls.Add(this.addFriendTextBox);
            this.addFriendGroupBox.Controls.Add(this.addFriendButton);
            this.addFriendGroupBox.Controls.Add(this.addFriendStatusLabel);

            // FollowFriendForm
            this.AutoScaleDimensions = new SizeF(6F, 13F);
            this.AutoScaleMode = AutoScaleMode.Font;
            this.ClientSize = new Size(584, 411);
            this.Controls.Add(this.addFriendGroupBox);
            this.Controls.Add(this.statusLabel);
            this.Controls.Add(this.followButton);
            this.Controls.Add(this.refreshButton);
            this.Controls.Add(this.filterComboBox);
            this.Controls.Add(this.searchTextBox);
            this.Controls.Add(this.searchLabel);
            this.Controls.Add(this.friendsListView);
            this.MinimumSize = new Size(500, 400);
            this.Name = "FollowFriendForm";
            this.StartPosition = FormStartPosition.CenterParent;
            this.Text = "Seguir Amigo";
            this.Load += FollowFriendForm_Load;
            this.ResumeLayout(false);
            this.PerformLayout();
        }

        private ListView friendsListView;
        private Button refreshButton;
        private Button followButton;
        private TextBox searchTextBox;
        private Label searchLabel;
        private Label statusLabel;
        private ComboBox filterComboBox;
        private GroupBox addFriendGroupBox;
        private TextBox addFriendTextBox;
        private Button addFriendButton;
        private Label addFriendStatusLabel;

        private async void FollowFriendForm_Load(object sender, EventArgs e)
        {
            await LoadFriends();
        }

        private async void RefreshButton_Click(object sender, EventArgs e)
        {
            await LoadFriends();
        }

        private async Task LoadFriends()
        {
            refreshButton.Enabled = false;
            statusLabel.Text = "Carregando lista de amigos...";
            friendsListView.Items.Clear();
            _friends.Clear();

            try
            {
                AccountManager.AddLog("🔍 [FollowFriend] Iniciando carregamento de amigos...");
                
                // Obter lista de amigos
                var friendsRequest = new RestRequest($"/v1/users/{_account.UserID}/friends", Method.Get);
                friendsRequest.AddHeader("Cookie", $".ROBLOSECURITY={_account.SecurityToken}");
                
                var friendsResponse = await AccountManager.FriendsClient.ExecuteAsync(friendsRequest);

                AccountManager.AddLog($"🔍 [FollowFriend] Resposta API amigos: Status={friendsResponse.StatusCode}, Success={friendsResponse.IsSuccessful}");

                if (!friendsResponse.IsSuccessful)
                {
                    AccountManager.AddLogError($"❌ [FollowFriend] Erro ao carregar amigos: {friendsResponse.StatusCode} - {friendsResponse.ErrorMessage}");
                    statusLabel.Text = "Erro ao carregar amigos. Verifique se a conta está válida.";
                    refreshButton.Enabled = true;
                    return;
                }

                var friendsData = JsonConvert.DeserializeObject<JObject>(friendsResponse.Content);
                var friendsList = friendsData["data"]?.ToObject<List<JObject>>() ?? new List<JObject>();

                AccountManager.AddLog($"🔍 [FollowFriend] {friendsList.Count} amigos encontrados na API");

                if (friendsList.Count == 0)
                {
                    statusLabel.Text = "Nenhum amigo encontrado.";
                    refreshButton.Enabled = true;
                    return;
                }

                statusLabel.Text = $"Carregando presença de {friendsList.Count} amigos...";

                // Obter IDs dos amigos
                var friendIds = friendsList.Select(f => f["id"].Value<long>()).ToArray();
                AccountManager.AddLog($"🔍 [FollowFriend] IDs dos amigos: {string.Join(", ", friendIds.Take(10))}...");

                // Obter presença de todos os amigos usando API autenticada
                var presences = await GetPresenceAuthenticated(friendIds);
                AccountManager.AddLog($"🔍 [FollowFriend] Presenças obtidas: {presences?.Count ?? 0}");

                foreach (var friend in friendsList)
                {
                    var friendId = friend["id"]?.Value<long>() ?? 0;
                    
                    // Log do primeiro amigo para debug - mostrar JSON completo
                    if (_friends.Count == 0)
                    {
                        AccountManager.AddLog($"🔍 [FollowFriend] JSON do primeiro amigo: {friend.ToString(Newtonsoft.Json.Formatting.None)}");
                    }
                    
                    // A API retorna "name" para username
                    var username = friend["name"]?.ToString() ?? "";
                    var displayName = friend["displayName"]?.ToString() ?? "";
                    
                    var presence = presences != null && presences.ContainsKey(friendId) ? presences[friendId] : null;
                    
                    var friendInfo = new FriendInfo
                    {
                        UserId = friendId,
                        Username = username,
                        DisplayName = displayName,
                        Presence = presence
                    };

                    _friends.Add(friendInfo);
                }

                // Se os nomes vieram vazios, buscar via API de usuários
                var friendsWithEmptyNames = _friends.Where(f => string.IsNullOrWhiteSpace(f.Username)).ToList();
                if (friendsWithEmptyNames.Count > 0)
                {
                    AccountManager.AddLog($"🔍 [FollowFriend] {friendsWithEmptyNames.Count} amigos com nome vazio, buscando via API...");
                    statusLabel.Text = "Buscando nomes dos amigos...";
                    var userIds = friendsWithEmptyNames.Select(f => f.UserId).ToArray();
                    var userNames = await GetUserNames(userIds);
                    
                    foreach (var friend in friendsWithEmptyNames)
                    {
                        if (userNames.ContainsKey(friend.UserId))
                        {
                            var userData = userNames[friend.UserId];
                            friend.Username = userData.Item1;
                            friend.DisplayName = userData.Item2;
                        }
                        else
                        {
                            friend.Username = $"User_{friend.UserId}";
                            friend.DisplayName = friend.Username;
                        }
                    }
                }

                // Obter nomes dos jogos em paralelo para os que estão em jogo
                // Usar rootPlaceId que é o PlaceId correto do jogo
                var inGameFriends = _friends.Where(f => f.Presence?.userPresenceType == UserPresenceType.InGame).ToList();
                
                // Atribuir o nome do jogo diretamente do lastLocation (que já contém o nome)
                foreach (var friend in inGameFriends)
                {
                    if (friend.Presence != null && !string.IsNullOrEmpty(friend.Presence.lastLocation))
                    {
                        friend.GameName = friend.Presence.lastLocation;
                    }
                    else
                    {
                        friend.GameName = "Jogo Desconhecido";
                    }
                }
                
                AccountManager.AddLog($"🎮 [FollowFriend] {inGameFriends.Count} amigos em jogo");

                ApplyFilter();
                
                var inGameCount = _friends.Count(f => f.Presence?.userPresenceType == UserPresenceType.InGame);
                var onlineCount = _friends.Count(f => f.Presence?.userPresenceType == UserPresenceType.Online);
                
                AccountManager.AddLogSuccess($"✅ [FollowFriend] Carregamento completo: {_friends.Count} amigos, {inGameCount} em jogo, {onlineCount} online");
                statusLabel.Text = $"{_friends.Count} amigos. {inGameCount} em jogo, {onlineCount} online.";
            }
            catch (Exception ex)
            {
                AccountManager.AddLogError($"❌ [FollowFriend] Exceção: {ex.Message}\n{ex.StackTrace}");
                statusLabel.Text = $"Erro: {ex.Message}";
            }

            refreshButton.Enabled = true;
        }

        private async Task<Dictionary<long, UserPresence>> GetPresenceAuthenticated(long[] userIds)
        {
            var dict = new Dictionary<long, UserPresence>();
            
            try
            {
                AccountManager.AddLog($"🔍 [FollowFriend] Obtendo presença para {userIds.Length} usuários...");
                
                var client = new RestClient("https://presence.roblox.com");
                var request = new RestRequest("/v1/presence/users", Method.Post);
                request.AddHeader("Cookie", $".ROBLOSECURITY={_account.SecurityToken}");
                request.AddHeader("Content-Type", "application/json");
                request.AddJsonBody(new { userIds = userIds });
                
                var response = await client.ExecuteAsync(request);
                
                AccountManager.AddLog($"🔍 [FollowFriend] Resposta Presence API: Status={response.StatusCode}, Success={response.IsSuccessful}, ContentLength={response.Content?.Length ?? 0}");
                
                if (!string.IsNullOrEmpty(response.Content) && response.Content.Length < 500)
                {
                    AccountManager.AddLog($"🔍 [FollowFriend] Conteúdo: {response.Content}");
                }
                
                if (response.IsSuccessful && !string.IsNullOrEmpty(response.Content))
                {
                    var data = JsonConvert.DeserializeObject<JObject>(response.Content);
                    if (data != null && data.ContainsKey("userPresences"))
                    {
                        var presencesList = data["userPresences"].ToObject<List<UserPresence>>();
                        AccountManager.AddLog($"🔍 [FollowFriend] {presencesList?.Count ?? 0} presenças parseadas");
                        
                        if (presencesList != null)
                        {
                            foreach (var presence in presencesList)
                            {
                                dict[presence.userId] = presence;
                            }
                        }
                    }
                    else
                    {
                        AccountManager.AddLogError($"❌ [FollowFriend] Resposta não contém 'userPresences'");
                    }
                }
                else
                {
                    AccountManager.AddLogError($"❌ [FollowFriend] Falha na API de presença: {response.ErrorMessage}");
                }
            }
            catch (Exception ex)
            {
                AccountManager.AddLogError($"❌ [FollowFriend] Exceção em GetPresenceAuthenticated: {ex.Message}");
            }
            
            return dict;
        }

        private async Task<Dictionary<long, Tuple<string, string>>> GetUserNames(long[] userIds)
        {
            var dict = new Dictionary<long, Tuple<string, string>>();
            
            try
            {
                var client = new RestClient("https://users.roblox.com");
                var request = new RestRequest("/v1/users", Method.Post);
                request.AddHeader("Content-Type", "application/json");
                request.AddJsonBody(new { userIds = userIds, excludeBannedUsers = false });
                
                var response = await client.ExecuteAsync(request);
                
                if (response.IsSuccessful && !string.IsNullOrEmpty(response.Content))
                {
                    var data = JsonConvert.DeserializeObject<JObject>(response.Content);
                    if (data != null && data.ContainsKey("data"))
                    {
                        foreach (var user in data["data"])
                        {
                            var userId = user["id"]?.Value<long>() ?? 0;
                            var name = user["name"]?.ToString() ?? "";
                            var displayName = user["displayName"]?.ToString() ?? name;
                            
                            if (userId > 0 && !string.IsNullOrWhiteSpace(name))
                            {
                                dict[userId] = Tuple.Create(name, displayName);
                            }
                        }
                        AccountManager.AddLog($"🔍 [FollowFriend] Obtidos nomes de {dict.Count} usuários");
                    }
                }
                else
                {
                    AccountManager.AddLogError($"❌ [FollowFriend] Falha ao obter nomes: {response.StatusCode}");
                }
            }
            catch (Exception ex)
            {
                AccountManager.AddLogError($"❌ [FollowFriend] Exceção em GetUserNames: {ex.Message}");
            }
            
            return dict;
        }

        private async Task<Dictionary<long, string>> GetGameNames(List<long> placeIds)
        {
            var dict = new Dictionary<long, string>();
            
            try
            {
                var client = new RestClient("https://games.roblox.com");
                var idsString = string.Join(",", placeIds);
                var request = new RestRequest($"/v1/games/multiget-place-details?placeIds={idsString}", Method.Get);
                var response = await client.ExecuteAsync(request);

                AccountManager.AddLog($"🎮 [FollowFriend] Resposta Games API: Status={response.StatusCode}, Success={response.IsSuccessful}");

                if (response.IsSuccessful && !string.IsNullOrEmpty(response.Content))
                {
                    var data = JsonConvert.DeserializeObject<JArray>(response.Content);
                    if (data != null)
                    {
                        foreach (var item in data)
                        {
                            var placeId = item["placeId"]?.Value<long>() ?? 0;
                            var name = item["name"]?.Value<string>() ?? "Desconhecido";
                            if (placeId > 0)
                            {
                                dict[placeId] = name;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                AccountManager.AddLogError($"❌ [FollowFriend] Exceção em GetGameNames: {ex.Message}");
            }
            
            return dict;
        }

        private void ApplyFilter()
        {
            var searchText = searchTextBox.Text.ToLower();
            var filterIndex = filterComboBox.SelectedIndex;

            _filteredFriends = _friends.Where(f =>
            {
                // Filtro de texto
                if (!string.IsNullOrEmpty(searchText))
                {
                    if (!f.Username.ToLower().Contains(searchText) && 
                        !f.DisplayName.ToLower().Contains(searchText))
                        return false;
                }

                // Filtro de status
                switch (filterIndex)
                {
                    case 1: // Em Jogo
                        return f.Presence?.userPresenceType == UserPresenceType.InGame;
                    case 2: // Online
                        return f.Presence?.userPresenceType == UserPresenceType.Online;
                    case 3: // Offline
                        return f.Presence == null || f.Presence.userPresenceType == UserPresenceType.Offline;
                    default: // Todos
                        return true;
                }
            }).OrderByDescending(f => f.Presence?.userPresenceType == UserPresenceType.InGame)
              .ThenByDescending(f => f.Presence?.userPresenceType == UserPresenceType.Online)
              .ThenBy(f => f.Username)
              .ToList();

            RefreshListView();
        }

        private void RefreshListView()
        {
            friendsListView.Items.Clear();

            foreach (var friend in _filteredFriends)
            {
                var status = "Offline";
                var statusColor = Color.Gray;

                if (friend.Presence != null)
                {
                    switch (friend.Presence.userPresenceType)
                    {
                        case UserPresenceType.InGame:
                            status = "Em Jogo";
                            statusColor = Color.Green;
                            break;
                        case UserPresenceType.Online:
                            status = "Online";
                            statusColor = Color.Blue;
                            break;
                        case UserPresenceType.InStudio:
                            status = "No Studio";
                            statusColor = Color.Purple;
                            break;
                    }
                }

                var item = new ListViewItem(friend.Username);
                item.SubItems.Add(friend.DisplayName);
                item.SubItems.Add(status);
                item.SubItems.Add(friend.GameName ?? "-");
                item.ForeColor = statusColor;
                item.Tag = friend;

                friendsListView.Items.Add(item);
            }
        }

        private void SearchTextBox_TextChanged(object sender, EventArgs e)
        {
            ApplyFilter();
        }

        private void FilterComboBox_SelectedIndexChanged(object sender, EventArgs e)
        {
            ApplyFilter();
        }

        private void FriendsListView_DoubleClick(object sender, EventArgs e)
        {
            FollowSelectedFriend();
        }

        private void FollowButton_Click(object sender, EventArgs e)
        {
            FollowSelectedFriend();
        }

        private async void FollowSelectedFriend()
        {
            if (friendsListView.SelectedItems.Count == 0)
            {
                MessageBox.Show("Selecione um amigo para seguir.", "Aviso", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            var selectedItem = friendsListView.SelectedItems[0];
            var friend = selectedItem.Tag as FriendInfo;

            if (friend == null)
                return;

            if (friend.Presence == null || friend.Presence.userPresenceType != UserPresenceType.InGame)
            {
                MessageBox.Show($"{friend.DisplayName} não está em jogo no momento.", "Aviso", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            var result = MessageBox.Show(
                $"Deseja seguir {friend.DisplayName} ({friend.Username})?\n\nJogo: {friend.GameName}",
                "Confirmar",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);

            if (result != DialogResult.Yes)
                return;

            statusLabel.Text = $"Entrando no jogo de {friend.DisplayName}...";
            followButton.Enabled = false;

            try
            {
                // Usar o método JoinServer com FollowUser = true
                string joinResult = await _account.JoinServer(friend.UserId, "", true, false);

                if (joinResult == "Success")
                {
                    statusLabel.Text = $"Seguindo {friend.DisplayName}!";
                    this.Close();
                }
                else
                {
                    statusLabel.Text = $"Erro: {joinResult}";
                    MessageBox.Show($"Erro ao seguir amigo: {joinResult}", "Erro", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
            catch (Exception ex)
            {
                statusLabel.Text = $"Erro: {ex.Message}";
                MessageBox.Show($"Erro ao seguir amigo: {ex.Message}", "Erro", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }

            followButton.Enabled = true;
        }

        private void AddFriendTextBox_GotFocus(object sender, EventArgs e)
        {
            if (addFriendTextBox.Text == "Username do jogador")
            {
                addFriendTextBox.Text = "";
                addFriendTextBox.ForeColor = Color.Black;
            }
        }

        private void AddFriendTextBox_LostFocus(object sender, EventArgs e)
        {
            if (string.IsNullOrWhiteSpace(addFriendTextBox.Text))
            {
                addFriendTextBox.Text = "Username do jogador";
                addFriendTextBox.ForeColor = Color.Gray;
            }
        }

        private void AddFriendTextBox_KeyPress(object sender, KeyPressEventArgs e)
        {
            if (e.KeyChar == (char)Keys.Enter)
            {
                e.Handled = true;
                AddFriendButton_Click(sender, e);
            }
        }

        private async void AddFriendButton_Click(object sender, EventArgs e)
        {
            var username = addFriendTextBox.Text.Trim();
            
            if (string.IsNullOrWhiteSpace(username) || username == "Username do jogador")
            {
                addFriendStatusLabel.ForeColor = Color.Red;
                addFriendStatusLabel.Text = "Digite um username";
                return;
            }

            addFriendButton.Enabled = false;
            addFriendStatusLabel.ForeColor = Color.Black;
            addFriendStatusLabel.Text = "Buscando usuário...";

            try
            {
                // Primeiro, buscar o ID do usuário pelo username
                var userId = await GetUserIdByUsername(username);
                
                if (userId == 0)
                {
                    addFriendStatusLabel.ForeColor = Color.Red;
                    addFriendStatusLabel.Text = "Usuário não encontrado";
                    addFriendButton.Enabled = true;
                    return;
                }

                // Abrir perfil no navegador da conta e clicar em Add Connection
                var profileUrl = $"https://www.roblox.com/users/{userId}/profile";
                
                // Usar PostNavigation para clicar no botão e fechar
                new RBX_Alt_Manager.Classes.AccountBrowser(_account, profileUrl, null, async (page) =>
                {
                    try
                    {
                        // Esperar a página carregar
                        await Task.Delay(2000);
                        
                        // Clicar no botão Add Connection
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
                            // Esperar 1.5 segundos e fechar
                            await Task.Delay(1500);
                        }
                        
                        // Fechar o browser
                        await page.Browser.CloseAsync();
                    }
                    catch { }
                });
                
                addFriendStatusLabel.ForeColor = Color.Green;
                addFriendStatusLabel.Text = $"Adicionando {username}...";
                addFriendTextBox.Text = "Username do jogador";
                addFriendTextBox.ForeColor = Color.Gray;
                AccountManager.AddLog($"🌐 [AddFriend] Abrindo perfil de {username} e clicando em Add Connection");
            }
            catch (Exception ex)
            {
                addFriendStatusLabel.ForeColor = Color.Red;
                addFriendStatusLabel.Text = $"Erro: {ex.Message}";
                AccountManager.AddLogError($"❌ [AddFriend] Erro: {ex.Message}");
            }

            addFriendButton.Enabled = true;
        }

        private async Task<long> GetUserIdByUsername(string username)
        {
            try
            {
                var client = new RestClient("https://users.roblox.com");
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
                        return users[0]["id"]?.Value<long>() ?? 0;
                    }
                }
            }
            catch (Exception ex)
            {
                AccountManager.AddLogError($"❌ [FollowFriend] Erro ao buscar usuário: {ex.Message}");
            }

            return 0;
        }

        private async Task<bool> SendFriendRequest(long userId)
        {
            try
            {
                var client = new RestClient("https://friends.roblox.com");
                var request = new RestRequest($"/v1/users/{userId}/request-friendship", Method.Post);
                request.AddHeader("Cookie", $".ROBLOSECURITY={_account.SecurityToken}");
                request.AddHeader("Content-Type", "application/json");

                AccountManager.AddLog($"🔍 [AddFriend] Enviando solicitação para userId={userId}");

                // Primeira tentativa para obter X-CSRF-Token
                var csrfResponse = await client.ExecuteAsync(request);
                
                AccountManager.AddLog($"🔍 [AddFriend] Primeira resposta: Status={csrfResponse.StatusCode}, Content={csrfResponse.Content?.Substring(0, Math.Min(200, csrfResponse.Content?.Length ?? 0))}");

                if (csrfResponse.StatusCode == System.Net.HttpStatusCode.Forbidden)
                {
                    var csrfToken = csrfResponse.Headers?.FirstOrDefault(h => h.Name.Equals("x-csrf-token", StringComparison.OrdinalIgnoreCase))?.Value?.ToString();
                    
                    AccountManager.AddLog($"🔍 [AddFriend] CSRF Token obtido: {csrfToken ?? "NULO"}");
                    
                    if (!string.IsNullOrEmpty(csrfToken))
                    {
                        request = new RestRequest($"/v1/users/{userId}/request-friendship", Method.Post);
                        request.AddHeader("Cookie", $".ROBLOSECURITY={_account.SecurityToken}");
                        request.AddHeader("Content-Type", "application/json");
                        request.AddHeader("X-CSRF-TOKEN", csrfToken);
                        
                        var finalResponse = await client.ExecuteAsync(request);
                        
                        AccountManager.AddLog($"🔍 [AddFriend] Segunda resposta: Status={finalResponse.StatusCode}, Content={finalResponse.Content}");
                        
                        if (finalResponse.IsSuccessful)
                        {
                            AccountManager.AddLogSuccess($"✅ [AddFriend] Solicitação enviada com sucesso!");
                            return true;
                        }
                        else
                        {
                            AccountManager.AddLogError($"❌ [AddFriend] Falha: {finalResponse.StatusCode} - {finalResponse.Content}");
                            
                            // Verificar mensagem de erro específica
                            if (finalResponse.Content != null)
                            {
                                addFriendStatusLabel.Text = GetFriendlyErrorMessage(finalResponse.Content);
                            }
                            return false;
                        }
                    }
                }
                else if (csrfResponse.IsSuccessful)
                {
                    AccountManager.AddLogSuccess($"✅ [AddFriend] Solicitação enviada na primeira tentativa!");
                    return true;
                }
                else
                {
                    AccountManager.AddLogError($"❌ [AddFriend] Erro inesperado: {csrfResponse.StatusCode} - {csrfResponse.Content}");
                    if (csrfResponse.Content != null)
                    {
                        addFriendStatusLabel.Text = GetFriendlyErrorMessage(csrfResponse.Content);
                    }
                }

                return false;
            }
            catch (Exception ex)
            {
                AccountManager.AddLogError($"❌ [AddFriend] Exceção: {ex.Message}");
                return false;
            }
        }

        private string GetFriendlyErrorMessage(string content)
        {
            try
            {
                var json = JsonConvert.DeserializeObject<JObject>(content);
                var errors = json?["errors"]?.ToObject<List<JObject>>();
                if (errors != null && errors.Count > 0)
                {
                    var code = errors[0]["code"]?.Value<int>() ?? 0;
                    var message = errors[0]["message"]?.ToString() ?? "";
                    
                    // Traduzir erros comuns
                    switch (code)
                    {
                        case 1: return "Usuário não encontrado";
                        case 2: return "Você foi bloqueado por este usuário";
                        case 3: return "Solicitação já enviada";
                        case 4: return "Vocês já são amigos";
                        case 5: return "Você não pode enviar para si mesmo";
                        case 6: return "Limite de amigos atingido";
                        case 7: return "O usuário atingiu o limite de amigos";
                        case 8: return "Usuário não aceita solicitações";
                        case 10: return "Captcha necessário";
                        case 11: return "Usuário não encontrado";
                        default: return $"Erro {code}: {message}";
                    }
                }
            }
            catch { }
            return "Erro desconhecido";
        }
    }

    public class FriendInfo
    {
        public long UserId { get; set; }
        public string Username { get; set; }
        public string DisplayName { get; set; }
        public UserPresence Presence { get; set; }
        public string GameName { get; set; }
    }
}

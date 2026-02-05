using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Newtonsoft.Json;
using RBX_Alt_Manager.Classes;

namespace RBX_Alt_Manager.Controls
{
    /// <summary>
    /// Controle do painel de inventário usando Supabase
    /// Hierarquia: Jogos → Itens → Contas (expandíveis)
    /// </summary>
    public partial class InventoryPanelControl : UserControl
    {
        // Debounce
        private Dictionary<string, CancellationTokenSource> _debounceTokens = new Dictionary<string, CancellationTokenSource>();
        private const int DEBOUNCE_DELAY_MS = 1500;

        // Estado
        private SupabaseGame _selectedGame;
        private List<SupabaseGame> _games = new List<SupabaseGame>();
        private List<SupabaseGameItem> _gameItems = new List<SupabaseGameItem>();
        private Dictionary<int, List<SupabaseInventoryEntry>> _inventoryByItem = new Dictionary<int, List<SupabaseInventoryEntry>>();
        private Dictionary<int, bool> _expandedItems = new Dictionary<int, bool>();
        
        // Modo Contas Vazias
        private bool _emptyAccountsMode = false;
        private SupabaseGame _emptyAccountsSelectedGame = null;
        private List<SupabaseAccount> _allAccounts = new List<SupabaseAccount>();
        
        // Favoritos
        private HashSet<int> _favoriteGames = new HashSet<int>();
        private string _favoritesFilePath;
        
        // Busca
        private string _searchText = "";
        private TextBox _searchBox;
        private Panel _searchPanel;
        
        // Guarda de re-entrância para evitar RefreshItemsPanel ser chamado recursivamente
        private bool _isRefreshingItems = false;

        // Eventos
        public event EventHandler<string> LogMessage;
        public event EventHandler<string> LogWarning;
        public event EventHandler<string> LogError;
        public event EventHandler<string> AccountSelected; // Para selecionar conta no AccountManager

        // Componentes
        private Panel _headerPanel;
        private Label _titleLabel;
        private Button _backButton;
        private Button _addButton;
        private Button _refreshButton;
        private FlowLayoutPanel _contentPanel;

        public InventoryPanelControl()
        {
            InitializeComponent();
            
            // Definir caminho do arquivo de favoritos
            _favoritesFilePath = Path.Combine(Environment.CurrentDirectory, "favorite_games.json");
            LoadFavorites();
        }

        /// <summary>
        /// Atualiza a quantidade de um inventory entry na UI (chamado pelo painel ALTERAR ESTOQUE)
        /// </summary>
        public void UpdateInventoryQuantity(int inventoryId, long newQuantity)
        {
            // Atualizar no cache local
            int itemId = -1;
            foreach (var kvp in _inventoryByItem)
            {
                var entry = kvp.Value.FirstOrDefault(e => e.Id == inventoryId);
                if (entry != null)
                {
                    entry.Quantity = newQuantity;
                    itemId = kvp.Key;
                    break;
                }
            }

            if (itemId == -1) return;

            // Atualizar a UI na thread correta
            if (InvokeRequired)
            {
                Invoke(new Action(() => UpdateInventoryLabelInUI(inventoryId, newQuantity, itemId)));
            }
            else
            {
                UpdateInventoryLabelInUI(inventoryId, newQuantity, itemId);
            }
        }

        /// <summary>
        /// Recarrega o inventário se o jogo e item atuais correspondem aos parâmetros
        /// Chamado quando uma conta é sincronizada para a nuvem
        /// </summary>
        public void RefreshIfCurrentGameItem(int gameId, int itemId)
        {
            // Verificar se estamos visualizando o jogo correto
            if (_selectedGame == null || _selectedGame.Id != gameId)
                return;

            // Verificar se o item está na lista de itens do jogo atual
            if (!_gameItems.Any(i => i.Id == itemId))
                return;

            // Recarregar o jogo atual para pegar a nova conta
            if (InvokeRequired)
            {
                Invoke(new Action(() => _ = LoadGameItemsAsync(_selectedGame)));
            }
            else
            {
                _ = LoadGameItemsAsync(_selectedGame);
            }
        }

        /// <summary>
        /// Recarrega o inventário se o jogo atual corresponde ao gameId.
        /// Usado quando o saldo de Robux é atualizado para refletir no painel.
        /// </summary>
        public void RefreshIfCurrentGame(int gameId)
        {
            if (_selectedGame == null || _selectedGame.Id != gameId)
                return;

            if (InvokeRequired)
            {
                Invoke(new Action(() => _ = LoadGameItemsAsync(_selectedGame)));
            }
            else
            {
                _ = LoadGameItemsAsync(_selectedGame);
            }
        }

        private void UpdateInventoryLabelInUI(int inventoryId, long newQuantity, int itemId)
        {
            if (_contentPanel == null) return;

            // Atualizar o label de quantidade no painel da conta (se estiver expandido)
            foreach (Control c in _contentPanel.Controls)
            {
                if (c is Panel p && p.Tag is SupabaseInventoryEntry inv && inv.Id == inventoryId)
                {
                    foreach (Control child in p.Controls)
                    {
                        if (child is Label lbl && lbl.Name == $"invqty_{inventoryId}")
                        {
                            lbl.Text = newQuantity.ToString("N0", new System.Globalization.CultureInfo("pt-BR"));
                            break;
                        }
                    }
                    break;
                }
            }

            // Atualizar o header do item (total de contas e total de quantidade)
            RefreshItemHeaderInfo(itemId);
        }

        private void RefreshItemHeaderInfo(int itemId)
        {
            if (!_inventoryByItem.ContainsKey(itemId) || _contentPanel == null) return;
            
            var inventory = _inventoryByItem[itemId];
            long totalQty = inventory.Sum(i => i.Quantity);
            
            // Encontrar o painel do item e atualizar a info
            foreach (Control c in _contentPanel.Controls)
            {
                if (c is Panel p && p.Tag is SupabaseGameItem item && item.Id == itemId)
                {
                    foreach (Control child in p.Controls)
                    {
                        if (child is Label lbl && lbl.ForeColor == Color.Gray)
                        {
                            lbl.Text = $"({inventory.Count} contas, {totalQty} total)";
                            break;
                        }
                    }
                    break;
                }
            }
        }

        private void InitializeComponent()
        {
            this.SuspendLayout();
            this.BackColor = Color.FromArgb(41, 41, 41);
            this.Size = new Size(310, 682);

            // Header Panel
            _headerPanel = new Panel
            {
                Dock = DockStyle.Top,
                Height = 35,
                BackColor = Color.FromArgb(30, 30, 30),
                Padding = new Padding(3)
            };

            // Botão Voltar (oculto inicialmente)
            _backButton = new Button
            {
                Text = "← Voltar",
                Font = new Font("Segoe UI", 7F),
                ForeColor = Color.White,
                BackColor = Color.FromArgb(60, 60, 60),
                FlatStyle = FlatStyle.Flat,
                Size = new Size(55, 22),
                Location = new Point(3, 6),
                Visible = false,
                Cursor = Cursors.Hand
            };
            _backButton.FlatAppearance.BorderSize = 0;
            _backButton.Click += BackButton_Click;

            // Título
            _titleLabel = new Label
            {
                Text = "INVENTÁRIO",
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                ForeColor = Color.White,
                Location = new Point(5, 8),
                AutoSize = true
            };

            // Botão Adicionar (Jogo ou Item dependendo do contexto)
            _addButton = new Button
            {
                Text = "+ Jogo",
                Font = new Font("Segoe UI", 7F),
                ForeColor = Color.White,
                BackColor = Color.FromArgb(0, 120, 0),
                FlatStyle = FlatStyle.Flat,
                Size = new Size(50, 22),
                Anchor = AnchorStyles.Top | AnchorStyles.Right,
                Cursor = Cursors.Hand
            };
            _addButton.FlatAppearance.BorderSize = 0;
            _addButton.Click += AddButton_Click;

            // Botão Refresh
            _refreshButton = new Button
            {
                Text = "🔄",
                Font = new Font("Segoe UI", 8F),
                ForeColor = Color.White,
                BackColor = Color.FromArgb(60, 60, 60),
                FlatStyle = FlatStyle.Flat,
                Size = new Size(25, 22),
                Anchor = AnchorStyles.Top | AnchorStyles.Right,
                Cursor = Cursors.Hand
            };
            _refreshButton.FlatAppearance.BorderSize = 0;
            _refreshButton.Click += RefreshButton_Click;

            _headerPanel.Controls.AddRange(new Control[] { _backButton, _titleLabel, _addButton, _refreshButton });

            // Search Panel (entre header e content)
            _searchPanel = new Panel
            {
                Dock = DockStyle.Top,
                Height = 28,
                BackColor = Color.FromArgb(35, 35, 35),
                Padding = new Padding(3, 3, 3, 3)
            };

            _searchBox = new TextBox
            {
                Text = "procurar...",
                Font = new Font("Segoe UI", 8F),
                ForeColor = Color.Gray,
                BackColor = Color.FromArgb(50, 50, 50),
                BorderStyle = BorderStyle.None,
                Dock = DockStyle.Fill
            };
            _searchBox.Enter += SearchBox_Enter;
            _searchBox.Leave += SearchBox_Leave;
            _searchBox.TextChanged += SearchBox_TextChanged;
            _searchBox.KeyDown += SearchBox_KeyDown;

            _searchPanel.Controls.Add(_searchBox);

            // Content Panel
            _contentPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                BackColor = Color.FromArgb(41, 41, 41),
                Padding = new Padding(3)
            };

            this.Controls.Add(_contentPanel);
            this.Controls.Add(_searchPanel);
            this.Controls.Add(_headerPanel);

            this.Resize += (s, e) => UpdateLayout();

            this.ResumeLayout(false);
        }

        private void UpdateLayout()
        {
            int rightMargin = 5;
            _refreshButton.Location = new Point(this.Width - _refreshButton.Width - rightMargin, 6);
            _addButton.Location = new Point(_refreshButton.Left - _addButton.Width - 3, 6);
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            UpdateLayout();
        }

        /// <summary>
        /// Inicializa e carrega lista de jogos
        /// </summary>
        public async Task InitializeAsync()
        {
            await LoadGamesAsync();
        }

        private async Task LoadGamesAsync()
        {
            _selectedGame = null;
            _backButton.Visible = false;
            _titleLabel.Text = "INVENTÁRIO";
            _titleLabel.Location = new Point(5, 8);
            _addButton.Text = "+ Jogo";
            _addButton.Visible = true;
            
            // Resetar modo contas vazias
            _emptyAccountsMode = false;
            _emptyAccountsSelectedGame = null;
            
            // Limpar busca ao voltar para jogos
            _searchBox.Text = "procurar...";
            _searchBox.ForeColor = Color.Gray;
            _searchText = "";
            
            // Limpar estados de expansão
            _expandedItems.Clear();

            _contentPanel.Controls.Clear();

            try
            {
                _games = await SupabaseManager.Instance.GetGamesAsync() ?? new List<SupabaseGame>();
                RefreshGamesPanel();
                OnLogMessage($"✅ {_games.Count} jogos carregados");
            }
            catch (Exception ex)
            {
                OnLogError($"Erro ao carregar jogos: {ex.Message}");
            }
        }

        private Panel CreateGamePanel(SupabaseGame game)
        {
            int width = _contentPanel.Width - 25;
            bool isFavorite = _favoriteGames.Contains(game.Id);
            
            var panel = new Panel
            {
                Size = new Size(width, 32),
                BackColor = Color.FromArgb(50, 50, 50),
                Margin = new Padding(0, 2, 0, 2),
                Cursor = Cursors.Hand,
                Tag = game
            };

            var nameLabel = new Label
            {
                Text = game.Name.ToUpper(),
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                ForeColor = Color.LimeGreen,
                Location = new Point(8, 7),
                Size = new Size(width - 40, 20),
                AutoEllipsis = true,
                Cursor = Cursors.Hand
            };

            // Botão de favorito (estrela)
            var favoriteBtn = new Label
            {
                Text = isFavorite ? "★" : "☆",
                Font = new Font("Segoe UI", 12F),
                ForeColor = isFavorite ? Color.Gold : Color.Gray,
                Location = new Point(width - 30, 4),
                Size = new Size(25, 25),
                Cursor = Cursors.Hand,
                Tag = game.Id
            };
            favoriteBtn.Click += (s, e) => {
                ToggleFavorite(game.Id);
            };

            // Menu de contexto para o jogo
            var contextMenu = new ContextMenuStrip();
            contextMenu.BackColor = Color.FromArgb(45, 45, 45);
            contextMenu.ForeColor = Color.White;
            contextMenu.Renderer = new DarkMenuRenderer();

            var editGameItem = new ToolStripMenuItem("✏️ Editar Nome");
            editGameItem.Click += (s, e) => ShowEditGameDialog(game);

            var archiveGameItem = new ToolStripMenuItem("📁 Arquivar Jogo");
            archiveGameItem.Click += (s, e) => ShowArchiveGameConfirmation(game);

            contextMenu.Items.Add(editGameItem);
            contextMenu.Items.Add(new ToolStripSeparator());
            contextMenu.Items.Add(archiveGameItem);

            panel.ContextMenuStrip = contextMenu;
            nameLabel.ContextMenuStrip = contextMenu;

            panel.Controls.AddRange(new Control[] { nameLabel, favoriteBtn });

            // Click para abrir jogo (mas não no botão de favorito)
            Action<object, EventArgs> onClick = (s, e) => _ = LoadGameItemsAsync(game);
            panel.Click += (s, e) => onClick(s, e);
            nameLabel.Click += (s, e) => onClick(s, e);

            return panel;
        }

        private async Task LoadGameItemsAsync(SupabaseGame game)
        {
            _selectedGame = game;
            _backButton.Visible = true;
            _titleLabel.Text = game.Name.ToUpper();
            _titleLabel.Location = new Point(60, 8);
            _addButton.Text = "+ Item";
            _addButton.Visible = true;
            
            // Limpar busca ao entrar em um jogo
            _searchBox.Text = "procurar...";
            _searchBox.ForeColor = Color.Gray;
            _searchText = "";

            _contentPanel.SuspendLayout();
            _contentPanel.Visible = false;
            _contentPanel.Controls.Clear();
            _expandedItems.Clear();

            try
            {
                // Mostrar loading
                var loadingLabel = new Label
                {
                    Text = "Carregando...",
                    Font = new Font("Segoe UI", 9F),
                    ForeColor = Color.Gray,
                    AutoSize = true,
                    Padding = new Padding(10)
                };
                _contentPanel.Controls.Add(loadingLabel);
                _contentPanel.Visible = true;
                _contentPanel.ResumeLayout(true);
                
                // Buscar itens e inventário em PARALELO para maior velocidade
                var itemsTask = SupabaseManager.Instance.GetGameItemsAsync(game.Id);
                var inventoryTask = SupabaseManager.Instance.GetInventoryByGameIdAsync(game.Id);
                
                await Task.WhenAll(itemsTask, inventoryTask);
                
                _gameItems = itemsTask.Result ?? new List<SupabaseGameItem>();
                var allInventory = inventoryTask.Result ?? new List<SupabaseInventoryEntry>();

                if (_gameItems.Count == 0)
                {
                    _contentPanel.SuspendLayout();
                    _contentPanel.Controls.Clear();
                    var emptyLabel = new Label
                    {
                        Text = "Nenhum item cadastrado.\nClique em '+ Item' para adicionar.",
                        Font = new Font("Segoe UI", 8F),
                        ForeColor = Color.Gray,
                        AutoSize = true,
                        Padding = new Padding(10)
                    };
                    _contentPanel.Controls.Add(emptyLabel);
                    _contentPanel.ResumeLayout(true);
                    return;
                }

                // Agrupar inventário por item_id localmente (sem requisições adicionais)
                _inventoryByItem.Clear();
                foreach (var item in _gameItems)
                {
                    _inventoryByItem[item.Id] = new List<SupabaseInventoryEntry>();
                    _expandedItems[item.Id] = false;
                }
                
                foreach (var inv in allInventory)
                {
                    if (_inventoryByItem.ContainsKey(inv.ItemId))
                    {
                        _inventoryByItem[inv.ItemId].Add(inv);
                    }
                }

                RefreshItemsPanel();
                OnLogMessage($"📦 {game.Name}: {_gameItems.Count} itens, {allInventory.Count} registros");
            }
            catch (Exception ex)
            {
                OnLogError($"Erro ao carregar itens: {ex.Message}");
            }
        }

        private void RefreshItemsPanel()
        {
            // Guarda de re-entrância: ctrl.Dispose() pode bombar mensagens do Windows
            // que re-entram neste método. Sem esta guarda, o painel pode ficar invisível.
            if (_isRefreshingItems) return;
            _isRefreshingItems = true;
            
            _contentPanel.SuspendLayout();
            _contentPanel.Visible = false;
            
            try
            {
                // Limpar controles existentes - cada Dispose em try/catch individual
                // para que uma exceção em um controle não impeça a limpeza dos outros
                while (_contentPanel.Controls.Count > 0)
                {
                    var ctrl = _contentPanel.Controls[0];
                    _contentPanel.Controls.RemoveAt(0);
                    try { ctrl.Dispose(); } catch { }
                }

                int width = _contentPanel.Width - 25;
                var controlsToAdd = new List<Control>();

                // Filtrar itens pela busca
                var filteredItems = _gameItems.AsEnumerable();
                if (!string.IsNullOrEmpty(_searchText))
                {
                    filteredItems = filteredItems.Where(i => 
                        i.Name.ToLower().Contains(_searchText) ||
                        (_inventoryByItem.ContainsKey(i.Id) && 
                         _inventoryByItem[i.Id].Any(inv => inv.Username.ToLower().Contains(_searchText)))
                    );
                }

                // Ordenar: itens com estoque > 0 primeiro (alfabético), depois itens com estoque 0 (alfabético)
                var sortedItems = filteredItems
                    .Select(i => new {
                        Item = i,
                        TotalQty = _inventoryByItem.ContainsKey(i.Id) 
                            ? _inventoryByItem[i.Id].Sum(inv => inv.Quantity) 
                            : 0
                    })
                    .OrderBy(x => x.TotalQty == 0 ? 1 : 0) // Estoque zero vai pro final
                    .ThenBy(x => x.Item.Name)
                    .Select(x => x.Item)
                    .ToList();

                foreach (var item in sortedItems)
                {
                    var inventory = _inventoryByItem.ContainsKey(item.Id) ? _inventoryByItem[item.Id] : new List<SupabaseInventoryEntry>();
                    bool isExpanded = _expandedItems.ContainsKey(item.Id) && _expandedItems[item.Id];

                    // Verificar se o item foi encontrado pelo nome ou por username de alguma conta
                    bool itemMatchedByName = !string.IsNullOrEmpty(_searchText) && 
                                             item.Name.ToLower().Contains(_searchText);
                    bool itemMatchedByUsername = !string.IsNullOrEmpty(_searchText) && 
                                                 inventory.Any(inv => inv.Username.ToLower().Contains(_searchText));

                    // Se buscando por username, expandir automaticamente os itens que contêm a conta
                    // Nota: NÃO salvamos em _expandedItems para não persistir após limpar busca
                    if (itemMatchedByUsername && !itemMatchedByName)
                    {
                        isExpanded = true;
                        // Removido: _expandedItems[item.Id] = true;
                    }

                    // Painel do item (header clicável)
                    var itemHeader = CreateItemHeaderPanel(item, inventory, width, isExpanded);
                    controlsToAdd.Add(itemHeader);

                    // Se expandido, mostrar contas
                    if (isExpanded && inventory.Count > 0)
                    {
                        var displayInventory = inventory.AsEnumerable();
                        
                        // Só filtrar por username se o item NÃO foi encontrado pelo nome
                        if (!string.IsNullOrEmpty(_searchText) && !itemMatchedByName)
                        {
                            displayInventory = displayInventory.Where(inv => 
                                inv.Username.ToLower().Contains(_searchText));
                        }
                        
                        foreach (var inv in displayInventory.OrderBy(i => i.Username))
                        {
                            var accountPanel = CreateAccountPanel(item, inv, width);
                            controlsToAdd.Add(accountPanel);
                        }
                    }
                }

                // Adicionar todos os controles de uma vez (mais eficiente)
                _contentPanel.Controls.AddRange(controlsToAdd.ToArray());
            }
            finally
            {
                // GARANTIR que o painel sempre volte a ficar visível e com layout ativo,
                // independente de qualquer exceção durante a reconstrução
                _contentPanel.Visible = true;
                _contentPanel.ResumeLayout(true);
                _isRefreshingItems = false;
            }
        }

        private Panel CreateItemHeaderPanel(SupabaseGameItem item, List<SupabaseInventoryEntry> inventory, int width, bool isExpanded)
        {
            long totalQty = inventory.Sum(i => i.Quantity);
            bool hasZeroStock = totalQty == 0;
            
            // Cor baseada no estoque
            Color itemColor = hasZeroStock ? Color.FromArgb(200, 80, 80) : Color.White; // Vermelho se zero
            Color arrowColor = hasZeroStock ? Color.FromArgb(200, 80, 80) : Color.LimeGreen;
            
            var panel = new Panel
            {
                Size = new Size(width, 28),
                BackColor = Color.FromArgb(45, 45, 45),
                Margin = new Padding(0, 2, 0, 0),
                Cursor = Cursors.Hand,
                Tag = item
            };

            // Seta de expansão
            var arrowLabel = new Label
            {
                Text = isExpanded ? "▼" : "▶",
                Font = new Font("Segoe UI", 7F),
                ForeColor = arrowColor,
                Location = new Point(5, 7),
                Size = new Size(15, 14),
                Cursor = Cursors.Hand
            };

            // Nome do item
            var nameLabel = new Label
            {
                Text = item.Name,
                Font = new Font("Segoe UI", 8F, FontStyle.Bold),
                ForeColor = itemColor,
                Location = new Point(22, 6),
                Size = new Size(width - 115, 16), // Reduzido para dar mais espaço ao número
                AutoEllipsis = true,
                Cursor = Cursors.Hand
            };

            // Info (total) - formatado com separador de milhares
            string formattedTotal = totalQty.ToString("N0", new System.Globalization.CultureInfo("pt-BR"));
            var infoLabel = new Label
            {
                Text = $"({formattedTotal})",
                Font = new Font("Segoe UI", 7F),
                ForeColor = hasZeroStock ? Color.FromArgb(200, 80, 80) : Color.Gray,
                Location = new Point(width - 110, 7),
                Size = new Size(80, 14), // Aumentado para números grandes
                TextAlign = ContentAlignment.MiddleRight
            };

            // Botão adicionar conta ao item (com menu de contexto)
            var addBtn = new SafeButton
            {
                Text = "+",
                Font = new Font("Segoe UI", 8F, FontStyle.Bold),
                ForeColor = Color.White,
                BackColor = Color.FromArgb(0, 100, 0),
                FlatStyle = FlatStyle.Flat,
                Size = new Size(20, 20),
                Location = new Point(width - 25, 4),
                Tag = item.Id, // Guardar apenas o ID, não o objeto
                Cursor = Cursors.Hand
            };
            addBtn.FlatAppearance.BorderSize = 0;
            
            // Click = mostrar menu dinâmico
            addBtn.Click += AddAccountButton_ShowMenu;

            // Menu de contexto para o item (editar/arquivar)
            var itemContextMenu = new ContextMenuStrip();
            itemContextMenu.BackColor = Color.FromArgb(45, 45, 45);
            itemContextMenu.ForeColor = Color.White;
            itemContextMenu.Renderer = new DarkMenuRenderer();

            var editItemMenuItem = new ToolStripMenuItem("✏️ Editar Nome");
            editItemMenuItem.Click += (s, ev) => ShowEditItemDialog(item);

            var archiveItemMenuItem = new ToolStripMenuItem("📁 Arquivar Item");
            archiveItemMenuItem.Click += (s, ev) => ShowArchiveItemConfirmation(item);

            itemContextMenu.Items.Add(editItemMenuItem);
            itemContextMenu.Items.Add(new ToolStripSeparator());
            itemContextMenu.Items.Add(archiveItemMenuItem);

            panel.ContextMenuStrip = itemContextMenu;
            nameLabel.ContextMenuStrip = itemContextMenu;
            arrowLabel.ContextMenuStrip = itemContextMenu;

            panel.Controls.AddRange(new Control[] { arrowLabel, nameLabel, infoLabel, addBtn });

            // Click para expandir/colapsar (otimizado - não reconstrói tudo)
            Action<object, EventArgs> toggleExpand = (s, e) =>
            {
                try
                {
                    // Verificar se os controles ainda existem
                    if (panel.IsDisposed || arrowLabel.IsDisposed || _contentPanel == null || _contentPanel.IsDisposed) return;
                    
                    // Verificar se a chave existe, se não, criar com valor false
                    if (!_expandedItems.ContainsKey(item.Id))
                    {
                        _expandedItems[item.Id] = false;
                    }
                    
                    bool newState = !_expandedItems[item.Id];
                    _expandedItems[item.Id] = newState;
                    
                    // Atualizar a seta
                    arrowLabel.Text = newState ? "▼" : "▶";
                    
                    // Encontrar o índice deste painel no contentPanel
                    int panelIndex = _contentPanel.Controls.IndexOf(panel);
                    if (panelIndex < 0) return;
                    
                    if (newState)
                    {
                        // Expandindo - adicionar painéis de contas após este
                        var invList = _inventoryByItem.ContainsKey(item.Id) ? _inventoryByItem[item.Id] : new List<SupabaseInventoryEntry>();
                        int insertIndex = panelIndex + 1;
                        int widthPanel = _contentPanel.Width - 25;
                        
                        // Verificar se a busca é por nome do item (não por username)
                        bool itemMatchedByName = !string.IsNullOrEmpty(_searchText) && 
                                                 item.Name.ToLower().Contains(_searchText);
                        
                        _contentPanel.SuspendLayout();
                        try
                        {
                            foreach (var inv in invList.OrderBy(i => i.Username))
                            {
                                if (!string.IsNullOrEmpty(_searchText) && 
                                    !itemMatchedByName &&
                                    !inv.Username.ToLower().Contains(_searchText))
                                    continue;
                                    
                                var accountPanel = CreateAccountPanel(item, inv, widthPanel);
                                _contentPanel.Controls.Add(accountPanel);
                                _contentPanel.Controls.SetChildIndex(accountPanel, insertIndex++);
                            }
                        }
                        finally
                        {
                            _contentPanel.ResumeLayout(true);
                        }
                    }
                    else
                    {
                        // Colapsando - remover painéis de contas deste item
                        _contentPanel.SuspendLayout();
                        try
                        {
                            var toRemove = new List<Control>();
                            
                            for (int i = panelIndex + 1; i < _contentPanel.Controls.Count; i++)
                            {
                                var ctrl = _contentPanel.Controls[i];
                                if (ctrl.Tag is SupabaseGameItem) break;
                                if (ctrl.Tag is SupabaseInventoryEntry inv && 
                                    _inventoryByItem.ContainsKey(item.Id) && 
                                    _inventoryByItem[item.Id].Any(x => x.Id == inv.Id))
                                {
                                    toRemove.Add(ctrl);
                                }
                            }
                            
                            foreach (var ctrl in toRemove)
                            {
                                _contentPanel.Controls.Remove(ctrl);
                                try { ctrl.Dispose(); } catch { }
                            }
                        }
                        finally
                        {
                            _contentPanel.ResumeLayout(true);
                        }
                    }
                }
                catch (ObjectDisposedException) { }
            };

            panel.Click += (s, e) => toggleExpand(s, e);
            arrowLabel.Click += (s, e) => toggleExpand(s, e);
            nameLabel.Click += (s, e) => toggleExpand(s, e);
            infoLabel.Click += (s, e) => toggleExpand(s, e);

            return panel;
        }

        private Panel CreateAccountPanel(SupabaseGameItem item, SupabaseInventoryEntry inventory, int width)
        {
            var panel = new Panel
            {
                Size = new Size(width - 15, 24),
                BackColor = Color.FromArgb(35, 35, 35),
                Margin = new Padding(15, 1, 0, 0),
                Tag = inventory
            };

            // Username (clicável para selecionar conta)
            var usernameLabel = new Label
            {
                Text = inventory.Username,
                Font = new Font("Segoe UI", 8F),
                ForeColor = Color.LimeGreen,
                Location = new Point(5, 4),
                Size = new Size(width - 130, 16), // Reduzido para dar mais espaço ao número
                AutoEllipsis = true,
                Cursor = Cursors.Hand
            };
            usernameLabel.Click += (s, e) => OnAccountSelected(inventory.Username);

            // Quantidade (alinhada à direita, apenas visualização)
            // Aumentado para caber números grandes como 490.000.000
            var qtyLabel = new Label
            {
                Text = inventory.Quantity.ToString("N0", new System.Globalization.CultureInfo("pt-BR")),
                Name = $"invqty_{inventory.Id}",
                Font = new Font("Segoe UI", 8F, FontStyle.Bold),
                ForeColor = Color.White,
                Location = new Point(width - 115, 4),
                Size = new Size(80, 16), // Aumentado de 50 para 80
                TextAlign = ContentAlignment.MiddleRight
            };

            // Botão remover
            var removeBtn = new SafeButton
            {
                Text = "×",
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                ForeColor = Color.White,
                BackColor = Color.FromArgb(120, 40, 40),
                FlatStyle = FlatStyle.Flat,
                Size = new Size(20, 18),
                Location = new Point(width - 32, 3),
                Tag = inventory,
                Cursor = Cursors.Hand
            };
            removeBtn.FlatAppearance.BorderSize = 0;
            removeBtn.Click += RemoveAccountButton_Click;

            // Menu de contexto (click direito)
            var contextMenu = new ContextMenuStrip();
            contextMenu.BackColor = Color.FromArgb(45, 45, 45);
            contextMenu.ForeColor = Color.White;
            contextMenu.Renderer = new DarkMenuRenderer();

            var moveToEmptyItem = new ToolStripMenuItem("📤 Mover para Contas Vazias");
            moveToEmptyItem.Click += (s, e) => MoveToEmptyAccounts(item, inventory);
            
            var changeItemMenu = new ToolStripMenuItem("🔄 Alterar Item");
            changeItemMenu.Click += (s, e) => ShowChangeItemDialog(item, inventory);

            var archiveItem = new ToolStripMenuItem("📁 Arquivar Conta");
            archiveItem.Click += (s, e) => ArchiveAccountFromInventory(item, inventory);

            contextMenu.Items.Add(moveToEmptyItem);
            contextMenu.Items.Add(changeItemMenu);
            contextMenu.Items.Add(new ToolStripSeparator());
            contextMenu.Items.Add(archiveItem);

            panel.ContextMenuStrip = contextMenu;
            usernameLabel.ContextMenuStrip = contextMenu;

            panel.Controls.AddRange(new Control[] { usernameLabel, qtyLabel, removeBtn });

            return panel;
        }

        #region Event Handlers

        private void BackButton_Click(object sender, EventArgs e)
        {
            if (_emptyAccountsMode)
            {
                if (_emptyAccountsSelectedGame != null)
                {
                    // Voltar para lista de jogos no modo contas vazias
                    _emptyAccountsSelectedGame = null;
                    _titleLabel.Text = "CONTAS VAZIAS";
                    RefreshEmptyAccountsGamesPanel();
                }
                else
                {
                    // Sair do modo contas vazias e voltar para lista normal de jogos
                    _emptyAccountsMode = false;
                    _ = LoadGamesAsync();
                }
            }
            else
            {
                _ = LoadGamesAsync();
            }
        }

        private void AddButton_Click(object sender, EventArgs e)
        {
            if (_selectedGame == null)
            {
                // Adicionar jogo
                string name = ShowInputDialog("Nome do novo jogo:", "Adicionar Jogo");
                if (!string.IsNullOrWhiteSpace(name))
                {
                    _ = AddGameAsync(name);
                }
            }
            else
            {
                // Adicionar item ao jogo
                string name = ShowInputDialog($"Nome do novo item para {_selectedGame.Name}:", "Adicionar Item");
                if (!string.IsNullOrWhiteSpace(name))
                {
                    _ = AddItemAsync(name);
                }
            }
        }

        private async Task AddGameAsync(string name)
        {
            try
            {
                var game = await SupabaseManager.Instance.AddGameAsync(name);
                if (game != null)
                {
                    OnLogMessage($"✅ Jogo '{name}' adicionado");
                    await LoadGamesAsync();
                }
                else
                {
                    OnLogError("Erro ao adicionar jogo");
                }
            }
            catch (Exception ex)
            {
                OnLogError($"Erro: {ex.Message}");
            }
        }

        private async Task AddItemAsync(string name)
        {
            if (_selectedGame == null) return;

            try
            {
                var item = await SupabaseManager.Instance.AddGameItemAsync(_selectedGame.Id, name);
                if (item != null)
                {
                    OnLogMessage($"✅ Item '{name}' adicionado");
                    await LoadGameItemsAsync(_selectedGame);
                }
                else
                {
                    OnLogError("Erro ao adicionar item");
                }
            }
            catch (Exception ex)
            {
                OnLogError($"Erro: {ex.Message}");
            }
        }

        private void AddAccountToItemButton_Click(object sender, EventArgs e)
        {
            var btn = sender as Button;
            var item = btn?.Tag as SupabaseGameItem;
            if (item == null) return;

            _ = AddNextAvailableAccountToItemAsync(item);
        }

        private void AddAccountManual(SupabaseGameItem item)
        {
            string username = ShowInputDialog($"Username da conta para '{item.Name}':", "Adicionar Conta Manual");
            if (!string.IsNullOrWhiteSpace(username))
            {
                _ = AddAccountToItemDirectAsync(item, username.Trim());
            }
        }

        private async Task AddNextAvailableAccountToItemAsync(SupabaseGameItem item)
        {
            try
            {
                // Buscar todas as contas do Supabase
                var allAccounts = await SupabaseManager.Instance.GetAccountsAsync() ?? new List<SupabaseAccount>();
                
                if (allAccounts.Count == 0)
                {
                    OnLogWarning("⚠️ Nenhuma conta cadastrada no Supabase");
                    MessageBox.Show("Nenhuma conta cadastrada.\nSincronize suas contas primeiro.", "Sem contas", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                // Pegar usernames já adicionados a ESTE item
                var existingInThisItem = _inventoryByItem.ContainsKey(item.Id) 
                    ? _inventoryByItem[item.Id].Select(i => i.Username.ToLower()).ToHashSet() 
                    : new HashSet<string>();

                // Primeiro: tentar encontrar uma conta que NÃO está em nenhum item deste jogo
                var usernamesInAnyItemOfThisGame = new HashSet<string>();
                foreach (var gameItem in _gameItems)
                {
                    if (_inventoryByItem.ContainsKey(gameItem.Id))
                    {
                        foreach (var inv in _inventoryByItem[gameItem.Id])
                        {
                            usernamesInAnyItemOfThisGame.Add(inv.Username.ToLower());
                        }
                    }
                }

                var completelyFreeAccount = allAccounts.FirstOrDefault(a => 
                    !usernamesInAnyItemOfThisGame.Contains(a.Username.ToLower()));

                if (completelyFreeAccount != null)
                {
                    // Conta totalmente livre - adicionar direto
                    await AddAccountToItemDirectAsync(item, completelyFreeAccount.Username);
                    return;
                }

                // Segundo: procurar conta em OUTRO item do mesmo jogo com estoque 0
                SupabaseInventoryEntry zeroStockEntry = null;
                SupabaseGameItem sourceItem = null;

                foreach (var gameItem in _gameItems)
                {
                    if (gameItem.Id == item.Id) continue; // Pular o item atual
                    
                    if (_inventoryByItem.ContainsKey(gameItem.Id))
                    {
                        var entryWithZero = _inventoryByItem[gameItem.Id]
                            .FirstOrDefault(inv => inv.Quantity == 0 && !existingInThisItem.Contains(inv.Username.ToLower()));
                        
                        if (entryWithZero != null)
                        {
                            zeroStockEntry = entryWithZero;
                            sourceItem = gameItem;
                            break;
                        }
                    }
                }

                if (zeroStockEntry != null && sourceItem != null)
                {
                    // Encontrou conta com estoque 0 em outro item - mover
                    OnLogMessage($"🔄 Movendo '{zeroStockEntry.Username}' de '{sourceItem.Name}' para '{item.Name}'");
                    
                    // Remover do item antigo
                    await SupabaseManager.Instance.DeleteInventoryAsync(zeroStockEntry.Id);
                    if (_inventoryByItem.ContainsKey(sourceItem.Id))
                    {
                        _inventoryByItem[sourceItem.Id].RemoveAll(i => i.Id == zeroStockEntry.Id);
                    }
                    
                    // Adicionar ao novo item
                    await AddAccountToItemDirectAsync(item, zeroStockEntry.Username);
                    return;
                }

                // Terceiro: verificar se simplesmente não tem conta disponível
                var availableAccount = allAccounts.FirstOrDefault(a => 
                    !existingInThisItem.Contains(a.Username.ToLower()));

                if (availableAccount == null)
                {
                    OnLogWarning($"⚠️ Todas as contas já estão em '{item.Name}'");
                    MessageBox.Show($"Todas as {allAccounts.Count} contas já estão adicionadas a este item.", "Sem contas disponíveis", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                // Conta está em outro item mas com estoque > 0, perguntar se quer adicionar mesmo assim
                var result = MessageBox.Show(
                    $"A conta '{availableAccount.Username}' já está em outro item deste jogo com estoque.\n\nDeseja adicioná-la mesmo assim a '{item.Name}'?",
                    "Conta já em uso",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question);

                if (result == DialogResult.Yes)
                {
                    await AddAccountToItemDirectAsync(item, availableAccount.Username);
                }
            }
            catch (Exception ex)
            {
                OnLogError($"Erro: {ex.Message}");
            }
        }

        private async Task AddAccountToItemDirectAsync(SupabaseGameItem item, string username)
        {
            bool success = await SupabaseManager.Instance.UpsertInventoryAsync(username, item.Id, 0);
            if (success)
            {
                OnLogMessage($"✅ '{username}' adicionada a '{item.Name}'");
                
                // Recarregar inventário do item
                var inventory = await SupabaseManager.Instance.GetInventoryByItemAsync(item.Id);
                _inventoryByItem[item.Id] = inventory ?? new List<SupabaseInventoryEntry>();
                _expandedItems[item.Id] = true; // Expandir para mostrar
                
                RefreshItemsPanel();
            }
            else
            {
                OnLogError("Erro ao adicionar conta");
            }
        }

        private void AddAccountButton_ShowMenu(object sender, EventArgs e)
        {
            try
            {
                var btn = sender as Button;
                if (btn == null || btn.IsDisposed) return;
                
                // Obter o ID do item do Tag
                if (!(btn.Tag is int itemId)) return;
                
                // Buscar o item atual na lista
                var item = _gameItems?.FirstOrDefault(i => i.Id == itemId);
                if (item == null) return;
                
                // Criar menu dinâmico (não fica preso a objetos antigos)
                var contextMenu = new ContextMenuStrip();
                contextMenu.BackColor = Color.FromArgb(45, 45, 45);
                contextMenu.ForeColor = Color.White;
                contextMenu.Renderer = new DarkMenuRenderer();
                
                var autoItem = new ToolStripMenuItem("🔄 Adicionar Automático");
                autoItem.Click += (s, ev) => _ = AddNextAvailableAccountToItemAsync(item);
                
                var manualItem = new ToolStripMenuItem("✏️ Adicionar Manual");
                manualItem.Click += (s, ev) => AddAccountManual(item);
                
                contextMenu.Items.Add(autoItem);
                contextMenu.Items.Add(manualItem);
                
                contextMenu.Show(btn, new Point(0, btn.Height));
            }
            catch (ObjectDisposedException) { }
            catch (Exception ex)
            {
                OnLogError($"Erro ao mostrar menu: {ex.Message}");
            }
        }

        private void RemoveAccountButton_Click(object sender, EventArgs e)
        {
            var btn = sender as Button;
            if (btn == null || btn.IsDisposed) return;
            
            var inventory = btn.Tag as SupabaseInventoryEntry;
            if (inventory == null) return;

            var result = MessageBox.Show(
                $"Remover '{inventory.Username}' deste item?",
                "Confirmar",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);

            if (result == DialogResult.Yes)
            {
                _ = RemoveAccountFromItemAsync(inventory);
            }
        }

        private async Task RemoveAccountFromItemAsync(SupabaseInventoryEntry inventory)
        {
            if (inventory == null) return;
            
            try
            {
                // Verificar se o item ainda existe no cache antes de tentar remover
                if (!_inventoryByItem.ContainsKey(inventory.ItemId))
                {
                    OnLogWarning("Item não encontrado no cache. Recarregando...");
                    if (_selectedGame != null)
                    {
                        await LoadGameItemsAsync(_selectedGame);
                    }
                    return;
                }
                
                // Verificar se o registro ainda existe na lista
                var existsInCache = _inventoryByItem[inventory.ItemId].Any(i => i.Id == inventory.Id);
                if (!existsInCache)
                {
                    OnLogWarning("Registro já foi removido.");
                    RefreshItemsPanel();
                    return;
                }
                
                bool success = await SupabaseManager.Instance.DeleteInventoryAsync(inventory.Id);
                if (success)
                {
                    OnLogMessage($"✅ Conta removida");
                    
                    // Atualizar lista local
                    if (_inventoryByItem.ContainsKey(inventory.ItemId))
                    {
                        _inventoryByItem[inventory.ItemId].RemoveAll(i => i.Id == inventory.Id);
                    }
                    
                    RefreshItemsPanel();
                }
            }
            catch (Exception ex)
            {
                OnLogError($"Erro: {ex.Message}");
            }
        }

        private void RefreshButton_Click(object sender, EventArgs e)
        {
            _refreshButton.Enabled = false;

            if (_selectedGame != null)
            {
                _ = LoadGameItemsAsync(_selectedGame);
            }
            else
            {
                _ = LoadGamesAsync();
            }

            OnLogMessage("🔄 Atualizando...");

            Task.Run(async () =>
            {
                await Task.Delay(2000);
                if (_refreshButton.InvokeRequired)
                    _refreshButton.Invoke(new Action(() => _refreshButton.Enabled = true));
                else
                    _refreshButton.Enabled = true;
            });
        }

        #endregion

        #region Event Helpers

        protected virtual void OnLogMessage(string message) => LogMessage?.Invoke(this, message);
        protected virtual void OnLogWarning(string message) => LogWarning?.Invoke(this, message);
        protected virtual void OnLogError(string message) => LogError?.Invoke(this, message);
        protected virtual void OnAccountSelected(string username) => AccountSelected?.Invoke(this, username);

        #endregion

        #region Helper Methods

        /// <summary>
        /// Exibe um dialog para entrada de texto (substitui Microsoft.VisualBasic.Interaction.InputBox)
        /// </summary>
        private string ShowInputDialog(string prompt, string title, string defaultValue = "")
        {
            using (Form inputForm = new Form())
            {
                inputForm.Text = title;
                inputForm.Size = new Size(350, 150);
                inputForm.StartPosition = FormStartPosition.CenterParent;
                inputForm.FormBorderStyle = FormBorderStyle.FixedDialog;
                inputForm.MaximizeBox = false;
                inputForm.MinimizeBox = false;
                inputForm.BackColor = Color.FromArgb(45, 45, 45);

                Label promptLabel = new Label
                {
                    Text = prompt,
                    Location = new Point(10, 15),
                    Size = new Size(310, 20),
                    ForeColor = Color.White,
                    Font = new Font("Segoe UI", 9F)
                };

                TextBox inputTextBox = new TextBox
                {
                    Text = defaultValue,
                    Location = new Point(10, 40),
                    Size = new Size(310, 25),
                    Font = new Font("Segoe UI", 9F),
                    BackColor = Color.FromArgb(60, 60, 60),
                    ForeColor = Color.White,
                    BorderStyle = BorderStyle.FixedSingle
                };

                Button okButton = new Button
                {
                    Text = "OK",
                    Location = new Point(160, 75),
                    Size = new Size(75, 28),
                    DialogResult = DialogResult.OK,
                    FlatStyle = FlatStyle.Flat,
                    BackColor = Color.FromArgb(0, 120, 0),
                    ForeColor = Color.White,
                    Font = new Font("Segoe UI", 9F)
                };
                okButton.FlatAppearance.BorderSize = 0;

                Button cancelButton = new Button
                {
                    Text = "Cancelar",
                    Location = new Point(245, 75),
                    Size = new Size(75, 28),
                    DialogResult = DialogResult.Cancel,
                    FlatStyle = FlatStyle.Flat,
                    BackColor = Color.FromArgb(80, 80, 80),
                    ForeColor = Color.White,
                    Font = new Font("Segoe UI", 9F)
                };
                cancelButton.FlatAppearance.BorderSize = 0;

                inputForm.Controls.AddRange(new Control[] { promptLabel, inputTextBox, okButton, cancelButton });
                inputForm.AcceptButton = okButton;
                inputForm.CancelButton = cancelButton;

                if (inputForm.ShowDialog() == DialogResult.OK)
                {
                    return inputTextBox.Text.Trim();
                }
                return string.Empty;
            }
        }

        #endregion

        #region Search and Favorites

        private void SearchBox_Enter(object sender, EventArgs e)
        {
            if (_searchBox.Text == "procurar...")
            {
                _searchBox.Text = "";
                _searchBox.ForeColor = Color.White;
            }
        }

        private void SearchBox_Leave(object sender, EventArgs e)
        {
            if (string.IsNullOrWhiteSpace(_searchBox.Text))
            {
                _searchBox.Text = "procurar...";
                _searchBox.ForeColor = Color.Gray;
                _searchText = "";
                
                // Limpar estados de expansão ao limpar busca
                _expandedItems.Clear();
                ApplyFilter();
            }
        }

        private void SearchBox_TextChanged(object sender, EventArgs e)
        {
            if (_searchBox.Text != "procurar...")
            {
                _searchText = _searchBox.Text.ToLower().Trim();
                ApplyFilter();
            }
        }

        private void SearchBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape)
            {
                _searchBox.Text = "";
                _searchText = "";
                _searchBox.Text = "procurar...";
                _searchBox.ForeColor = Color.Gray;
                
                // Limpar estados de expansão ao limpar busca
                _expandedItems.Clear();
                
                ApplyFilter();
            }
        }

        private void ApplyFilter()
        {
            if (_emptyAccountsMode)
            {
                if (_emptyAccountsSelectedGame != null)
                {
                    // Filtrando contas vazias de um jogo
                    _ = LoadEmptyAccountsForGameAsync(_emptyAccountsSelectedGame);
                }
                else
                {
                    // Filtrando lista de jogos no modo contas vazias
                    RefreshEmptyAccountsGamesPanel();
                }
            }
            else if (_selectedGame == null)
            {
                // Filtrando jogos
                RefreshGamesPanel();
            }
            else
            {
                // Filtrando itens
                RefreshItemsPanel();
            }
        }

        private void RefreshGamesPanel()
        {
            _contentPanel.SuspendLayout();
            _contentPanel.Visible = false;
            
            try
            {
                while (_contentPanel.Controls.Count > 0)
                {
                    var ctrl = _contentPanel.Controls[0];
                    _contentPanel.Controls.RemoveAt(0);
                    try { ctrl.Dispose(); } catch { }
                }

                if (_games.Count == 0)
                {
                    var emptyLabel = new Label
                    {
                        Text = "Nenhum jogo cadastrado.\nClique em '+ Jogo' para adicionar.",
                        Font = new Font("Segoe UI", 8F),
                        ForeColor = Color.Gray,
                        AutoSize = true,
                        Padding = new Padding(10)
                    };
                    _contentPanel.Controls.Add(emptyLabel);
                    return;
                }

                // Filtrar jogos
                var filteredGames = _games.AsEnumerable();
                if (!string.IsNullOrEmpty(_searchText))
                {
                    filteredGames = filteredGames.Where(g => g.Name.ToLower().Contains(_searchText));
                }

                // Ordenar: favoritos primeiro, depois alfabeticamente
                var sortedGames = filteredGames
                    .OrderByDescending(g => _favoriteGames.Contains(g.Id))
                    .ThenBy(g => g.Name)
                    .ToList();

                foreach (var game in sortedGames)
                {
                    var gamePanel = CreateGamePanel(game);
                    _contentPanel.Controls.Add(gamePanel);
                }

                // Adicionar "CONTAS VAZIAS" no final (se não houver busca ativa)
                if (string.IsNullOrEmpty(_searchText))
                {
                    var emptyAccountsPanel = CreateEmptyAccountsGameStylePanel();
                    _contentPanel.Controls.Add(emptyAccountsPanel);
                }
            }
            finally
            {
                _contentPanel.Visible = true;
                _contentPanel.ResumeLayout(true);
            }
        }

        private void ToggleFavorite(int gameId)
        {
            if (_favoriteGames.Contains(gameId))
            {
                _favoriteGames.Remove(gameId);
            }
            else
            {
                _favoriteGames.Add(gameId);
            }
            SaveFavorites();
            RefreshGamesPanel();
        }

        private void LoadFavorites()
        {
            try
            {
                if (File.Exists(_favoritesFilePath))
                {
                    var json = File.ReadAllText(_favoritesFilePath);
                    var list = JsonConvert.DeserializeObject<List<int>>(json);
                    if (list != null)
                    {
                        _favoriteGames = new HashSet<int>(list);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Erro ao carregar favoritos: {ex.Message}");
            }
        }

        private void SaveFavorites()
        {
            try
            {
                var json = JsonConvert.SerializeObject(_favoriteGames.ToList());
                File.WriteAllText(_favoritesFilePath, json);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Erro ao salvar favoritos: {ex.Message}");
            }
        }

        #endregion

        #region Contas Vazias

        /// <summary>
        /// Cria o painel clicável "CONTAS VAZIAS"
        /// </summary>
        private Panel CreateEmptyAccountsPanel()
        {
            int width = _contentPanel.Width - 25;
            
            var panel = new Panel
            {
                Size = new Size(width, 32),
                BackColor = Color.FromArgb(139, 69, 19), // Marrom/laranja escuro
                Margin = new Padding(0, 2, 0, 2),
                Cursor = Cursors.Hand
            };

            var nameLabel = new Label
            {
                Text = "CONTAS VAZIAS",
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                ForeColor = Color.White,
                Location = new Point(8, 7),
                Size = new Size(width - 20, 20),
                AutoEllipsis = true,
                Cursor = Cursors.Hand
            };

            panel.Controls.Add(nameLabel);

            // Click para abrir modo contas vazias
            Action<object, EventArgs> onClick = (s, e) => _ = LoadEmptyAccountsGamesAsync();
            panel.Click += (s, e) => onClick(s, e);
            nameLabel.Click += (s, e) => onClick(s, e);

            return panel;
        }

        /// <summary>
        /// Cria o painel "CONTAS VAZIAS" com mesmo estilo dos jogos
        /// </summary>
        private Panel CreateEmptyAccountsGameStylePanel()
        {
            int width = _contentPanel.Width - 25;
            
            var panel = new Panel
            {
                Size = new Size(width, 32),
                BackColor = Color.FromArgb(50, 50, 50), // Mesmo fundo dos jogos
                Margin = new Padding(0, 2, 0, 2),
                Cursor = Cursors.Hand
            };

            var nameLabel = new Label
            {
                Text = "CONTAS VAZIAS",
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                ForeColor = Color.FromArgb(205, 133, 63), // Laranja/marrom (Peru color)
                Location = new Point(8, 7),
                Size = new Size(width - 40, 20),
                AutoEllipsis = true,
                Cursor = Cursors.Hand
            };

            // Estrela vazia (sem funcionalidade de favorito)
            var placeholderStar = new Label
            {
                Text = "☆",
                Font = new Font("Segoe UI", 12F),
                ForeColor = Color.FromArgb(60, 60, 60), // Quase invisível
                Location = new Point(width - 30, 4),
                Size = new Size(25, 25)
            };

            panel.Controls.AddRange(new Control[] { nameLabel, placeholderStar });

            // Click para abrir modo contas vazias
            Action<object, EventArgs> onClick = (s, e) => _ = LoadEmptyAccountsGamesAsync();
            panel.Click += (s, e) => onClick(s, e);
            nameLabel.Click += (s, e) => onClick(s, e);

            return panel;
        }

        /// <summary>
        /// Carrega a lista de jogos no modo CONTAS VAZIAS
        /// </summary>
        private async Task LoadEmptyAccountsGamesAsync()
        {
            _emptyAccountsMode = true;
            _emptyAccountsSelectedGame = null;
            _backButton.Visible = true;
            _titleLabel.Text = "CONTAS VAZIAS";
            _titleLabel.Location = new Point(60, 8);
            _addButton.Visible = false;
            
            // Limpar busca
            _searchBox.Text = "procurar...";
            _searchBox.ForeColor = Color.Gray;
            _searchText = "";

            _contentPanel.SuspendLayout();
            _contentPanel.Visible = false;
            _contentPanel.Controls.Clear();

            try
            {
                // Mostrar loading
                var loadingLabel = new Label
                {
                    Text = "Carregando...",
                    Font = new Font("Segoe UI", 9F),
                    ForeColor = Color.Gray,
                    AutoSize = true,
                    Padding = new Padding(10)
                };
                _contentPanel.Controls.Add(loadingLabel);
                _contentPanel.Visible = true;
                _contentPanel.ResumeLayout(true);

                // Carregar jogos E contas do Supabase
                var gamesTask = SupabaseManager.Instance.GetGamesAsync();
                var accountsTask = SupabaseManager.Instance.GetAccountsAsync();
                
                await Task.WhenAll(gamesTask, accountsTask);
                
                _games = gamesTask.Result ?? new List<SupabaseGame>();
                _allAccounts = accountsTask.Result ?? new List<SupabaseAccount>();
                
                // Mostrar lista de jogos (sem favoritos, sem botão contas vazias)
                RefreshEmptyAccountsGamesPanel();
                
                OnLogMessage($"✅ Modo Contas Vazias: {_games.Count} jogos, {_allAccounts.Count} contas");
            }
            catch (Exception ex)
            {
                OnLogError($"Erro ao carregar contas: {ex.Message}");
            }
        }

        /// <summary>
        /// Atualiza o painel de jogos no modo CONTAS VAZIAS
        /// </summary>
        private void RefreshEmptyAccountsGamesPanel()
        {
            _contentPanel.SuspendLayout();
            _contentPanel.Visible = false;
            
            try
            {
                while (_contentPanel.Controls.Count > 0)
                {
                    var ctrl = _contentPanel.Controls[0];
                    _contentPanel.Controls.RemoveAt(0);
                    try { ctrl.Dispose(); } catch { }
                }

                // Filtrar jogos pela busca
                var filteredGames = _games.AsEnumerable();
                if (!string.IsNullOrEmpty(_searchText))
                {
                    filteredGames = filteredGames.Where(g => g.Name.ToLower().Contains(_searchText));
                }

                var sortedGames = filteredGames.OrderBy(g => g.Name).ToList();

                foreach (var game in sortedGames)
                {
                    var gamePanel = CreateEmptyAccountsGamePanel(game);
                    _contentPanel.Controls.Add(gamePanel);
                }
            }
            finally
            {
                _contentPanel.Visible = true;
                _contentPanel.ResumeLayout(true);
            }
        }

        /// <summary>
        /// Cria painel de jogo para o modo CONTAS VAZIAS
        /// </summary>
        private Panel CreateEmptyAccountsGamePanel(SupabaseGame game)
        {
            int width = _contentPanel.Width - 25;
            
            var panel = new Panel
            {
                Size = new Size(width, 32),
                BackColor = Color.FromArgb(50, 50, 50),
                Margin = new Padding(0, 2, 0, 2),
                Cursor = Cursors.Hand,
                Tag = game
            };

            var nameLabel = new Label
            {
                Text = game.Name.ToUpper(),
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                ForeColor = Color.Orange,
                Location = new Point(8, 7),
                Size = new Size(width - 20, 20),
                AutoEllipsis = true,
                Cursor = Cursors.Hand
            };

            panel.Controls.Add(nameLabel);

            // Click para ver contas vazias deste jogo
            Action<object, EventArgs> onClick = (s, e) => _ = LoadEmptyAccountsForGameAsync(game);
            panel.Click += (s, e) => onClick(s, e);
            nameLabel.Click += (s, e) => onClick(s, e);

            return panel;
        }

        /// <summary>
        /// Carrega as contas vazias (sem estoque ou estoque zero) para um jogo específico
        /// </summary>
        private async Task LoadEmptyAccountsForGameAsync(SupabaseGame game)
        {
            _emptyAccountsSelectedGame = game;
            _titleLabel.Text = game.Name.ToUpper();
            
            // Limpar busca
            _searchBox.Text = "procurar...";
            _searchBox.ForeColor = Color.Gray;
            _searchText = "";

            _contentPanel.SuspendLayout();
            _contentPanel.Visible = false;
            _contentPanel.Controls.Clear();

            try
            {
                // Mostrar loading
                var loadingLabel = new Label
                {
                    Text = "Carregando...",
                    Font = new Font("Segoe UI", 9F),
                    ForeColor = Color.Gray,
                    AutoSize = true,
                    Padding = new Padding(10)
                };
                _contentPanel.Controls.Add(loadingLabel);
                _contentPanel.Visible = true;
                _contentPanel.ResumeLayout(true);

                // Garantir que _allAccounts esteja carregado
                if (_allAccounts == null || _allAccounts.Count == 0)
                {
                    _allAccounts = await SupabaseManager.Instance.GetAccountsAsync() ?? new List<SupabaseAccount>();
                }

                // Buscar inventário deste jogo
                var inventory = await SupabaseManager.Instance.GetInventoryByGameIdAsync(game.Id) ?? new List<SupabaseInventoryEntry>();
                
                OnLogMessage($"🔍 Debug: {_allAccounts.Count} contas carregadas, {inventory.Count} registros de inventário");
                
                // Encontrar contas que TÊM estoque > 0 neste jogo
                var accountsWithStock = inventory
                    .Where(i => i.Quantity > 0)
                    .Select(i => i.Username.ToLower())
                    .Distinct()
                    .ToHashSet();

                OnLogMessage($"🔍 Debug: {accountsWithStock.Count} contas COM estoque > 0");

                // Contas vazias = todas as contas - contas com estoque
                var emptyAccounts = _allAccounts
                    .Where(a => !accountsWithStock.Contains(a.Username.ToLower()))
                    .OrderBy(a => a.Username)
                    .ToList();

                OnLogMessage($"🔍 Debug: {emptyAccounts.Count} contas SEM estoque");

                // Filtrar pela busca se houver
                if (!string.IsNullOrEmpty(_searchText))
                {
                    emptyAccounts = emptyAccounts
                        .Where(a => a.Username.ToLower().Contains(_searchText))
                        .ToList();
                }

                _contentPanel.SuspendLayout();
                _contentPanel.Controls.Clear();

                if (emptyAccounts.Count == 0)
                {
                    var noAccountsLabel = new Label
                    {
                        Text = "Nenhuma conta vazia neste jogo.\nTodas as contas têm estoque.",
                        Font = new Font("Segoe UI", 8F),
                        ForeColor = Color.Gray,
                        AutoSize = true,
                        Padding = new Padding(10)
                    };
                    _contentPanel.Controls.Add(noAccountsLabel);
                }
                else
                {
                    // Header com contagem
                    var headerLabel = new Label
                    {
                        Text = $"{emptyAccounts.Count} contas sem estoque",
                        Font = new Font("Segoe UI", 8F),
                        ForeColor = Color.Orange,
                        AutoSize = true,
                        Padding = new Padding(5, 3, 5, 3)
                    };
                    _contentPanel.Controls.Add(headerLabel);

                    // Listar contas vazias
                    foreach (var account in emptyAccounts)
                    {
                        var accountPanel = CreateEmptyAccountPanel(account);
                        _contentPanel.Controls.Add(accountPanel);
                    }
                }

                _contentPanel.Visible = true;
                _contentPanel.ResumeLayout(true);
                
                OnLogMessage($"📭 {game.Name}: {emptyAccounts.Count} contas sem estoque");
            }
            catch (Exception ex)
            {
                OnLogError($"Erro ao carregar contas vazias: {ex.Message}");
            }
        }

        /// <summary>
        /// Cria painel de conta vazia (clicável para selecionar)
        /// </summary>
        private Panel CreateEmptyAccountPanel(SupabaseAccount account)
        {
            int width = _contentPanel.Width - 25;
            
            var panel = new Panel
            {
                Size = new Size(width, 24),
                BackColor = Color.FromArgb(55, 45, 35),
                Margin = new Padding(10, 1, 0, 1),
                Cursor = Cursors.Hand,
                Tag = account
            };

            var usernameLabel = new Label
            {
                Text = account.Username,
                Font = new Font("Segoe UI", 8F),
                ForeColor = Color.LightGray,
                Location = new Point(8, 4),
                Size = new Size(width - 20, 16),
                AutoEllipsis = true,
                Cursor = Cursors.Hand
            };

            panel.Controls.Add(usernameLabel);

            // Click esquerdo para selecionar a conta
            Action<object, EventArgs> onClick = (s, e) => OnAccountSelected(account.Username);
            panel.Click += (s, e) => onClick(s, e);
            usernameLabel.Click += (s, e) => onClick(s, e);

            // Menu de contexto (click direito)
            var contextMenu = new ContextMenuStrip();
            contextMenu.BackColor = Color.FromArgb(45, 45, 45);
            contextMenu.ForeColor = Color.White;
            contextMenu.Renderer = new DarkMenuRenderer();

            var newStockItem = new ToolStripMenuItem("📦 Novo Estoque");
            newStockItem.Click += (s, e) => ShowNewStockDialog(account);
            
            var archiveItem = new ToolStripMenuItem("📁 Arquivar Conta");
            archiveItem.Click += (s, e) => ShowArchiveConfirmation(account);

            contextMenu.Items.Add(newStockItem);
            contextMenu.Items.Add(archiveItem);

            panel.ContextMenuStrip = contextMenu;
            usernameLabel.ContextMenuStrip = contextMenu;

            // Hover effect
            panel.MouseEnter += (s, e) => panel.BackColor = Color.FromArgb(70, 55, 40);
            panel.MouseLeave += (s, e) => panel.BackColor = Color.FromArgb(55, 45, 35);
            usernameLabel.MouseEnter += (s, e) => panel.BackColor = Color.FromArgb(70, 55, 40);
            usernameLabel.MouseLeave += (s, e) => panel.BackColor = Color.FromArgb(55, 45, 35);

            return panel;
        }

        /// <summary>
        /// Mostra diálogo para adicionar novo estoque à conta
        /// </summary>
        private void ShowNewStockDialog(SupabaseAccount account)
        {
            if (_emptyAccountsSelectedGame == null) return;

            // Criar form de diálogo
            var form = new Form
            {
                Text = "Novo Estoque",
                Size = new Size(320, 200),
                StartPosition = FormStartPosition.CenterParent,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                MaximizeBox = false,
                MinimizeBox = false,
                BackColor = Color.FromArgb(40, 40, 40)
            };

            // Label conta
            var accountLabel = new Label
            {
                Text = $"Conta: {account.Username}",
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                ForeColor = Color.White,
                Location = new Point(15, 15),
                AutoSize = true
            };

            // Label Item
            var itemLabel = new Label
            {
                Text = "Item:",
                ForeColor = Color.LightGray,
                Location = new Point(15, 45),
                AutoSize = true
            };

            // ComboBox de itens
            var itemCombo = new ComboBox
            {
                Location = new Point(15, 65),
                Size = new Size(270, 25),
                DropDownStyle = ComboBoxStyle.DropDownList,
                BackColor = Color.FromArgb(50, 50, 50),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat
            };

            // Carregar itens do jogo selecionado
            _ = LoadItemsForComboAsync(itemCombo, _emptyAccountsSelectedGame.Id);

            // Label Quantidade
            var qtyLabel = new Label
            {
                Text = "Quantidade:",
                ForeColor = Color.LightGray,
                Location = new Point(15, 95),
                AutoSize = true
            };

            // TextBox de quantidade
            var qtyTextBox = new TextBox
            {
                Location = new Point(15, 115),
                Size = new Size(100, 25),
                BackColor = Color.FromArgb(50, 50, 50),
                ForeColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle,
                Text = "1"
            };

            // Botão Salvar
            var saveBtn = new Button
            {
                Text = "Salvar",
                Location = new Point(120, 115),
                Size = new Size(80, 25),
                BackColor = Color.FromArgb(0, 120, 0),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand
            };
            saveBtn.FlatAppearance.BorderSize = 0;
            saveBtn.Click += async (s, e) =>
            {
                if (itemCombo.SelectedItem == null)
                {
                    MessageBox.Show("Selecione um item.", "Aviso", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                if (!int.TryParse(qtyTextBox.Text, out int qty) || qty <= 0)
                {
                    MessageBox.Show("Digite uma quantidade válida.", "Aviso", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                var selectedItem = (ComboBoxItem)itemCombo.SelectedItem;
                
                // Salvar no Supabase
                var success = await SupabaseManager.Instance.UpsertInventoryAsync(account.Username, selectedItem.Id, qty);
                
                if (success)
                {
                    OnLogMessage($"✅ Estoque adicionado: {account.Username} - {selectedItem.Name} x{qty}");
                    form.DialogResult = DialogResult.OK;
                    form.Close();
                    
                    // Recarregar lista de contas vazias
                    _ = LoadEmptyAccountsForGameAsync(_emptyAccountsSelectedGame);
                }
                else
                {
                    MessageBox.Show("Erro ao salvar estoque.", "Erro", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            };

            // Botão Cancelar
            var cancelBtn = new Button
            {
                Text = "Cancelar",
                Location = new Point(205, 115),
                Size = new Size(80, 25),
                BackColor = Color.FromArgb(80, 80, 80),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand
            };
            cancelBtn.FlatAppearance.BorderSize = 0;
            cancelBtn.Click += (s, e) => form.Close();

            form.Controls.AddRange(new Control[] { accountLabel, itemLabel, itemCombo, qtyLabel, qtyTextBox, saveBtn, cancelBtn });
            form.ShowDialog();
        }

        /// <summary>
        /// Carrega itens do jogo para o ComboBox
        /// </summary>
        private async Task LoadItemsForComboAsync(ComboBox combo, int gameId)
        {
            var items = await SupabaseManager.Instance.GetGameItemsAsync(gameId);
            if (items != null && items.Count > 0)
            {
                combo.Items.Clear();
                foreach (var item in items.OrderBy(i => i.Name))
                {
                    combo.Items.Add(new ComboBoxItem { Id = item.Id, Name = item.Name });
                }
                combo.SelectedIndex = 0;
            }
        }

        /// <summary>
        /// Mostra confirmação para arquivar conta
        /// </summary>
        private void ShowArchiveConfirmation(SupabaseAccount account)
        {
            var result = MessageBox.Show(
                $"VOCÊ TEM CERTEZA QUE QUER ARQUIVAR A CONTA '{account.Username}'?\n\nContas arquivadas não aparecem na listagem de contas vazias.",
                "Arquivar Conta",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);

            if (result == DialogResult.Yes)
            {
                _ = ArchiveAccountAsync(account);
            }
        }

        /// <summary>
        /// Arquiva uma conta a partir do painel de inventário
        /// Remove do inventário e arquiva a conta
        /// </summary>
        private void ArchiveAccountFromInventory(SupabaseGameItem item, SupabaseInventoryEntry inventory)
        {
            var result = MessageBox.Show(
                $"VOCÊ TEM CERTEZA QUE QUER ARQUIVAR A CONTA '{inventory.Username}'?\n\n" +
                $"Isso irá:\n" +
                $"• Remover o estoque do item '{item.Name}'\n" +
                $"• Arquivar a conta (não aparecerá mais nas listagens)",
                "Arquivar Conta",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning);

            if (result == DialogResult.Yes)
            {
                _ = ArchiveAccountFromInventoryAsync(item, inventory);
            }
        }

        /// <summary>
        /// Arquiva uma conta do inventário (remove estoque e arquiva)
        /// </summary>
        private async Task ArchiveAccountFromInventoryAsync(SupabaseGameItem item, SupabaseInventoryEntry inventory)
        {
            try
            {
                // 1. Remover do inventário
                var deleteSuccess = await SupabaseManager.Instance.DeleteInventoryAsync(inventory.Id);
                if (!deleteSuccess)
                {
                    MessageBox.Show("Erro ao remover estoque.", "Erro", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                // 2. Arquivar a conta
                var archiveSuccess = await SupabaseManager.Instance.ArchiveAccountAsync(inventory.Username, true);
                if (!archiveSuccess)
                {
                    MessageBox.Show("Estoque removido, mas erro ao arquivar conta.", "Aviso", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }

                OnLogMessage($"📁 Conta arquivada: {inventory.Username} (removido de {item.Name})");

                // 3. Atualizar cache local
                if (_inventoryByItem.ContainsKey(item.Id))
                {
                    _inventoryByItem[item.Id].RemoveAll(i => i.Id == inventory.Id);
                }

                // 4. Atualizar UI
                RefreshItemsPanel();
            }
            catch (Exception ex)
            {
                OnLogError($"Erro ao arquivar conta: {ex.Message}");
                MessageBox.Show($"Erro: {ex.Message}", "Erro", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        /// <summary>
        /// Arquiva uma conta no Supabase
        /// </summary>
        private async Task ArchiveAccountAsync(SupabaseAccount account)
        {
            var success = await SupabaseManager.Instance.ArchiveAccountAsync(account.Username, true);
            
            if (success)
            {
                OnLogMessage($"📁 Conta arquivada: {account.Username}");
                
                // Remover da lista local
                _allAccounts.RemoveAll(a => a.Username == account.Username);
                
                // Recarregar lista se estiver em um jogo específico
                if (_emptyAccountsSelectedGame != null)
                {
                    _ = LoadEmptyAccountsForGameAsync(_emptyAccountsSelectedGame);
                }
            }
            else
            {
                MessageBox.Show("Erro ao arquivar conta.", "Erro", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        /// <summary>
        /// Move conta para "Contas Vazias" (remove estoque do item atual)
        /// </summary>
        private void MoveToEmptyAccounts(SupabaseGameItem item, SupabaseInventoryEntry inventory)
        {
            var result = MessageBox.Show(
                $"Mover '{inventory.Username}' para Contas Vazias?\n\nIsso irá remover o estoque de '{item.Name}' desta conta.",
                "Mover para Contas Vazias",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);

            if (result == DialogResult.Yes)
            {
                _ = MoveToEmptyAccountsAsync(item, inventory);
            }
        }

        /// <summary>
        /// Remove o estoque e atualiza a UI
        /// </summary>
        private async Task MoveToEmptyAccountsAsync(SupabaseGameItem item, SupabaseInventoryEntry inventory)
        {
            try
            {
                // Deletar o registro de inventário (zera o estoque)
                var success = await SupabaseManager.Instance.DeleteInventoryAsync(inventory.Id);
                
                if (success)
                {
                    OnLogMessage($"📤 {inventory.Username} movido para Contas Vazias (removido de {item.Name})");
                    
                    // Atualizar cache local
                    if (_inventoryByItem.ContainsKey(item.Id))
                    {
                        _inventoryByItem[item.Id].RemoveAll(i => i.Id == inventory.Id);
                    }
                    
                    // Recarregar a UI
                    RefreshItemsPanel();
                }
                else
                {
                    MessageBox.Show("Erro ao mover conta.", "Erro", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
            catch (Exception ex)
            {
                OnLogError($"Erro ao mover conta: {ex.Message}");
            }
        }

        /// <summary>
        /// Mostra diálogo para alterar o item da conta
        /// </summary>
        private void ShowChangeItemDialog(SupabaseGameItem currentItem, SupabaseInventoryEntry inventory)
        {
            if (_selectedGame == null) return;

            // Criar form de diálogo
            var form = new Form
            {
                Text = "Alterar Item",
                Size = new Size(320, 200),
                StartPosition = FormStartPosition.CenterParent,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                MaximizeBox = false,
                MinimizeBox = false,
                BackColor = Color.FromArgb(40, 40, 40)
            };

            // Label conta
            var accountLabel = new Label
            {
                Text = $"Conta: {inventory.Username}",
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                ForeColor = Color.White,
                Location = new Point(15, 15),
                AutoSize = true
            };

            // Label item atual
            var currentLabel = new Label
            {
                Text = $"Item atual: {currentItem.Name}",
                ForeColor = Color.Gray,
                Location = new Point(15, 38),
                AutoSize = true
            };

            // Label Novo Item
            var newItemLabel = new Label
            {
                Text = "Novo Item:",
                ForeColor = Color.LightGray,
                Location = new Point(15, 65),
                AutoSize = true
            };

            // ComboBox de itens
            var itemCombo = new ComboBox
            {
                Location = new Point(15, 85),
                Size = new Size(270, 25),
                DropDownStyle = ComboBoxStyle.DropDownList,
                BackColor = Color.FromArgb(50, 50, 50),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat
            };

            // Carregar itens do jogo (exceto o atual)
            _ = LoadItemsForChangeAsync(itemCombo, _selectedGame.Id, currentItem.Id);

            // Botão Salvar
            var saveBtn = new Button
            {
                Text = "Alterar",
                Location = new Point(120, 120),
                Size = new Size(80, 25),
                BackColor = Color.FromArgb(0, 120, 0),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand
            };
            saveBtn.FlatAppearance.BorderSize = 0;
            saveBtn.Click += async (s, e) =>
            {
                if (itemCombo.SelectedItem == null)
                {
                    MessageBox.Show("Selecione um item.", "Aviso", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                var newItem = (ComboBoxItem)itemCombo.SelectedItem;
                
                // Mover para novo item: deletar do antigo e criar no novo
                var deleteSuccess = await SupabaseManager.Instance.DeleteInventoryAsync(inventory.Id);
                if (deleteSuccess)
                {
                    var upsertSuccess = await SupabaseManager.Instance.UpsertInventoryAsync(
                        inventory.Username, newItem.Id, inventory.Quantity);
                    
                    if (upsertSuccess)
                    {
                        OnLogMessage($"🔄 {inventory.Username}: {currentItem.Name} → {newItem.Name}");
                        form.DialogResult = DialogResult.OK;
                        form.Close();
                        
                        // Recarregar itens do jogo
                        if (_selectedGame != null)
                        {
                            _ = LoadGameItemsAsync(_selectedGame);
                        }
                    }
                    else
                    {
                        // Tentar restaurar o original
                        await SupabaseManager.Instance.UpsertInventoryAsync(
                            inventory.Username, currentItem.Id, inventory.Quantity);
                        MessageBox.Show("Erro ao criar no novo item.", "Erro", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                }
                else
                {
                    MessageBox.Show("Erro ao remover do item atual.", "Erro", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            };

            // Botão Cancelar
            var cancelBtn = new Button
            {
                Text = "Cancelar",
                Location = new Point(205, 120),
                Size = new Size(80, 25),
                BackColor = Color.FromArgb(80, 80, 80),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand
            };
            cancelBtn.FlatAppearance.BorderSize = 0;
            cancelBtn.Click += (s, e) => form.Close();

            form.Controls.AddRange(new Control[] { accountLabel, currentLabel, newItemLabel, itemCombo, saveBtn, cancelBtn });
            form.ShowDialog();
        }

        /// <summary>
        /// Carrega itens do jogo para o ComboBox de alteração (excluindo o item atual)
        /// </summary>
        private async Task LoadItemsForChangeAsync(ComboBox combo, int gameId, int excludeItemId)
        {
            var items = await SupabaseManager.Instance.GetGameItemsAsync(gameId);
            if (items != null && items.Count > 0)
            {
                combo.Items.Clear();
                foreach (var item in items.Where(i => i.Id != excludeItemId).OrderBy(i => i.Name))
                {
                    combo.Items.Add(new ComboBoxItem { Id = item.Id, Name = item.Name });
                }
                if (combo.Items.Count > 0)
                {
                    combo.SelectedIndex = 0;
                }
            }
        }

        #region Game/Item Edit/Archive

        /// <summary>
        /// Mostra dialog para editar nome do jogo
        /// </summary>
        private void ShowEditGameDialog(SupabaseGame game)
        {
            string newName = ShowInputDialog($"Novo nome para '{game.Name}':", "Editar Jogo", game.Name);
            
            if (!string.IsNullOrWhiteSpace(newName) && newName != game.Name)
            {
                _ = UpdateGameNameAsync(game, newName);
            }
        }

        /// <summary>
        /// Atualiza o nome do jogo no Supabase
        /// </summary>
        private async Task UpdateGameNameAsync(SupabaseGame game, string newName)
        {
            var success = await SupabaseManager.Instance.UpdateGameNameAsync(game.Id, newName);
            
            if (success)
            {
                OnLogMessage($"✏️ Jogo renomeado: {game.Name} → {newName}");
                game.Name = newName;
                
                // Recarregar lista de jogos
                await LoadGamesAsync();
            }
            else
            {
                MessageBox.Show("Erro ao renomear jogo.", "Erro", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        /// <summary>
        /// Mostra confirmação para arquivar jogo
        /// </summary>
        private void ShowArchiveGameConfirmation(SupabaseGame game)
        {
            var result = MessageBox.Show(
                $"VOCÊ TEM CERTEZA QUE QUER ARQUIVAR O JOGO '{game.Name}'?\n\n" +
                $"⚠️ Isso irá:\n" +
                $"• Ocultar o jogo da listagem\n" +
                $"• Manter todos os dados (itens, contas, estoque)\n\n" +
                $"O jogo pode ser restaurado posteriormente no banco de dados.",
                "Arquivar Jogo",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning);

            if (result == DialogResult.Yes)
            {
                _ = ArchiveGameAsync(game);
            }
        }

        /// <summary>
        /// Arquiva um jogo no Supabase
        /// </summary>
        private async Task ArchiveGameAsync(SupabaseGame game)
        {
            var success = await SupabaseManager.Instance.ArchiveGameAsync(game.Id, true);
            
            if (success)
            {
                OnLogMessage($"📁 Jogo arquivado: {game.Name}");
                
                // Remover dos favoritos se estiver lá
                if (_favoriteGames.Contains(game.Id))
                {
                    _favoriteGames.Remove(game.Id);
                    SaveFavorites();
                }
                
                // Recarregar lista de jogos
                await LoadGamesAsync();
            }
            else
            {
                MessageBox.Show("Erro ao arquivar jogo.", "Erro", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        /// <summary>
        /// Mostra dialog para editar nome do item
        /// </summary>
        private void ShowEditItemDialog(SupabaseGameItem item)
        {
            string newName = ShowInputDialog($"Novo nome para '{item.Name}':", "Editar Item", item.Name);
            
            if (!string.IsNullOrWhiteSpace(newName) && newName != item.Name)
            {
                _ = UpdateItemNameAsync(item, newName);
            }
        }

        /// <summary>
        /// Atualiza o nome do item no Supabase
        /// </summary>
        private async Task UpdateItemNameAsync(SupabaseGameItem item, string newName)
        {
            var success = await SupabaseManager.Instance.UpdateGameItemNameAsync(item.Id, newName);
            
            if (success)
            {
                OnLogMessage($"✏️ Item renomeado: {item.Name} → {newName}");
                item.Name = newName;
                
                // Atualizar na lista local
                var localItem = _gameItems.FirstOrDefault(i => i.Id == item.Id);
                if (localItem != null)
                {
                    localItem.Name = newName;
                }
                
                // Recarregar painel de itens
                RefreshItemsPanel();
            }
            else
            {
                MessageBox.Show("Erro ao renomear item.", "Erro", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        /// <summary>
        /// Mostra confirmação para arquivar item
        /// </summary>
        private void ShowArchiveItemConfirmation(SupabaseGameItem item)
        {
            var result = MessageBox.Show(
                $"VOCÊ TEM CERTEZA QUE QUER ARQUIVAR O ITEM '{item.Name}'?\n\n" +
                $"⚠️ Isso irá:\n" +
                $"• Ocultar o item da listagem\n" +
                $"• Manter todos os dados (contas, estoque)\n\n" +
                $"O item pode ser restaurado posteriormente no banco de dados.",
                "Arquivar Item",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning);

            if (result == DialogResult.Yes)
            {
                _ = ArchiveItemAsync(item);
            }
        }

        /// <summary>
        /// Arquiva um item no Supabase
        /// </summary>
        private async Task ArchiveItemAsync(SupabaseGameItem item)
        {
            var success = await SupabaseManager.Instance.ArchiveGameItemAsync(item.Id, true);
            
            if (success)
            {
                OnLogMessage($"📁 Item arquivado: {item.Name}");
                
                // Remover da lista local
                _gameItems.RemoveAll(i => i.Id == item.Id);
                
                // Remover do cache de inventário
                if (_inventoryByItem.ContainsKey(item.Id))
                {
                    _inventoryByItem.Remove(item.Id);
                }
                
                // Remover dos expandidos
                if (_expandedItems.ContainsKey(item.Id))
                {
                    _expandedItems.Remove(item.Id);
                }
                
                // Recarregar painel de itens
                RefreshItemsPanel();
            }
            else
            {
                MessageBox.Show("Erro ao arquivar item.", "Erro", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        #endregion

        #endregion
    }

    /// <summary>
    /// Item para ComboBox com ID e Nome
    /// </summary>
    internal class ComboBoxItem
    {
        public int Id { get; set; }
        public string Name { get; set; }
        
        public override string ToString() => Name;
    }

    /// <summary>
    /// Renderer para menu de contexto com tema escuro
    /// </summary>
    internal class DarkMenuRenderer : ToolStripProfessionalRenderer
    {
        public DarkMenuRenderer() : base(new DarkColorTable()) { }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = Color.White;
            base.OnRenderItemText(e);
        }
    }

    internal class DarkColorTable : ProfessionalColorTable
    {
        public override Color MenuItemSelected => Color.FromArgb(60, 60, 60);
        public override Color MenuItemSelectedGradientBegin => Color.FromArgb(60, 60, 60);
        public override Color MenuItemSelectedGradientEnd => Color.FromArgb(60, 60, 60);
        public override Color MenuBorder => Color.FromArgb(70, 70, 70);
        public override Color MenuItemBorder => Color.FromArgb(70, 70, 70);
        public override Color ImageMarginGradientBegin => Color.FromArgb(45, 45, 45);
        public override Color ImageMarginGradientMiddle => Color.FromArgb(45, 45, 45);
        public override Color ImageMarginGradientEnd => Color.FromArgb(45, 45, 45);
        public override Color ToolStripDropDownBackground => Color.FromArgb(45, 45, 45);
    }

    /// <summary>
    /// Button que suprime ObjectDisposedException causado por mensagens de mouse
    /// que chegam após o controle ter sido descartado pelo RefreshItemsPanel.
    /// 
    /// IMPORTANTE: Só suprime mensagens de MOUSE em controles descartados.
    /// Todas as outras mensagens (WM_DESTROY, WM_NCDESTROY, etc.) passam normalmente
    /// para que o HWND seja destruído corretamente e não "fantasme" sobre novos controles.
    /// </summary>
    internal class SafeButton : Button
    {
        private const int WM_LBUTTONDOWN = 0x0201;
        private const int WM_LBUTTONUP = 0x0202;
        private const int WM_LBUTTONDBLCLK = 0x0203;
        private const int WM_RBUTTONDOWN = 0x0204;
        private const int WM_RBUTTONUP = 0x0205;
        private const int WM_MBUTTONDOWN = 0x0207;
        private const int WM_MBUTTONUP = 0x0208;
        private const int WM_MOUSEMOVE = 0x0200;
        
        protected override void WndProc(ref Message m)
        {
            // Se descartado, só ignorar mensagens de mouse (que causam o crash).
            // Deixar todas as outras passarem para limpeza correta do HWND.
            if (IsDisposed)
            {
                switch (m.Msg)
                {
                    case WM_LBUTTONDOWN:
                    case WM_LBUTTONUP:
                    case WM_LBUTTONDBLCLK:
                    case WM_RBUTTONDOWN:
                    case WM_RBUTTONUP:
                    case WM_MBUTTONDOWN:
                    case WM_MBUTTONUP:
                    case WM_MOUSEMOVE:
                        return; // Ignorar silenciosamente
                }
            }
            
            try
            {
                base.WndProc(ref m);
            }
            catch (ObjectDisposedException) { }
        }
    }
}

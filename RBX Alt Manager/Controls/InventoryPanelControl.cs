using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Newtonsoft.Json;
using RBX_Alt_Manager.Classes;
using RBX_Alt_Manager.Forms;

namespace RBX_Alt_Manager.Controls
{
    /// <summary>
    /// Controle do painel de inventário usando Supabase
    /// Hierarquia: Jogos → Itens → Contas (expandíveis)
    /// </summary>
    public partial class InventoryPanelControl : UserControl
    {
        // P/Invoke para placeholder nativo do TextBox
        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, string lParam);
        private const int EM_SETCUEBANNER = 0x1501;

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
        private List<SupabaseAccount> _cachedEmptyAccounts = null;
        private bool _suppressSearchFilter = false;

        // Favoritos
        private HashSet<int> _favoriteGames = new HashSet<int>();
        private string _favoritesFilePath;
        
        // Busca
        private string _searchText = "";
        private TextBox _searchBox;
        private Panel _searchPanel;
        private System.Windows.Forms.Timer _searchDebounceTimer;
        
        // Guarda de re-entrância para evitar RefreshItemsPanel ser chamado recursivamente
        private bool _isRefreshingItems = false;

        // Cooldown para bloquear refreshes externos (Pusher/Realtime) após ação do usuário
        private DateTime _lastUserActionTime = DateTime.MinValue;
        private const int USER_ACTION_COOLDOWN_SECONDS = 5;

        private static readonly Random _rng = new Random();

        // Fonts reutilizáveis (evita criar novos objetos Font por controle)
        private static readonly Font F7 = new Font("Segoe UI", 7F);
        private static readonly Font F7_5B = new Font("Segoe UI", 7.5F, FontStyle.Bold);
        private static readonly Font F8 = new Font("Segoe UI", 8F);
        private static readonly Font F8B = new Font("Segoe UI", 8F, FontStyle.Bold);
        private static readonly Font F8_5 = new Font("Segoe UI", 8.5F);
        private static readonly Font F9 = new Font("Segoe UI", 9F);
        private static readonly Font F9B = new Font("Segoe UI", 9F, FontStyle.Bold);
        private static readonly Font F12 = new Font("Segoe UI", 12F);

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
            // Ignorar updates externos durante edição local
            if ((DateTime.UtcNow - _lastUserActionTime).TotalSeconds < USER_ACTION_COOLDOWN_SECONDS)
                return;

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

            // Cooldown: não recarregar se o usuário acabou de fazer uma ação manual
            if ((DateTime.UtcNow - _lastUserActionTime).TotalSeconds < USER_ACTION_COOLDOWN_SECONDS)
            {
                LogDebug($"[DEBUG] RefreshIfCurrentGameItem IGNORADO (cooldown {USER_ACTION_COOLDOWN_SECONDS}s após ação do usuário)");
                return;
            }

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

            // Cooldown: não recarregar se o usuário acabou de fazer uma ação manual
            if ((DateTime.UtcNow - _lastUserActionTime).TotalSeconds < USER_ACTION_COOLDOWN_SECONDS)
            {
                LogDebug($"[DEBUG] RefreshIfCurrentGame IGNORADO (cooldown {USER_ACTION_COOLDOWN_SECONDS}s após ação do usuário)");
                return;
            }

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

            // Atualizar o label de quantidade e cor do username no painel da conta (se estiver expandido)
            foreach (Control c in _contentPanel.Controls)
            {
                if (c is Panel p && p.Tag is SupabaseInventoryEntry inv && inv.Id == inventoryId)
                {
                    foreach (Control child in p.Controls)
                    {
                        if (child is Label lbl && lbl.Name == $"invqty_{inventoryId}")
                        {
                            lbl.Text = newQuantity.ToString("N0", new System.Globalization.CultureInfo("pt-BR"));
                            lbl.ForeColor = newQuantity > 0 ? ThemeEditor.FormsForeground : Color.FromArgb(230, 100, 100);
                        }
                        else if (child is Label userLbl && userLbl.Name == $"invuser_{inventoryId}")
                        {
                            userLbl.ForeColor = newQuantity > 0 ? Color.White : Color.FromArgb(230, 100, 100);
                        }
                    }
                    break;
                }
            }

            // Atualizar o header do item (total, cores)
            RefreshItemHeaderInfo(itemId);
        }

        private void RefreshItemHeaderInfo(int itemId)
        {
            if (!_inventoryByItem.ContainsKey(itemId) || _contentPanel == null) return;

            var inventory = _inventoryByItem[itemId];
            long totalQty = inventory.Sum(i => i.Quantity);

            // Cor baseada no estoque: >5 verde, 1-5 laranja, 0 vermelho
            Color stockColor;
            if (totalQty > 5)
                stockColor = Color.FromArgb(80, 200, 80);   // Verde
            else if (totalQty > 0)
                stockColor = Color.FromArgb(230, 160, 50);   // Laranja
            else
                stockColor = Color.FromArgb(200, 80, 80);    // Vermelho

            string formattedTotal = totalQty.ToString("N0", new System.Globalization.CultureInfo("pt-BR"));

            // Encontrar o painel do item e atualizar info + cores
            foreach (Control c in _contentPanel.Controls)
            {
                if (c is Panel p && p.Tag is SupabaseGameItem item && item.Id == itemId)
                {
                    foreach (Control child in p.Controls)
                    {
                        if (child is Label lbl)
                        {
                            if (lbl.Name == $"iteminfo_{itemId}")
                            {
                                lbl.Text = $"({formattedTotal})";
                                lbl.ForeColor = stockColor;
                            }
                            else if (lbl.Name == $"itemname_{itemId}")
                            {
                                lbl.ForeColor = stockColor;
                            }
                            else if (lbl.Name == $"itemarrow_{itemId}")
                            {
                                lbl.ForeColor = stockColor;
                            }
                        }
                    }
                    break;
                }
            }
        }

        private void InitializeComponent()
        {
            this.SuspendLayout();
            this.BackColor = ThemeEditor.FormsBackground;
            this.Size = new Size(310, 682);

            // Header Panel
            _headerPanel = new Panel
            {
                Dock = DockStyle.Top,
                Height = 35,
                BackColor = ThemeEditor.HeaderBackground,
                Padding = new Padding(3)
            };

            // Botão Voltar (oculto inicialmente)
            _backButton = new Button
            {
                Text = "← Voltar",
                Font = F7,
                ForeColor = ThemeEditor.ButtonsForeground,
                BackColor = ThemeEditor.ButtonsBackground,
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
                Font = F9B,
                ForeColor = ThemeEditor.FormsForeground,
                Location = new Point(5, 8),
                AutoSize = true
            };

            // Botão Adicionar (Jogo ou Item dependendo do contexto)
            _addButton = new Button
            {
                Text = "+ Jogo",
                Font = F7,
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
                Font = F8,
                ForeColor = ThemeEditor.ButtonsForeground,
                BackColor = ThemeEditor.ButtonsBackground,
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
                Height = 30,
                BackColor = ThemeEditor.HeaderBackground,
                Padding = new Padding(6, 5, 6, 5)
            };

            // Borda sutil ao redor do TextBox
            var searchBorder = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = ThemeEditor.TextBoxesBorder,
                Padding = new Padding(1)
            };

            _searchBox = new TextBox
            {
                Font = F8_5,
                ForeColor = ThemeEditor.TextBoxesForeground,
                BackColor = ThemeEditor.TextBoxesBackground,
                BorderStyle = BorderStyle.None,
                Dock = DockStyle.Fill
            };
            _searchBox.Leave += SearchBox_Leave;
            _searchBox.TextChanged += SearchBox_TextChanged;
            _searchBox.KeyDown += SearchBox_KeyDown;

            // Placeholder nativo do Windows (desaparece ao focar)
            _searchBox.HandleCreated += (s, ev) =>
            {
                SendMessage(_searchBox.Handle, EM_SETCUEBANNER, (IntPtr)1, "procurar...");
            };

            searchBorder.Controls.Add(_searchBox);
            _searchPanel.Controls.Add(searchBorder);

            // Content Panel
            _contentPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                BackColor = ThemeEditor.FormsBackground,
                Padding = new Padding(3)
            };

            this.Controls.Add(_contentPanel);
            this.Controls.Add(_searchPanel);
            this.Controls.Add(_headerPanel);

            this.Resize += (s, e) => UpdateLayout();

            this.ResumeLayout(false);
        }

        /// <summary>
        /// Reaplica cores do ThemeEditor em todos os controles persistentes e reconstrói o painel de conteúdo.
        /// </summary>
        public void ApplyTheme()
        {
            // Controles persistentes
            this.BackColor = ThemeEditor.FormsBackground;
            _headerPanel.BackColor = ThemeEditor.HeaderBackground;
            _backButton.ForeColor = ThemeEditor.ButtonsForeground;
            _backButton.BackColor = ThemeEditor.ButtonsBackground;
            _titleLabel.ForeColor = ThemeEditor.FormsForeground;
            _refreshButton.ForeColor = ThemeEditor.ButtonsForeground;
            _refreshButton.BackColor = ThemeEditor.ButtonsBackground;

            _searchPanel.BackColor = ThemeEditor.HeaderBackground;
            if (_searchPanel.Controls.Count > 0 && _searchPanel.Controls[0] is Panel searchBorder)
                searchBorder.BackColor = ThemeEditor.TextBoxesBorder;
            _searchBox.ForeColor = ThemeEditor.TextBoxesForeground;
            _searchBox.BackColor = ThemeEditor.TextBoxesBackground;

            _contentPanel.BackColor = ThemeEditor.FormsBackground;

            // Reconstruir painéis de conteúdo com as novas cores
            if (_selectedGame != null)
                RefreshItemsPanel();
            else
                RefreshGamesPanel();
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
            _cachedEmptyAccounts = null;
            
            // Limpar busca ao voltar para jogos (sem disparar TextChanged → ApplyFilter)
            _suppressSearchFilter = true;
            _searchBox.Text = "";
            _searchText = "";
            _suppressSearchFilter = false;

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
                BackColor = ThemeEditor.PanelBackground,
                Margin = new Padding(0, 2, 0, 2),
                Cursor = Cursors.Hand,
                Tag = game
            };

            var nameLabel = new Label
            {
                Text = game.Name.ToUpper(),
                Font = F9B,
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
                Font = F12,
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
            contextMenu.BackColor = ThemeEditor.ItemBackground;
            contextMenu.ForeColor = ThemeEditor.FormsForeground;
            contextMenu.Renderer = new DarkMenuRenderer();

            var editGameItem = new ToolStripMenuItem("✏️ Editar Jogo");
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
            LogDebug($"[DEBUG] LoadGameItemsAsync CHAMADO para '{game?.Name}' (stack: {new System.Diagnostics.StackTrace(1, false).GetFrame(0)?.GetMethod()?.Name ?? "?"})");
            _selectedGame = game;
            _backButton.Visible = true;
            _titleLabel.Text = game.Name.ToUpper();
            _titleLabel.Location = new Point(60, 8);
            _addButton.Text = "+ Item";
            _addButton.Visible = true;
            
            // Limpar busca ao entrar em um jogo
            _searchBox.Text = "";
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
                    Font = F9,
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
                        Font = F8,
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

        private int _refreshCount = 0;

        private void RefreshItemsPanel()
        {
            // Guarda de re-entrância: ctrl.Dispose() pode bombar mensagens do Windows
            // que re-entram neste método. Sem esta guarda, o painel pode ficar invisível.
            if (_isRefreshingItems)
            {
                LogDebugWarning($"[DEBUG] RefreshItemsPanel BLOQUEADO por _isRefreshingItems=true");
                return;
            }
            _isRefreshingItems = true;
            _refreshCount++;
            int thisRefresh = _refreshCount;

            // Stack trace para identificar quem está chamando
            var st = new System.Diagnostics.StackTrace(1, false);
            var callers = new System.Text.StringBuilder();
            for (int i = 0; i < Math.Min(st.FrameCount, 5); i++)
            {
                var frame = st.GetFrame(i);
                var method = frame?.GetMethod();
                if (method != null)
                {
                    if (callers.Length > 0) callers.Append(" → ");
                    callers.Append($"{method.DeclaringType?.Name}.{method.Name}");
                }
            }
            LogDebug($"[DEBUG] RefreshItemsPanel #{thisRefresh} INICIADO. Controls antigos: {_contentPanel.Controls.Count}. CALLER: {callers}");

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
                LogDebug($"[DEBUG] RefreshItemsPanel #{thisRefresh} recriou {controlsToAdd.Count} controles");
            }
            finally
            {
                // GARANTIR que o painel sempre volte a ficar visível e com layout ativo,
                // independente de qualquer exceção durante a reconstrução
                _contentPanel.Visible = true;
                _contentPanel.ResumeLayout(true);
                _isRefreshingItems = false;
                LogDebug($"[DEBUG] RefreshItemsPanel #{thisRefresh} CONCLUÍDO. Controls finais: {_contentPanel.Controls.Count}, Visible={_contentPanel.Visible}");
            }
        }

        private Panel CreateItemHeaderPanel(SupabaseGameItem item, List<SupabaseInventoryEntry> inventory, int width, bool isExpanded)
        {
            long totalQty = inventory.Sum(i => i.Quantity);

            // Cor baseada no estoque: >5 verde, 1-5 laranja, 0 vermelho
            Color stockColor;
            if (totalQty > 5)
                stockColor = Color.FromArgb(80, 200, 80);   // Verde
            else if (totalQty > 0)
                stockColor = Color.FromArgb(230, 160, 50);   // Laranja
            else
                stockColor = Color.FromArgb(200, 80, 80);    // Vermelho

            Color itemColor = stockColor;
            Color arrowColor = stockColor;
            
            var panel = new Panel
            {
                Size = new Size(width, 28),
                BackColor = ThemeEditor.ItemBackground,
                Margin = new Padding(0, 2, 0, 0),
                Cursor = Cursors.Hand,
                Tag = item
            };

            // Seta de expansão
            var arrowLabel = new Label
            {
                Name = $"itemarrow_{item.Id}",
                Text = isExpanded ? "▼" : "▶",
                Font = F7,
                ForeColor = arrowColor,
                Location = new Point(5, 7),
                Size = new Size(15, 14),
                Cursor = Cursors.Hand
            };

            // Nome do item
            var nameLabel = new Label
            {
                Name = $"itemname_{item.Id}",
                Text = item.Name,
                Font = F8B,
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
                Name = $"iteminfo_{item.Id}",
                Text = $"({formattedTotal})",
                Font = F7,
                ForeColor = stockColor,
                Location = new Point(width - 110, 7),
                Size = new Size(80, 14), // Aumentado para números grandes
                TextAlign = ContentAlignment.MiddleRight
            };

            // Botão adicionar conta ao item (com menu de contexto)
            var addBtn = new SafeButton
            {
                Text = "+",
                Font = F8B,
                ForeColor = Color.White,
                BackColor = Color.FromArgb(0, 100, 0), // Semantic: green accent
                FlatStyle = FlatStyle.Flat,
                Size = new Size(20, 20),
                Location = new Point(width - 25, 4),
                Cursor = Cursors.Hand,
                DebugName = $"+_item{item.Id}"
            };
            addBtn.FlatAppearance.BorderSize = 0;

            // Menu do botão "+" (criado junto com o botão para garantir ciclo de vida correto)
            var addAccountMenu = new ContextMenuStrip();
            addAccountMenu.BackColor = ThemeEditor.ItemBackground;
            addAccountMenu.ForeColor = ThemeEditor.FormsForeground;
            addAccountMenu.Renderer = new DarkMenuRenderer();

            var autoItem = new ToolStripMenuItem("🔄 Adicionar Automático");
            autoItem.Click += (s, ev) =>
            {
                LogDebug($"[DEBUG] Menu 'Adicionar Automático' clicado para item '{item.Name}' (id={item.Id})");
                _ = AddNextAvailableAccountToItemAsync(item);
            };

            var manualItem = new ToolStripMenuItem("✏️ Adicionar Manual");
            manualItem.Click += (s, ev) => AddAccountManual(item);

            addAccountMenu.Items.Add(autoItem);
            addAccountMenu.Items.Add(manualItem);

            addAccountMenu.Opening += (s, ev) =>
            {
                LogDebug($"[DEBUG] Menu Opening para item '{item.Name}' (id={item.Id})");
            };
            addAccountMenu.Closed += (s, ev) =>
            {
                LogDebug($"[DEBUG] Menu Closed para item '{item.Name}' (id={item.Id}), reason={ev.CloseReason}");
            };

            // Click esquerdo = mostrar o menu
            addBtn.Click += (s, ev) =>
            {
                LogDebug($"[DEBUG] Botão '+' CLICK fired! item='{item.Name}' (id={item.Id}), btn.IsDisposed={addBtn.IsDisposed}, menu.IsDisposed={addAccountMenu.IsDisposed}, _isAddingAccount={_isAddingAccount}");
                try
                {
                    if (addBtn.IsDisposed)
                    {
                        LogDebugWarning($"[DEBUG] Botão descartado! Abortando.");
                        return;
                    }
                    if (addAccountMenu.IsDisposed)
                    {
                        LogDebugWarning($"[DEBUG] Menu descartado! Abortando.");
                        return;
                    }
                    LogDebug($"[DEBUG] Chamando addAccountMenu.Show(addBtn)...");
                    addAccountMenu.Show(addBtn, new Point(0, addBtn.Height));
                    LogDebug($"[DEBUG] addAccountMenu.Show() retornou. Visible={addAccountMenu.Visible}");
                }
                catch (ObjectDisposedException odex)
                {
                    LogDebugWarning($"[DEBUG] ObjectDisposedException no Show: {odex.ObjectName}");
                }
                catch (Exception ex)
                {
                    OnLogError($"[DEBUG] Exceção no Show: {ex.GetType().Name}: {ex.Message}");
                }
            };

            // Menu de contexto para o item (editar/arquivar)
            var itemContextMenu = new ContextMenuStrip();
            itemContextMenu.BackColor = ThemeEditor.ItemBackground;
            itemContextMenu.ForeColor = ThemeEditor.FormsForeground;
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
            bool hasStock = inventory.Quantity > 0;

            var panel = new Panel
            {
                Size = new Size(width - 15, 24),
                BackColor = ThemeEditor.HeaderBackground,
                Margin = new Padding(15, 1, 0, 0),
                Tag = inventory
            };

            // Username (clicável para selecionar conta)
            var usernameLabel = new Label
            {
                Name = $"invuser_{inventory.Id}",
                Text = inventory.Username,
                Font = F8,
                ForeColor = hasStock ? Color.White : Color.FromArgb(230, 100, 100),
                Location = new Point(5, 4),
                Size = new Size(width - 130, 16), // Reduzido para dar mais espaço ao número
                AutoEllipsis = true,
                Cursor = Cursors.Hand
            };
            usernameLabel.Click += (s, e) => OnAccountSelected(inventory.Username);

            // Quantidade (somente leitura)
            var qtyLabel = new Label
            {
                Text = inventory.Quantity.ToString("N0", new System.Globalization.CultureInfo("pt-BR")),
                Name = $"invqty_{inventory.Id}",
                Font = F7_5B,
                ForeColor = hasStock ? ThemeEditor.FormsForeground : Color.FromArgb(230, 100, 100),
                BackColor = ThemeEditor.HeaderBackground,
                Location = new Point(width - 115, 4),
                Size = new Size(75, 16),
                TextAlign = ContentAlignment.MiddleRight
            };

            // Botão remover
            var removeBtn = new SafeButton
            {
                Text = "×",
                Font = F9B,
                ForeColor = Color.White,
                BackColor = Color.FromArgb(120, 40, 40),
                FlatStyle = FlatStyle.Flat,
                Size = new Size(20, 20),
                Location = new Point(width - 32, 2),
                Tag = inventory,
                Cursor = Cursors.Hand,
                TextAlign = ContentAlignment.MiddleCenter,
                Padding = new Padding(0, 0, 0, 1)
            };
            removeBtn.FlatAppearance.BorderSize = 0;
            removeBtn.Click += RemoveAccountButton_Click;

            // Menu de contexto (click direito)
            var contextMenu = new ContextMenuStrip();
            contextMenu.BackColor = ThemeEditor.ItemBackground;
            contextMenu.ForeColor = ThemeEditor.FormsForeground;
            contextMenu.Renderer = new DarkMenuRenderer();

            var copyUsernameItem = new ToolStripMenuItem("📋 Copiar Username");
            copyUsernameItem.Click += (s, e) => { try { Clipboard.SetText(inventory.Username); } catch { } };

            var moveToEmptyItem = new ToolStripMenuItem("📤 Mover para Contas Vazias");
            moveToEmptyItem.Click += (s, e) => MoveToEmptyAccounts(item, inventory);

            var changeItemMenu = new ToolStripMenuItem("🔄 Alterar Item");
            changeItemMenu.Click += (s, e) => ShowChangeItemDialog(item, inventory);

            var archiveItem = new ToolStripMenuItem("📁 Arquivar Conta");
            archiveItem.Click += (s, e) => ArchiveAccountFromInventory(item, inventory);

            contextMenu.Items.Add(copyUsernameItem);
            contextMenu.Items.Add(new ToolStripSeparator());
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
                    _cachedEmptyAccounts = null;
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

        private bool _isAddingAccount = false;

        private async Task AddNextAvailableAccountToItemAsync(SupabaseGameItem item)
        {
            LogDebug($"[DEBUG] AddNextAvailable CHAMADO para '{item.Name}' (id={item.Id}), _isAddingAccount={_isAddingAccount}");
            if (_isAddingAccount)
            {
                LogDebugWarning($"[DEBUG] AddNextAvailable BLOQUEADO! _isAddingAccount=true");
                return;
            }
            _isAddingAccount = true;
            LogDebug($"[DEBUG] AddNextAvailable _isAddingAccount=true, iniciando busca...");

            try
            {
                // Buscar todas as contas do Supabase (excluir arquivadas)
                var allAccounts = await SupabaseManager.Instance.GetAccountsAsync(false) ?? new List<SupabaseAccount>();

                if (allAccounts.Count == 0)
                {
                    OnLogWarning("⚠️ Nenhuma conta cadastrada no Supabase");
                    MessageBox.Show("Nenhuma conta cadastrada.\nSincronize suas contas primeiro.", "Sem contas", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                // Pegar usernames já adicionados a ESTE item (snapshot para evitar modificação concurrent)
                var existingInThisItem = _inventoryByItem.ContainsKey(item.Id)
                    ? _inventoryByItem[item.Id].ToList().Select(i => i.Username.ToLower()).ToHashSet()
                    : new HashSet<string>();

                // Primeiro: tentar encontrar uma conta que NÃO está em nenhum item deste jogo
                var usernamesInAnyItemOfThisGame = new HashSet<string>();
                foreach (var gameItem in _gameItems.ToList())
                {
                    if (_inventoryByItem.ContainsKey(gameItem.Id))
                    {
                        foreach (var inv in _inventoryByItem[gameItem.Id].ToList())
                        {
                            if (!string.IsNullOrEmpty(inv.Username))
                                usernamesInAnyItemOfThisGame.Add(inv.Username.ToLower());
                        }
                    }
                }

                var completelyFreeAccounts = allAccounts
                    .Where(a => !string.IsNullOrEmpty(a.Username) && !usernamesInAnyItemOfThisGame.Contains(a.Username.ToLower()))
                    .ToList();
                var completelyFreeAccount = completelyFreeAccounts.Count > 0
                    ? completelyFreeAccounts[_rng.Next(completelyFreeAccounts.Count)]
                    : null;

                if (completelyFreeAccount != null)
                {
                    // Conta totalmente livre - adicionar direto
                    await AddAccountToItemDirectAsync(item, completelyFreeAccount.Username);
                    return;
                }

                // Segundo: procurar conta em OUTRO item do mesmo jogo com estoque 0
                SupabaseInventoryEntry zeroStockEntry = null;
                SupabaseGameItem sourceItem = null;

                // Coletar TODAS as entradas com estoque 0 de outros itens, depois escolher aleatoriamente
                var allZeroEntries = new List<(SupabaseInventoryEntry entry, SupabaseGameItem source)>();
                foreach (var gameItem in _gameItems.ToList())
                {
                    if (gameItem.Id == item.Id) continue;

                    if (_inventoryByItem.ContainsKey(gameItem.Id))
                    {
                        var zeroEntries = _inventoryByItem[gameItem.Id].ToList()
                            .Where(inv => inv.Quantity == 0 && !string.IsNullOrEmpty(inv.Username) && !existingInThisItem.Contains(inv.Username.ToLower()));
                        foreach (var entry in zeroEntries)
                            allZeroEntries.Add((entry, gameItem));
                    }
                }

                if (allZeroEntries.Count > 0)
                {
                    var pick = allZeroEntries[_rng.Next(allZeroEntries.Count)];
                    zeroStockEntry = pick.entry;
                    sourceItem = pick.source;
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
                var availableAccounts = allAccounts
                    .Where(a => !string.IsNullOrEmpty(a.Username) && !existingInThisItem.Contains(a.Username.ToLower()))
                    .ToList();
                var availableAccount = availableAccounts.Count > 0
                    ? availableAccounts[_rng.Next(availableAccounts.Count)]
                    : null;

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
                OnLogError($"Erro ao adicionar conta: {ex.Message}");
                MessageBox.Show($"Erro ao adicionar conta:\n{ex.Message}", "Erro", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                _isAddingAccount = false;
                LogDebug($"[DEBUG] AddNextAvailable FINALLY: _isAddingAccount=false");
            }
        }

        private async Task AddAccountToItemDirectAsync(SupabaseGameItem item, string username)
        {
            LogDebug($"[DEBUG] AddAccountDirect INICIADO: username='{username}', item='{item.Name}' (id={item.Id})");

            // Ativar cooldown ANTES do upsert para bloquear reações do Pusher/Realtime
            _lastUserActionTime = DateTime.UtcNow;

            bool success = await SupabaseManager.Instance.UpsertInventoryAsync(username, item.Id, 0);
            LogDebug($"[DEBUG] AddAccountDirect UpsertInventory retornou success={success}");
            if (success)
            {
                OnLogMessage($"✅ '{username}' adicionada a '{item.Name}'");

                // Recarregar inventário do item
                LogDebug($"[DEBUG] AddAccountDirect chamando GetInventoryByItemAsync...");
                var inventory = await SupabaseManager.Instance.GetInventoryByItemAsync(item.Id);
                LogDebug($"[DEBUG] AddAccountDirect inventário retornado: {inventory?.Count ?? -1} registros");
                _inventoryByItem[item.Id] = inventory ?? new List<SupabaseInventoryEntry>();
                _expandedItems[item.Id] = true; // Expandir para mostrar

                // Renovar cooldown antes do refresh
                _lastUserActionTime = DateTime.UtcNow;

                LogDebug($"[DEBUG] AddAccountDirect chamando RefreshItemsPanel...");
                RefreshItemsPanel();
                LogDebug($"[DEBUG] AddAccountDirect CONCLUÍDO");
            }
            else
            {
                OnLogError("Erro ao adicionar conta");
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
                
                _lastUserActionTime = DateTime.UtcNow;
                bool success = await SupabaseManager.Instance.DeleteInventoryAsync(inventory.Id);
                if (success)
                {
                    OnLogMessage($"✅ Conta removida");

                    // Atualizar lista local
                    if (_inventoryByItem.ContainsKey(inventory.ItemId))
                    {
                        _inventoryByItem[inventory.ItemId].RemoveAll(i => i.Id == inventory.Id);
                    }

                    _lastUserActionTime = DateTime.UtcNow;
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

        /// <summary>Log apenas quando Debug Mode está ativo nas configurações.</summary>
        private void LogDebug(string message)
        {
            if (AccountManager.DebugModeAtivo)
                OnLogMessage(message);
        }

        private void LogDebugWarning(string message)
        {
            if (AccountManager.DebugModeAtivo)
                OnLogWarning(message);
        }

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
                inputForm.BackColor = ThemeEditor.FormsBackground;

                Label promptLabel = new Label
                {
                    Text = prompt,
                    Location = new Point(10, 15),
                    Size = new Size(310, 20),
                    ForeColor = ThemeEditor.FormsForeground,
                    Font = F9
                };

                TextBox inputTextBox = new TextBox
                {
                    Text = defaultValue,
                    Location = new Point(10, 40),
                    Size = new Size(310, 25),
                    Font = F9,
                    BackColor = ThemeEditor.InputBackground,
                    ForeColor = ThemeEditor.TextBoxesForeground,
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
                    Font = F9
                };
                okButton.FlatAppearance.BorderSize = 0;

                Button cancelButton = new Button
                {
                    Text = "Cancelar",
                    Location = new Point(245, 75),
                    Size = new Size(75, 28),
                    DialogResult = DialogResult.Cancel,
                    FlatStyle = FlatStyle.Flat,
                    BackColor = ThemeEditor.ButtonsBackground,
                    ForeColor = ThemeEditor.ButtonsForeground,
                    Font = F9
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

        private void SearchBox_Leave(object sender, EventArgs e)
        {
            // Quando sai do campo vazio e havia busca ativa, resetar filtro
            if (string.IsNullOrWhiteSpace(_searchBox.Text) && !string.IsNullOrEmpty(_searchText))
            {
                _searchText = "";
                _expandedItems.Clear();
                ApplyFilter();
            }
        }

        private void SearchBox_TextChanged(object sender, EventArgs e)
        {
            if (_suppressSearchFilter) return;

            _searchText = _searchBox.Text.ToLower().Trim();

            // Debounce: esperar 300ms antes de filtrar (evita rebuild por tecla)
            if (_searchDebounceTimer == null)
            {
                _searchDebounceTimer = new System.Windows.Forms.Timer { Interval = 300 };
                _searchDebounceTimer.Tick += (s, ev) =>
                {
                    _searchDebounceTimer.Stop();
                    ApplyFilter();
                };
            }
            _searchDebounceTimer.Stop();
            _searchDebounceTimer.Start();
        }

        private void SearchBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape)
            {
                _searchBox.Text = "";
                _searchText = "";
                _expandedItems.Clear();
                ApplyFilter();
                _contentPanel.Focus();
            }
        }

        private void ApplyFilter()
        {
            if (_emptyAccountsMode)
            {
                if (_emptyAccountsSelectedGame != null)
                {
                    // Filtrar contas vazias do cache local (sem API call)
                    RefreshCachedEmptyAccountsPanel();
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
                        Font = F8,
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
                BackColor = Color.FromArgb(139, 69, 19), // Semantic: brown/orange
                Margin = new Padding(0, 2, 0, 2),
                Cursor = Cursors.Hand
            };

            var nameLabel = new Label
            {
                Text = "CONTAS VAZIAS",
                Font = F9B,
                ForeColor = ThemeEditor.FormsForeground,
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
                BackColor = ThemeEditor.PanelBackground,
                Margin = new Padding(0, 2, 0, 2),
                Cursor = Cursors.Hand
            };

            var nameLabel = new Label
            {
                Text = "CONTAS VAZIAS",
                Font = F9B,
                ForeColor = Color.FromArgb(205, 133, 63), // Semantic: Peru color accent
                Location = new Point(8, 7),
                Size = new Size(width - 40, 20),
                AutoEllipsis = true,
                Cursor = Cursors.Hand
            };

            // Estrela vazia (sem funcionalidade de favorito)
            var placeholderStar = new Label
            {
                Text = "☆",
                Font = F12,
                ForeColor = ThemeEditor.ButtonsBackground,
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
            _searchBox.Text = "";
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
                    Font = F9,
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
                BackColor = ThemeEditor.PanelBackground,
                Margin = new Padding(0, 2, 0, 2),
                Cursor = Cursors.Hand,
                Tag = game
            };

            var nameLabel = new Label
            {
                Text = game.Name.ToUpper(),
                Font = F9B,
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
        /// Filtra e exibe contas vazias a partir do cache local (sem API call).
        /// Chamado pelo ApplyFilter quando o usuário digita na busca.
        /// </summary>
        private void RefreshCachedEmptyAccountsPanel()
        {
            if (_cachedEmptyAccounts == null) return;

            var filtered = _cachedEmptyAccounts.AsEnumerable();
            if (!string.IsNullOrEmpty(_searchText))
            {
                filtered = filtered.Where(a => a.Username.ToLower().Contains(_searchText));
            }
            var list = filtered.ToList();

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

                if (list.Count == 0)
                {
                    var noLabel = new Label
                    {
                        Text = string.IsNullOrEmpty(_searchText)
                            ? "Nenhuma conta vazia neste jogo.\nTodas as contas têm estoque."
                            : "Nenhuma conta encontrada.",
                        Font = F8,
                        ForeColor = Color.Gray,
                        AutoSize = true,
                        Padding = new Padding(10)
                    };
                    _contentPanel.Controls.Add(noLabel);
                }
                else
                {
                    var headerLabel = new Label
                    {
                        Text = $"{list.Count} contas sem estoque",
                        Font = F8,
                        ForeColor = Color.Orange,
                        AutoSize = true,
                        Padding = new Padding(5, 3, 5, 3)
                    };
                    _contentPanel.Controls.Add(headerLabel);

                    foreach (var account in list)
                    {
                        var accountPanel = CreateEmptyAccountPanel(account);
                        _contentPanel.Controls.Add(accountPanel);
                    }
                }
            }
            finally
            {
                _contentPanel.Visible = true;
                _contentPanel.ResumeLayout(true);
            }
        }

        /// <summary>
        /// Carrega as contas vazias (sem estoque ou estoque zero) para um jogo específico
        /// </summary>
        private async Task LoadEmptyAccountsForGameAsync(SupabaseGame game)
        {
            _emptyAccountsSelectedGame = game;
            _titleLabel.Text = game.Name.ToUpper();

            // Limpar busca sem disparar TextChanged → ApplyFilter
            _suppressSearchFilter = true;
            _searchBox.Text = "";
            _searchText = "";
            _suppressSearchFilter = false;

            _contentPanel.SuspendLayout();
            _contentPanel.Visible = false;
            _contentPanel.Controls.Clear();

            try
            {
                // Mostrar loading
                var loadingLabel = new Label
                {
                    Text = "Carregando...",
                    Font = F9,
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

                // Cachear para filtragem local (busca sem API call)
                _cachedEmptyAccounts = emptyAccounts;

                OnLogMessage($"🔍 Debug: {emptyAccounts.Count} contas SEM estoque");

                _contentPanel.SuspendLayout();
                _contentPanel.Controls.Clear();

                if (emptyAccounts.Count == 0)
                {
                    var noAccountsLabel = new Label
                    {
                        Text = "Nenhuma conta vazia neste jogo.\nTodas as contas têm estoque.",
                        Font = F8,
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
                        Font = F8,
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

            var emptyBg = Color.FromArgb(55, 45, 35); // Semantic: brown tint for empty accounts
            var emptyHover = Color.FromArgb(70, 55, 40);
            var panel = new Panel
            {
                Size = new Size(width, 24),
                BackColor = emptyBg,
                Margin = new Padding(10, 1, 0, 1),
                Cursor = Cursors.Hand,
                Tag = account
            };

            var usernameLabel = new Label
            {
                Text = account.Username,
                Font = F8,
                ForeColor = ThemeEditor.FormsForeground,
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
            contextMenu.BackColor = ThemeEditor.ItemBackground;
            contextMenu.ForeColor = ThemeEditor.FormsForeground;
            contextMenu.Renderer = new DarkMenuRenderer();

            var copyUsernameItem = new ToolStripMenuItem("📋 Copiar Username");
            copyUsernameItem.Click += (s, e) => { try { Clipboard.SetText(account.Username); } catch { } };

            var newStockItem = new ToolStripMenuItem("📦 Novo Estoque");
            newStockItem.Click += (s, e) => ShowNewStockDialog(account);

            var archiveItem = new ToolStripMenuItem("📁 Arquivar Conta");
            archiveItem.Click += (s, e) => ShowArchiveConfirmation(account);

            contextMenu.Items.Add(copyUsernameItem);
            contextMenu.Items.Add(new ToolStripSeparator());
            contextMenu.Items.Add(newStockItem);
            contextMenu.Items.Add(archiveItem);

            panel.ContextMenuStrip = contextMenu;
            usernameLabel.ContextMenuStrip = contextMenu;

            // Hover effect
            panel.MouseEnter += (s, e) => panel.BackColor = emptyHover;
            panel.MouseLeave += (s, e) => panel.BackColor = emptyBg;
            usernameLabel.MouseEnter += (s, e) => panel.BackColor = emptyHover;
            usernameLabel.MouseLeave += (s, e) => panel.BackColor = emptyBg;

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
                BackColor = ThemeEditor.FormsBackground
            };

            // Label conta
            var accountLabel = new Label
            {
                Text = $"Conta: {account.Username}",
                Font = F9B,
                ForeColor = ThemeEditor.FormsForeground,
                Location = new Point(15, 15),
                AutoSize = true
            };

            // Label Item
            var itemLabel = new Label
            {
                Text = "Item:",
                ForeColor = ThemeEditor.FormsForeground,
                Location = new Point(15, 45),
                AutoSize = true
            };

            // ComboBox de itens
            var itemCombo = new ComboBox
            {
                Location = new Point(15, 65),
                Size = new Size(270, 25),
                DropDownStyle = ComboBoxStyle.DropDownList,
                BackColor = ThemeEditor.TextBoxesBackground,
                ForeColor = ThemeEditor.TextBoxesForeground,
                FlatStyle = FlatStyle.Flat
            };

            // Carregar itens do jogo selecionado
            _ = LoadItemsForComboAsync(itemCombo, _emptyAccountsSelectedGame.Id);

            // Label Quantidade
            var qtyLabel = new Label
            {
                Text = "Quantidade:",
                ForeColor = ThemeEditor.FormsForeground,
                Location = new Point(15, 95),
                AutoSize = true
            };

            // TextBox de quantidade
            var qtyTextBox = new TextBox
            {
                Location = new Point(15, 115),
                Size = new Size(100, 25),
                BackColor = ThemeEditor.InputBackground,
                ForeColor = ThemeEditor.TextBoxesForeground,
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
                BackColor = ThemeEditor.ButtonsBackground,
                ForeColor = ThemeEditor.ButtonsForeground,
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
                BackColor = ThemeEditor.FormsBackground
            };

            // Label conta
            var accountLabel = new Label
            {
                Text = $"Conta: {inventory.Username}",
                Font = F9B,
                ForeColor = ThemeEditor.FormsForeground,
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
                ForeColor = ThemeEditor.FormsForeground,
                Location = new Point(15, 65),
                AutoSize = true
            };

            // ComboBox de itens
            var itemCombo = new ComboBox
            {
                Location = new Point(15, 85),
                Size = new Size(270, 25),
                DropDownStyle = ComboBoxStyle.DropDownList,
                BackColor = ThemeEditor.TextBoxesBackground,
                ForeColor = ThemeEditor.TextBoxesForeground,
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
                BackColor = ThemeEditor.ButtonsBackground,
                ForeColor = ThemeEditor.ButtonsForeground,
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
        /// Mostra dialog para editar jogo (nome + place_id)
        /// </summary>
        private void ShowEditGameDialog(SupabaseGame game)
        {
            using (Form editForm = new Form())
            {
                editForm.Text = "Editar Jogo";
                editForm.Size = new Size(370, 220);
                editForm.StartPosition = FormStartPosition.CenterParent;
                editForm.FormBorderStyle = FormBorderStyle.FixedDialog;
                editForm.MaximizeBox = false;
                editForm.MinimizeBox = false;
                editForm.BackColor = ThemeEditor.FormsBackground;

                var nameLabel = new Label
                {
                    Text = "Nome do jogo:",
                    Location = new Point(10, 15),
                    Size = new Size(330, 18),
                    ForeColor = ThemeEditor.FormsForeground,
                    Font = F9
                };

                var nameBox = new TextBox
                {
                    Text = game.Name,
                    Location = new Point(10, 36),
                    Size = new Size(330, 25),
                    Font = F9,
                    BackColor = ThemeEditor.InputBackground,
                    ForeColor = ThemeEditor.TextBoxesForeground,
                    BorderStyle = BorderStyle.FixedSingle
                };

                var placeLabel = new Label
                {
                    Text = "Place ID:",
                    Location = new Point(10, 70),
                    Size = new Size(330, 18),
                    ForeColor = ThemeEditor.FormsForeground,
                    Font = F9
                };

                var placeBox = new TextBox
                {
                    Text = game.PlaceId.HasValue && game.PlaceId.Value > 0 ? game.PlaceId.Value.ToString() : "",
                    Location = new Point(10, 91),
                    Size = new Size(330, 25),
                    Font = F9,
                    BackColor = ThemeEditor.InputBackground,
                    ForeColor = ThemeEditor.TextBoxesForeground,
                    BorderStyle = BorderStyle.FixedSingle
                };

                var okBtn = new Button
                {
                    Text = "Salvar",
                    Location = new Point(170, 135),
                    Size = new Size(80, 30),
                    DialogResult = DialogResult.OK,
                    FlatStyle = FlatStyle.Flat,
                    BackColor = Color.FromArgb(0, 120, 0),
                    ForeColor = Color.White,
                    Font = F9
                };
                okBtn.FlatAppearance.BorderSize = 0;

                var cancelBtn = new Button
                {
                    Text = "Cancelar",
                    Location = new Point(260, 135),
                    Size = new Size(80, 30),
                    DialogResult = DialogResult.Cancel,
                    FlatStyle = FlatStyle.Flat,
                    BackColor = ThemeEditor.ButtonsBackground,
                    ForeColor = ThemeEditor.ButtonsForeground,
                    Font = F9
                };
                cancelBtn.FlatAppearance.BorderSize = 0;

                editForm.Controls.AddRange(new Control[] { nameLabel, nameBox, placeLabel, placeBox, okBtn, cancelBtn });
                editForm.AcceptButton = okBtn;
                editForm.CancelButton = cancelBtn;

                if (editForm.ShowDialog() == DialogResult.OK)
                {
                    string newName = nameBox.Text.Trim();
                    string placeText = placeBox.Text.Trim();
                    long? placeId = null;

                    if (!string.IsNullOrEmpty(placeText) && long.TryParse(placeText, out long parsedId) && parsedId > 0)
                        placeId = parsedId;

                    bool nameChanged = !string.IsNullOrWhiteSpace(newName) && newName != game.Name;
                    bool placeChanged = placeId != game.PlaceId;

                    if (nameChanged || placeChanged)
                    {
                        _ = UpdateGameDetailsAsync(game, string.IsNullOrWhiteSpace(newName) ? game.Name : newName, placeId);
                    }
                }
            }
        }

        /// <summary>
        /// Atualiza nome e place_id do jogo no Supabase
        /// </summary>
        private async Task UpdateGameDetailsAsync(SupabaseGame game, string newName, long? placeId)
        {
            var success = await SupabaseManager.Instance.UpdateGameDetailsAsync(game.Id, newName, placeId);

            if (success)
            {
                string changes = "";
                if (newName != game.Name) changes += $"nome: {game.Name} → {newName}";
                if (placeId != game.PlaceId)
                {
                    if (changes.Length > 0) changes += ", ";
                    changes += $"place_id: {game.PlaceId?.ToString() ?? "vazio"} → {placeId?.ToString() ?? "vazio"}";
                }
                OnLogMessage($"✏️ Jogo atualizado: {changes}");
                game.Name = newName;
                game.PlaceId = placeId;

                // Recarregar lista de jogos
                await LoadGamesAsync();
            }
            else
            {
                MessageBox.Show("Erro ao atualizar jogo.", "Erro", MessageBoxButtons.OK, MessageBoxIcon.Error);
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
            e.TextColor = ThemeEditor.FormsForeground;
            base.OnRenderItemText(e);
        }
    }

    internal class DarkColorTable : ProfessionalColorTable
    {
        public override Color MenuItemSelected => ThemeEditor.ButtonsBackground;
        public override Color MenuItemSelectedGradientBegin => ThemeEditor.ButtonsBackground;
        public override Color MenuItemSelectedGradientEnd => ThemeEditor.ButtonsBackground;
        public override Color MenuBorder => ThemeEditor.TextBoxesBorder;
        public override Color MenuItemBorder => ThemeEditor.TextBoxesBorder;
        public override Color ImageMarginGradientBegin => ThemeEditor.ItemBackground;
        public override Color ImageMarginGradientMiddle => ThemeEditor.ItemBackground;
        public override Color ImageMarginGradientEnd => ThemeEditor.ItemBackground;
        public override Color ToolStripDropDownBackground => ThemeEditor.ItemBackground;
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

        /// <summary>Nome para debug (ex: "+_itemId").</summary>
        public string DebugName { get; set; }

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
                        if (AccountManager.DebugModeAtivo)
                            System.Diagnostics.Debug.WriteLine($"[SafeButton] DISPOSED click eaten! DebugName={DebugName}, Msg=0x{m.Msg:X4}, Handle=0x{m.HWnd:X}");
                        return;
                    case WM_RBUTTONDOWN:
                    case WM_RBUTTONUP:
                    case WM_MBUTTONDOWN:
                    case WM_MBUTTONUP:
                    case WM_MOUSEMOVE:
                        return; // Ignorar silenciosamente
                }
            }

            // Log de cliques em botões "+" para debug
            if (m.Msg == WM_LBUTTONDOWN && !string.IsNullOrEmpty(DebugName) && AccountManager.DebugModeAtivo)
            {
                System.Diagnostics.Debug.WriteLine($"[SafeButton] WM_LBUTTONDOWN DebugName={DebugName}, IsDisposed={IsDisposed}, Visible={Visible}, Enabled={Enabled}, Handle=0x{Handle:X}");
            }

            try
            {
                base.WndProc(ref m);
            }
            catch (ObjectDisposedException) { }
        }
    }
}

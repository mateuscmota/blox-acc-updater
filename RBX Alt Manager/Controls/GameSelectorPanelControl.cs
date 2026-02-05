using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using RBX_Alt_Manager.Classes;

namespace RBX_Alt_Manager.Controls
{
    public partial class GameSelectorPanelControl : UserControl
    {
        private GoogleSheetsIntegration _sheetsIntegration;
        private string _spreadsheetId;
        private long _selectedGameGid = 0;
        private string _selectedGameName = "";
        private Dictionary<string, int> _currentGameItems = new Dictionary<string, int>();
        
        // Índice de rotação por produto (para alternar entre contas)
        private Dictionary<string, int> _productRotationIndex = new Dictionary<string, int>();

        // Evento quando uma conta deve ser selecionada
        public event EventHandler<AccountSelectionEventArgs> AccountSelectionRequested;
        public event EventHandler<string> LogMessage;
        public event EventHandler<string> LogWarning;

        public GameSelectorPanelControl()
        {
            InitializeComponent();
            InitializeGameButtons();
        }

        /// <summary>
        /// Configura a integração com Google Sheets
        /// </summary>
        public void Configure(string spreadsheetId)
        {
            _spreadsheetId = spreadsheetId;
            _sheetsIntegration = new GoogleSheetsIntegration(spreadsheetId);
        }

        /// <summary>
        /// Inicializa os botões dos jogos
        /// </summary>
        public void InitializeGameButtons()
        {
            GameSelectorButtonsPanel.Controls.Clear();
            
            // Ordenar jogos alfabeticamente pelo nome
            foreach (var game in GoogleSheetsIntegration.GameSheets.OrderBy(x => x.Key))
            {
                var btn = new Button
                {
                    Text = game.Key.ToLower(),
                    Tag = game.Value,
                    Size = new Size(GameSelectorButtonsPanel.Width - 10, 35),
                    FlatStyle = FlatStyle.Flat,
                    Font = new Font("Segoe UI", 9F),
                    ForeColor = Color.Black,
                    BackColor = Color.White,
                    Cursor = Cursors.Hand,
                    Margin = new Padding(2, 5, 2, 5)
                };
                btn.FlatAppearance.BorderSize = 1;
                btn.Click += GameButton_Click;
                
                GameSelectorButtonsPanel.Controls.Add(btn);
            }

            GameSelectorTitleLabel.Text = "JOGOS";
            GameSelectorBackButton.Visible = false;
            GameSelectorSearchTextBox.Visible = false;
            _currentGameItems.Clear();
            _productRotationIndex.Clear();  // Limpar rotação ao voltar
        }

        private async void GameButton_Click(object sender, EventArgs e)
        {
            var btn = sender as Button;
            if (btn == null) return;

            _selectedGameGid = (long)btn.Tag;
            _selectedGameName = btn.Text.ToUpper();

            await ShowGameItemsAsync(_selectedGameGid, _selectedGameName);
        }

        private async Task ShowGameItemsAsync(long gid, string gameName)
        {
            if (_sheetsIntegration == null) return;

            GameSelectorTitleLabel.Text = gameName;
            GameSelectorButtonsPanel.Controls.Clear();
            GameSelectorBackButton.Visible = true;
            GameSelectorSearchTextBox.Visible = true;
            GameSelectorSearchTextBox.Text = "🔍 Buscar...";

            var productsWithQty = await _sheetsIntegration.GetProductsWithTotalQuantityAsync(gid);
            _currentGameItems = productsWithQty;

            if (productsWithQty.Count == 0)
            {
                var noItemsLabel = new Label
                {
                    Text = "Nenhum item encontrado",
                    ForeColor = Color.Gray,
                    Font = new Font("Segoe UI", 8F),
                    Size = new Size(GameSelectorButtonsPanel.Width - 10, 30),
                    TextAlign = ContentAlignment.MiddleCenter
                };
                GameSelectorButtonsPanel.Controls.Add(noItemsLabel);
                return;
            }

            // Ordenar itens alfabeticamente pelo nome
            foreach (var item in productsWithQty.OrderBy(x => x.Key))
            {
                var itemPanel = CreateGameItemPanel(item.Key, item.Value);
                GameSelectorButtonsPanel.Controls.Add(itemPanel);
            }

            OnLogMessage($"🎮 {gameName}: {productsWithQty.Count} itens");
        }

        /// <summary>
        /// Formata um número com separador de milhares (ponto)
        /// Ex: 900000000 -> 900.000.000
        /// </summary>
        private static string FormatNumber(int number)
        {
            return number.ToString("N0", new System.Globalization.CultureInfo("pt-BR"));
        }

        private Panel CreateGameItemPanel(string product, int totalQty)
        {
            var itemPanel = new Panel
            {
                Size = new Size(GameSelectorButtonsPanel.Width - 10, 28),
                BackColor = Color.FromArgb(50, 50, 50),
                Margin = new Padding(2, 2, 2, 2),
                Cursor = Cursors.Hand,
                Tag = product
            };

            var nameLabel = new Label
            {
                Text = product,
                Font = new Font("Segoe UI", 7.5F),
                ForeColor = Color.White,
                Location = new Point(5, 5),
                Size = new Size(itemPanel.Width - 70, 18),
                AutoEllipsis = true,
                Tag = product
            };

            var qtyLabel = new Label
            {
                Text = FormatNumber(totalQty),
                Font = new Font("Segoe UI", 8F, FontStyle.Bold),
                ForeColor = Color.LimeGreen,
                Location = new Point(itemPanel.Width - 65, 5),
                Size = new Size(60, 18),
                TextAlign = ContentAlignment.MiddleRight,
                Tag = product
            };

            itemPanel.Controls.Add(nameLabel);
            itemPanel.Controls.Add(qtyLabel);

            itemPanel.Click += ItemPanel_Click;
            nameLabel.Click += ItemPanel_Click;
            qtyLabel.Click += ItemPanel_Click;

            return itemPanel;
        }

        private async void ItemPanel_Click(object sender, EventArgs e)
        {
            Control ctrl = sender as Control;
            string product = ctrl?.Tag?.ToString();
            
            if (string.IsNullOrEmpty(product)) return;

            await SelectAccountWithProduct(product);
        }

        private async Task SelectAccountWithProduct(string product)
        {
            if (string.IsNullOrEmpty(product) || _sheetsIntegration == null) return;

            var accountsWithItem = await _sheetsIntegration.GetAccountsWithItemAsync(_selectedGameGid, product);

            if (accountsWithItem.Count == 0)
            {
                OnLogWarning($"⚠️ Nenhuma conta tem '{product}' em estoque");
                return;
            }

            // Obter índice atual de rotação para este produto
            string rotationKey = $"{_selectedGameGid}_{product}";
            if (!_productRotationIndex.ContainsKey(rotationKey))
                _productRotationIndex[rotationKey] = 0;

            // Pegar conta baseada no índice de rotação
            int currentIndex = _productRotationIndex[rotationKey] % accountsWithItem.Count;
            var selectedAccount = accountsWithItem[currentIndex];
            
            // Incrementar índice para próximo clique
            _productRotationIndex[rotationKey]++;
            
            // Log de debug
            OnLogMessage($"🔄 {product}: conta {currentIndex + 1}/{accountsWithItem.Count}");
            
            // Disparar evento para selecionar a conta
            OnAccountSelectionRequested(selectedAccount.Username, product, selectedAccount.QuantityInt);
        }

        private void BackButton_Click(object sender, EventArgs e)
        {
            InitializeGameButtons();
        }

        private void SearchTextBox_Enter(object sender, EventArgs e)
        {
            if (GameSelectorSearchTextBox.Text == "🔍 Buscar...")
                GameSelectorSearchTextBox.Text = "";
        }

        private void SearchTextBox_Leave(object sender, EventArgs e)
        {
            if (string.IsNullOrWhiteSpace(GameSelectorSearchTextBox.Text))
                GameSelectorSearchTextBox.Text = "🔍 Buscar...";
        }

        private void SearchTextBox_TextChanged(object sender, EventArgs e)
        {
            string searchText = GameSelectorSearchTextBox.Text;
            
            if (searchText == "🔍 Buscar..." || _currentGameItems.Count == 0)
                return;

            FilterGameItems(searchText);
        }

        private void FilterGameItems(string searchText)
        {
            GameSelectorButtonsPanel.Controls.Clear();

            var filteredItems = string.IsNullOrWhiteSpace(searchText)
                ? _currentGameItems
                : _currentGameItems.Where(x => x.Key.IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0)
                    .ToDictionary(x => x.Key, x => x.Value);

            // Ordenar itens filtrados alfabeticamente pelo nome
            foreach (var item in filteredItems.OrderBy(x => x.Key))
            {
                var itemPanel = CreateGameItemPanel(item.Key, item.Value);
                GameSelectorButtonsPanel.Controls.Add(itemPanel);
            }
        }

        public void InvalidateCache()
        {
            _sheetsIntegration?.InvalidateCache();
        }

        // Event helpers
        protected virtual void OnLogMessage(string message) => LogMessage?.Invoke(this, message);
        protected virtual void OnLogWarning(string message) => LogWarning?.Invoke(this, message);
        protected virtual void OnAccountSelectionRequested(string username, string product, int quantity)
        {
            AccountSelectionRequested?.Invoke(this, new AccountSelectionEventArgs(username, product, quantity));
        }

        private void GameSelectorPanelControl_Load(object sender, EventArgs e)
        {

        }
    }

    public class AccountSelectionEventArgs : EventArgs
    {
        public string Username { get; }
        public string Product { get; }
        public int Quantity { get; }

        public AccountSelectionEventArgs(string username, string product, int quantity)
        {
            Username = username;
            Product = product;
            Quantity = quantity;
        }
    }
}

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using RBX_Alt_Manager.Classes;

namespace RBX_Alt_Manager.Controls
{
    public partial class EstoquePanelControl : UserControl
    {
        private GoogleSheetsIntegration _sheetsIntegration;
        private string _spreadsheetId;
        private string _appsScriptUrl;
        private static readonly HttpClient _httpClient = new HttpClient() { Timeout = TimeSpan.FromSeconds(10) };

        /// <summary>
        /// Formata um número com separador de milhares (ponto)
        /// Ex: 900000000 -> 900.000.000
        /// </summary>
        private static string FormatNumber(long number)
        {
            return number.ToString("N0", new System.Globalization.CultureInfo("pt-BR"));
        }

        /// <summary>
        /// Formata um número com separador de milhares (ponto)
        /// Ex: 900000000 -> 900.000.000
        /// </summary>
        private static string FormatNumber(int number)
        {
            return number.ToString("N0", new System.Globalization.CultureInfo("pt-BR"));
        }

        // Sistema de debounce - aguarda 1.5s após último clique antes de enviar
        private Dictionary<string, CancellationTokenSource> _debounceTokens = new Dictionary<string, CancellationTokenSource>();
        private const int DEBOUNCE_DELAY_MS = 1500;
        
        // Proteção contra múltiplas chamadas simultâneas
        private bool _isLoading = false;
        private CancellationTokenSource _refreshCts = null;

        // Eventos
        public event EventHandler<string> LogMessage;
        public event EventHandler<string> LogWarning;
        public event EventHandler<string> LogError;
        public event EventHandler<SupabaseInventoryEntry> OnAccountMovedToEmpty;

        public EstoquePanelControl()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Configura a integração com Google Sheets
        /// </summary>
        public void Configure(string spreadsheetId, string appsScriptUrl)
        {
            _spreadsheetId = spreadsheetId;
            _appsScriptUrl = appsScriptUrl;
            _sheetsIntegration = new GoogleSheetsIntegration(spreadsheetId);
        }

        /// <summary>
        /// Carrega os produtos de uma conta
        /// </summary>
        public async Task LoadProductsAsync(string username)
        {
            if (_sheetsIntegration == null || string.IsNullOrEmpty(username))
            {
                ClearPanel();
                return;
            }

            // Evitar múltiplas chamadas simultâneas
            if (_isLoading) return;
            _isLoading = true;

            EstoqueUserLabel.Text = username;
            EstoqueItemsPanel.Controls.Clear();

            try
            {
                var allProducts = await _sheetsIntegration.GetAllProductsForUserAsync(username);

                if (allProducts.Count == 0)
                {
                    EstoqueGameLabel.Text = "Sem produtos";
                    return;
                }

                // Mostrar quantidade de jogos
                EstoqueGameLabel.Text = allProducts.Count == 1 
                    ? allProducts[0].GameName.ToUpper() 
                    : $"{allProducts.Count} JOGOS";

                // Adicionar produtos por jogo
                foreach (var game in allProducts)
                {
                    // Header do jogo
                    var header = new Label
                    {
                        Text = game.GameName.ToUpper(),
                        Font = new Font("Segoe UI", 7F, FontStyle.Bold),
                        ForeColor = Color.LimeGreen,
                        BackColor = Color.FromArgb(45, 45, 45),
                        Size = new Size(EstoqueItemsPanel.Width - 20, 18),
                        TextAlign = ContentAlignment.MiddleCenter,
                        Margin = new Padding(0, 5, 0, 2)
                    };
                    EstoqueItemsPanel.Controls.Add(header);

                    // Produtos
                    foreach (var product in game.Products)
                    {
                        var itemPanel = CreateProductPanel(product);
                        EstoqueItemsPanel.Controls.Add(itemPanel);
                    }
                }

                OnLogMessage($"📦 {username}: {allProducts.Sum(g => g.Products.Count)} produtos em {allProducts.Count} jogos");
            }
            catch (Exception ex)
            {
                OnLogError($"Erro ao carregar produtos: {ex.Message}");
            }
            finally
            {
                _isLoading = false;
            }
        }

        /// <summary>
        /// Cria um painel para um produto com botões +/-
        /// </summary>
        private Panel CreateProductPanel(SheetProduct product)
        {
            // Tamanho fixo igual ao Designer (154x38)
            var panel = new Panel
            {
                Size = new Size(154, 38),
                BackColor = Color.FromArgb(35, 35, 35),
                Margin = new Padding(0, 1, 0, 1)
            };

            // Nome do produto - igual ao Designer
            var nameLabel = new Label
            {
                Text = product.Product,
                Font = new Font("Segoe UI", 7F),
                ForeColor = Color.LightGray,
                Location = new Point(2, 1),
                Size = new Size(140, 15),
                AutoEllipsis = true
            };

            // Botão - (posições EXATAS do Designer)
            var minusBtn = new Button
            {
                Text = "-",
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                ForeColor = Color.White,
                BackColor = Color.FromArgb(80, 80, 80),
                FlatStyle = FlatStyle.Flat,
                Location = new Point(7, 17),
                Size = new Size(30, 15),
                Cursor = Cursors.Hand,
                Tag = product
            };
            minusBtn.FlatAppearance.BorderSize = 0;
            minusBtn.Click += MinusButton_Click;

            // Quantidade TextBox (posições EXATAS do Designer)
            var qtyTextBox = new TextBox
            {
                Text = FormatNumber(product.QuantityInt),
                Name = $"qty_{product.RowIndex}_{product.Gid}",
                Font = new Font("Segoe UI", 8F, FontStyle.Bold),
                ForeColor = Color.White,
                BackColor = Color.FromArgb(50, 50, 50),
                BorderStyle = BorderStyle.None,
                Location = new Point(42, 17),
                Size = new Size(70, 15),
                TextAlign = HorizontalAlignment.Center,
                Tag = product
            };
            qtyTextBox.KeyPress += QtyTextBox_KeyPress;
            qtyTextBox.Leave += QtyTextBox_Leave;
            qtyTextBox.KeyDown += QtyTextBox_KeyDown;
            qtyTextBox.Enter += QtyTextBox_Enter;

            // Botão + (posições EXATAS do Designer)
            var plusBtn = new Button
            {
                Text = "+",
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                ForeColor = Color.White,
                BackColor = Color.FromArgb(0, 120, 0),
                FlatStyle = FlatStyle.Flat,
                Location = new Point(117, 17),
                Size = new Size(30, 15),
                Cursor = Cursors.Hand,
                Tag = product
            };
            plusBtn.FlatAppearance.BorderSize = 0;
            plusBtn.Click += PlusButton_Click;

            panel.Controls.Add(nameLabel);
            panel.Controls.Add(minusBtn);
            panel.Controls.Add(qtyTextBox);
            panel.Controls.Add(plusBtn);

            return panel;
        }

        private void QtyTextBox_KeyPress(object sender, KeyPressEventArgs e)
        {
            // Permitir apenas números, controles (backspace, delete) e separadores
            if (!char.IsControl(e.KeyChar) && !char.IsDigit(e.KeyChar) && e.KeyChar != '.' && e.KeyChar != ',')
                e.Handled = true;
        }

        /// <summary>
        /// Quando entrar no TextBox, mostrar número sem formatação para facilitar edição
        /// </summary>
        private void QtyTextBox_Enter(object sender, EventArgs e)
        {
            var textBox = sender as TextBox;
            if (textBox == null) return;
            var product = textBox.Tag as SheetProduct;
            if (product == null) return;
            
            // Mostrar número sem formatação quando editando
            textBox.Text = product.QuantityInt.ToString();
            textBox.SelectAll();
        }

        private void QtyTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true;
                SaveQtyFromTextBox(sender as TextBox);
            }
        }

        private void QtyTextBox_Leave(object sender, EventArgs e)
        {
            SaveQtyFromTextBox(sender as TextBox);
        }

        private async void SaveQtyFromTextBox(TextBox textBox)
        {
            if (textBox == null) return;
            var product = textBox.Tag as SheetProduct;
            if (product == null) return;

            // Remover separadores de milhares para parsear
            string cleanText = textBox.Text.Replace(".", "").Replace(",", "");
            
            int newQty;
            if (!int.TryParse(cleanText, out newQty)) newQty = 0;
            if (newQty < 0) newQty = 0;

            if (newQty == product.QuantityInt)
            {
                // Reformatar mesmo sem mudança
                textBox.Text = FormatNumber(newQty);
                return;
            }

            product.QuantityInt = newQty;
            product.Quantity = newQty.ToString();
            textBox.Text = FormatNumber(newQty);

            await UpdateSheetValueAsync(product);
        }

        private void MinusButton_Click(object sender, EventArgs e)
        {
            var btn = sender as Button;
            var product = btn?.Tag as SheetProduct;
            if (product == null || product.QuantityInt <= 0) return;

            product.QuantityInt--;
            product.Quantity = product.QuantityInt.ToString();

            var parent = btn.Parent as Panel;
            var qtyTextBox = parent?.Controls.OfType<TextBox>().FirstOrDefault(t => t.Name.StartsWith("qty_"));
            if (qtyTextBox != null) qtyTextBox.Text = FormatNumber(product.QuantityInt);

            ScheduleUpdate(product);
        }

        private void PlusButton_Click(object sender, EventArgs e)
        {
            var btn = sender as Button;
            var product = btn?.Tag as SheetProduct;
            if (product == null) return;

            product.QuantityInt++;
            product.Quantity = product.QuantityInt.ToString();

            var parent = btn.Parent as Panel;
            var qtyTextBox = parent?.Controls.OfType<TextBox>().FirstOrDefault(t => t.Name.StartsWith("qty_"));
            if (qtyTextBox != null) qtyTextBox.Text = FormatNumber(product.QuantityInt);

            ScheduleUpdate(product);
        }

        /// <summary>
        /// Agenda uma atualização com debounce - só envia após 1.5s sem cliques
        /// </summary>
        private void ScheduleUpdate(SheetProduct product)
        {
            string key = $"{product.Gid}_{product.RowIndex}";

            // Cancelar timer anterior se existir
            if (_debounceTokens.ContainsKey(key))
            {
                _debounceTokens[key].Cancel();
                _debounceTokens[key].Dispose();
            }

            // Criar novo token de cancelamento
            var cts = new CancellationTokenSource();
            _debounceTokens[key] = cts;

            // Agendar envio após delay
            Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(DEBOUNCE_DELAY_MS, cts.Token);
                    
                    // Se não foi cancelado, enviar atualização
                    if (!cts.Token.IsCancellationRequested)
                    {
                        await UpdateSheetValueAsync(product);
                        
                        // Limpar token após uso
                        if (_debounceTokens.ContainsKey(key))
                            _debounceTokens.Remove(key);
                    }
                }
                catch (TaskCanceledException)
                {
                    // Ignorar - foi cancelado por novo clique
                }
            });
        }

        private async Task UpdateSheetValueAsync(SheetProduct product)
        {
            if (string.IsNullOrEmpty(_appsScriptUrl)) return;

            try
            {
                string url = $"{_appsScriptUrl}?action=update&gid={product.Gid}&row={product.RowIndex}&value={product.QuantityInt}";
                var response = await _httpClient.GetStringAsync(url);

                if (response.Contains("\"success\":true"))
                    OnLogMessage($"✅ {product.Product}: {product.QuantityInt}");
                else
                    OnLogWarning($"⚠️ {product.Product}: Erro ao salvar");
            }
            catch (TaskCanceledException)
            {
                // Ignorar - requisição cancelada pelo debounce
            }
            catch (OperationCanceledException)
            {
                // Ignorar - operação cancelada
            }
            catch (Exception ex)
            {
                OnLogError($"❌ Erro: {ex.Message}");
            }
        }

        private void RefreshButton_Click(object sender, EventArgs e)
        {
            // Desabilitar botão temporariamente para evitar spam
            EstoqueRefreshButton.Enabled = false;
            
            _sheetsIntegration?.InvalidateCache();
            if (!string.IsNullOrEmpty(EstoqueUserLabel.Text) && EstoqueUserLabel.Text != "Selecione uma conta")
            {
                _isLoading = false; // Forçar reload
                _ = LoadProductsAsync(EstoqueUserLabel.Text);
                OnLogMessage("🔄 Atualizando estoque...");
            }
            
            // Reabilitar após 2 segundos
            Task.Run(async () =>
            {
                await Task.Delay(2000);
                if (EstoqueRefreshButton.InvokeRequired)
                    EstoqueRefreshButton.Invoke(new Action(() => EstoqueRefreshButton.Enabled = true));
                else
                    EstoqueRefreshButton.Enabled = true;
            });
        }

        public void ClearPanel()
        {
            EstoqueUserLabel.Text = "Selecione uma conta";
            EstoqueGameLabel.Text = "";
            EstoqueItemsPanel.Controls.Clear();
        }

        public void InvalidateCache()
        {
            _sheetsIntegration?.InvalidateCache();
        }

        /// <summary>
        /// Carrega os produtos de uma conta do Supabase (novo sistema)
        /// </summary>
        public async Task LoadProductsFromSupabaseAsync(string username)
        {
            if (string.IsNullOrEmpty(username))
            {
                ClearPanel();
                return;
            }

            // Evitar múltiplas chamadas simultâneas
            if (_isLoading) return;
            _isLoading = true;

            EstoqueUserLabel.Text = username;
            EstoqueItemsPanel.Controls.Clear();

            try
            {
                // Buscar inventário da conta
                var inventory = await SupabaseManager.Instance.GetInventoryByUsernameAsync(username);
                
                if (inventory == null || inventory.Count == 0)
                {
                    EstoqueGameLabel.Text = "Sem produtos";
                    _isLoading = false;
                    return;
                }

                // Buscar todos os jogos e itens para mapear
                var games = await SupabaseManager.Instance.GetGamesAsync() ?? new List<SupabaseGame>();
                var gameDict = games.ToDictionary(g => g.Id, g => g.Name);
                
                // Agrupar inventário por jogo
                var itemIds = inventory.Select(i => i.ItemId).Distinct().ToList();
                var itemToGame = new Dictionary<int, int>();
                var itemNames = new Dictionary<int, string>();
                
                foreach (var game in games)
                {
                    var items = await SupabaseManager.Instance.GetGameItemsAsync(game.Id);
                    if (items != null)
                    {
                        foreach (var item in items)
                        {
                            itemToGame[item.Id] = game.Id;
                            itemNames[item.Id] = item.Name;
                        }
                    }
                }

                // Agrupar por jogo
                var groupedByGame = inventory
                    .Where(inv => itemToGame.ContainsKey(inv.ItemId))
                    .GroupBy(inv => itemToGame[inv.ItemId])
                    .OrderBy(g => gameDict.ContainsKey(g.Key) ? gameDict[g.Key] : "")
                    .ToList();

                // Mostrar quantidade de jogos
                EstoqueGameLabel.Text = groupedByGame.Count == 1 
                    ? (gameDict.ContainsKey(groupedByGame[0].Key) ? gameDict[groupedByGame[0].Key].ToUpper() : "JOGO")
                    : $"{groupedByGame.Count} JOGOS";

                // Adicionar produtos por jogo
                foreach (var gameGroup in groupedByGame)
                {
                    string gameName = gameDict.ContainsKey(gameGroup.Key) ? gameDict[gameGroup.Key] : "Jogo Desconhecido";
                    
                    // Header do jogo
                    var header = new Label
                    {
                        Text = gameName.ToUpper(),
                        Font = new Font("Segoe UI", 7F, FontStyle.Bold),
                        ForeColor = Color.LimeGreen,
                        BackColor = Color.FromArgb(45, 45, 45),
                        Size = new Size(EstoqueItemsPanel.Width - 20, 18),
                        TextAlign = ContentAlignment.MiddleCenter,
                        Margin = new Padding(0, 5, 0, 2)
                    };
                    EstoqueItemsPanel.Controls.Add(header);

                    // Produtos
                    foreach (var inv in gameGroup.OrderBy(i => itemNames.ContainsKey(i.ItemId) ? itemNames[i.ItemId] : ""))
                    {
                        string itemName = itemNames.ContainsKey(inv.ItemId) ? itemNames[inv.ItemId] : $"Item {inv.ItemId}";
                        var itemPanel = CreateSupabaseProductPanel(inv, itemName);
                        EstoqueItemsPanel.Controls.Add(itemPanel);
                    }
                }

                OnLogMessage($"📦 {username}: {inventory.Count} produtos em {groupedByGame.Count} jogos");
            }
            catch (Exception ex)
            {
                OnLogError($"Erro ao carregar produtos: {ex.Message}");
            }
            finally
            {
                _isLoading = false;
            }
        }

        /// <summary>
        /// Cria um painel para um produto do Supabase com botões +/-
        /// </summary>
        private Panel CreateSupabaseProductPanel(SupabaseInventoryEntry inventory, string itemName)
        {
            var panel = new Panel
            {
                Size = new Size(160, 38), // Aumentado de 154 para 160
                BackColor = Color.FromArgb(35, 35, 35),
                Margin = new Padding(0, 1, 0, 1)
            };

            var nameLabel = new Label
            {
                Text = itemName,
                Font = new Font("Segoe UI", 7F),
                ForeColor = Color.LightGray,
                Location = new Point(2, 1),
                Size = new Size(156, 15), // Aumentado de 140 para 156
                AutoEllipsis = true
            };

            var minusBtn = new Button
            {
                Text = "-",
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                ForeColor = Color.White,
                BackColor = Color.FromArgb(80, 80, 80),
                FlatStyle = FlatStyle.Flat,
                Location = new Point(5, 17),
                Size = new Size(30, 15),
                Cursor = Cursors.Hand,
                Tag = inventory
            };
            minusBtn.FlatAppearance.BorderSize = 0;
            minusBtn.Click += SupabaseMinusButton_Click;

            var qtyTextBox = new TextBox
            {
                Text = FormatNumber(inventory.Quantity),
                Name = $"sqty_{inventory.Id}",
                Font = new Font("Segoe UI", 8F, FontStyle.Bold),
                ForeColor = Color.White,
                BackColor = Color.FromArgb(50, 50, 50),
                BorderStyle = BorderStyle.None,
                Location = new Point(38, 17),
                Size = new Size(82, 15), // Maior para números grandes
                TextAlign = HorizontalAlignment.Center,
                Tag = inventory
            };
            qtyTextBox.KeyPress += QtyTextBox_KeyPress;
            qtyTextBox.Leave += SupabaseQtyTextBox_Leave;
            qtyTextBox.KeyDown += SupabaseQtyTextBox_KeyDown;
            qtyTextBox.Enter += SupabaseQtyTextBox_Enter;

            var plusBtn = new Button
            {
                Text = "+",
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                ForeColor = Color.White,
                BackColor = Color.FromArgb(0, 120, 0),
                FlatStyle = FlatStyle.Flat,
                Location = new Point(125, 17), // Ajustado
                Size = new Size(30, 15),
                Cursor = Cursors.Hand,
                Tag = inventory
            };
            plusBtn.FlatAppearance.BorderSize = 0;
            plusBtn.Click += SupabasePlusButton_Click;

            panel.Controls.Add(nameLabel);
            panel.Controls.Add(minusBtn);
            panel.Controls.Add(qtyTextBox);
            panel.Controls.Add(plusBtn);

            return panel;
        }

        private void SupabaseQtyTextBox_Enter(object sender, EventArgs e)
        {
            var textBox = sender as TextBox;
            if (textBox == null || textBox.IsDisposed) return;
            var inventory = textBox.Tag as SupabaseInventoryEntry;
            if (inventory == null) return;
            
            textBox.Text = inventory.Quantity.ToString();
            textBox.SelectAll();
        }

        private void SupabaseQtyTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true;
                SaveSupabaseQtyFromTextBox(sender as TextBox);
            }
        }

        private void SupabaseQtyTextBox_Leave(object sender, EventArgs e)
        {
            SaveSupabaseQtyFromTextBox(sender as TextBox);
        }

        /// <summary>
        /// Converte texto com notação abreviada para número
        /// Ex: "1k" -> 1000, "2m" -> 2000000, "3b" -> 3000000000, "4t" -> 4000000000000
        /// </summary>
        private long ParseAbbreviatedNumber(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return 0;
            
            text = text.Trim().ToLower().Replace(".", "").Replace(",", "");
            
            // Verificar se termina com sufixo
            long multiplier = 1;
            if (text.EndsWith("k"))
            {
                multiplier = 1_000;
                text = text.Substring(0, text.Length - 1);
            }
            else if (text.EndsWith("m"))
            {
                multiplier = 1_000_000;
                text = text.Substring(0, text.Length - 1);
            }
            else if (text.EndsWith("b"))
            {
                multiplier = 1_000_000_000;
                text = text.Substring(0, text.Length - 1);
            }
            else if (text.EndsWith("t"))
            {
                multiplier = 1_000_000_000_000;
                text = text.Substring(0, text.Length - 1);
            }
            
            // Tentar parsear como decimal para permitir "1.5m" = 1500000
            if (decimal.TryParse(text, System.Globalization.NumberStyles.Any, 
                System.Globalization.CultureInfo.InvariantCulture, out decimal decimalValue))
            {
                return (long)(decimalValue * multiplier);
            }
            
            // Fallback: tentar como long simples
            if (long.TryParse(text, out long longValue))
            {
                return longValue * multiplier;
            }
            
            return 0;
        }

        private void SaveSupabaseQtyFromTextBox(TextBox textBox)
        {
            if (textBox == null || textBox.IsDisposed) return;
            
            try
            {
                var inventory = textBox.Tag as SupabaseInventoryEntry;
                if (inventory == null) return;

                long newQty = ParseAbbreviatedNumber(textBox.Text);
                if (newQty < 0) newQty = 0;

                if (newQty == inventory.Quantity)
                {
                    if (!textBox.IsDisposed)
                        textBox.Text = FormatNumber(newQty);
                    return;
                }

                inventory.Quantity = newQty;
                
                if (!textBox.IsDisposed)
                    textBox.Text = FormatNumber(newQty);

                ScheduleSupabaseUpdate(inventory);
            }
            catch (ObjectDisposedException)
            {
                // TextBox foi descartado, ignorar
            }
        }

        private void SupabaseMinusButton_Click(object sender, EventArgs e)
        {
            var btn = sender as Button;
            if (btn == null || btn.IsDisposed) return;
            var inventory = btn.Tag as SupabaseInventoryEntry;
            if (inventory == null || inventory.Quantity <= 0) return;

            inventory.Quantity--;

            try
            {
                var parent = btn.Parent as Panel;
                var qtyTextBox = parent?.Controls.OfType<TextBox>().FirstOrDefault(t => t.Name.StartsWith("sqty_"));
                if (qtyTextBox != null && !qtyTextBox.IsDisposed) 
                    qtyTextBox.Text = FormatNumber(inventory.Quantity);
            }
            catch (ObjectDisposedException) { }

            ScheduleSupabaseUpdate(inventory);
        }

        private void SupabasePlusButton_Click(object sender, EventArgs e)
        {
            var btn = sender as Button;
            if (btn == null || btn.IsDisposed) return;
            var inventory = btn.Tag as SupabaseInventoryEntry;
            if (inventory == null) return;

            inventory.Quantity++;

            try
            {
                var parent = btn.Parent as Panel;
                var qtyTextBox = parent?.Controls.OfType<TextBox>().FirstOrDefault(t => t.Name.StartsWith("sqty_"));
                if (qtyTextBox != null && !qtyTextBox.IsDisposed) 
                    qtyTextBox.Text = FormatNumber(inventory.Quantity);
            }
            catch (ObjectDisposedException) { }

            ScheduleSupabaseUpdate(inventory);
        }

        private void ScheduleSupabaseUpdate(SupabaseInventoryEntry inventory)
        {
            string key = $"supabase_{inventory.Id}";

            if (_debounceTokens.ContainsKey(key))
            {
                _debounceTokens[key].Cancel();
                _debounceTokens[key].Dispose();
            }

            var cts = new CancellationTokenSource();
            _debounceTokens[key] = cts;

            Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(DEBOUNCE_DELAY_MS, cts.Token);
                    
                    if (!cts.Token.IsCancellationRequested)
                    {
                        var result = await SupabaseManager.Instance.UpdateInventoryQuantityAsync(inventory.Id, inventory.Quantity);
                        
                        if (result)
                        {
                            OnLogMessage($"✅ Estoque atualizado: {inventory.Quantity}");
                            
                            // Se estoque ficou zerado, deletar o registro (mover para contas vazias)
                            if (inventory.Quantity == 0)
                            {
                                await MoveToEmptyAccountsAsync(inventory);
                            }
                        }
                        else
                            OnLogWarning($"⚠️ Erro ao salvar estoque");
                        
                        if (_debounceTokens.ContainsKey(key))
                            _debounceTokens.Remove(key);
                    }
                }
                catch (TaskCanceledException)
                {
                }
            });
        }

        /// <summary>
        /// Move conta para contas vazias (deleta o registro de inventário)
        /// </summary>
        private async Task MoveToEmptyAccountsAsync(SupabaseInventoryEntry inventory)
        {
            try
            {
                // Deletar o registro do inventário
                var success = await SupabaseManager.Instance.DeleteInventoryAsync(inventory.Id);
                
                if (success)
                {
                    OnLogMessage($"📤 {inventory.Username} movido para Contas Vazias (estoque zerado)");
                    
                    // Notificar que a conta foi movida
                    OnAccountMovedToEmpty?.Invoke(this, inventory);
                }
            }
            catch (Exception ex)
            {
                OnLogError($"Erro ao mover conta: {ex.Message}");
            }
        }

        // Event helpers
        protected virtual void OnLogMessage(string message) => LogMessage?.Invoke(this, message);
        protected virtual void OnLogWarning(string message) => LogWarning?.Invoke(this, message);
        protected virtual void OnLogError(string message) => LogError?.Invoke(this, message);
    }
}

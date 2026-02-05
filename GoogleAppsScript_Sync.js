/**
 * Google Apps Script - Sincronização Google Sheets <-> Supabase
 * 
 * INSTRUÇÕES DE INSTALAÇÃO:
 * 1. Abra sua planilha no Google Sheets
 * 2. Vá em Extensões > Apps Script
 * 3. Cole este código
 * 4. Configure as constantes abaixo com suas credenciais
 * 5. Execute setupTriggers() uma vez para configurar sincronização automática
 * 
 * ESTRUTURA ESPERADA DA PLANILHA:
 * - Cada aba = um jogo (nome da aba = nome do jogo)
 * - Coluna A: Cookie
 * - Coluna B: Usuário (username)
 * - Coluna C: Produto (nome do item)
 * - Coluna D: Quantidade
 */

// ==================== CONFIGURAÇÃO ====================
const SUPABASE_URL = "https://exiwlqyojynbdcvozree.supabase.co";
const SUPABASE_KEY = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJpc3MiOiJzdXBhYmFzZSIsInJlZiI6ImV4aXdscXlvanluYmRjdm96cmVlIiwicm9sZSI6ImFub24iLCJpYXQiOjE3NzAyMTkzNzQsImV4cCI6MjA4NTc5NTM3NH0.unnN1IBwlEcD0lKamelDA3K0EuwcFmEUfs-Oh5KOUCk";

// Mapeamento de nomes de jogos na planilha para IDs no Supabase
// Preencha conforme seus jogos cadastrados
const GAME_NAME_TO_ID = {
  // "NOME_NA_PLANILHA": ID_NO_SUPABASE
  // Exemplo:
  // "BRAINROT 100M/S": 1,
  // "BRAINROT 1B/S": 2,
};

// ==================== FUNÇÕES PRINCIPAIS ====================

/**
 * Sincroniza TODA a planilha com o Supabase
 * Pode ser executada manualmente ou por trigger
 */
function syncAllToSupabase() {
  const ss = SpreadsheetApp.getActiveSpreadsheet();
  const sheets = ss.getSheets();
  
  let totalSynced = 0;
  
  for (const sheet of sheets) {
    const sheetName = sheet.getName();
    
    // Pular abas de configuração ou sumário
    if (sheetName.startsWith("_") || sheetName.toLowerCase() === "config") {
      continue;
    }
    
    const synced = syncSheetToSupabase(sheet);
    totalSynced += synced;
  }
  
  Logger.log(`✅ Sincronização completa: ${totalSynced} registros`);
  return totalSynced;
}

/**
 * Sincroniza uma aba específica com o Supabase
 */
function syncSheetToSupabase(sheet) {
  const sheetName = sheet.getName();
  const data = sheet.getDataRange().getValues();
  
  if (data.length < 2) return 0; // Sem dados (só header)
  
  // Detectar estrutura da planilha
  const headers = data[0].map(h => h.toString().toLowerCase().trim());
  const colUsuario = findColumn(headers, ["usuário", "usuario", "user", "username"]);
  const colProduto = findColumn(headers, ["produto", "product", "item"]);
  const colQuantidade = findColumn(headers, ["quantidade", "qty", "quantity", "estoque"]);
  
  if (colUsuario === -1 || colProduto === -1 || colQuantidade === -1) {
    Logger.log(`⚠️ Aba "${sheetName}": Colunas não encontradas`);
    return 0;
  }
  
  // Buscar ou criar jogo no Supabase
  let gameId = GAME_NAME_TO_ID[sheetName];
  if (!gameId) {
    gameId = getOrCreateGame(sheetName);
    if (!gameId) {
      Logger.log(`❌ Erro ao criar jogo: ${sheetName}`);
      return 0;
    }
  }
  
  // Processar cada linha
  let synced = 0;
  for (let i = 1; i < data.length; i++) {
    const row = data[i];
    const username = row[colUsuario]?.toString().trim();
    const itemName = row[colProduto]?.toString().trim();
    const quantity = parseInt(row[colQuantidade]) || 0;
    
    if (!username || !itemName) continue;
    
    // Sincronizar com Supabase
    if (syncInventoryItem(username, gameId, itemName, quantity)) {
      synced++;
    }
  }
  
  Logger.log(`📦 Aba "${sheetName}": ${synced} registros sincronizados`);
  return synced;
}

/**
 * Sincroniza um item específico do inventário
 */
function syncInventoryItem(username, gameId, itemName, quantity) {
  try {
    // 1. Buscar ou criar o item
    let itemId = getOrCreateItem(gameId, itemName);
    if (!itemId) return false;
    
    // 2. Upsert no inventário
    const url = `${SUPABASE_URL}/rest/v1/inventory?username=eq.${encodeURIComponent(username)}&item_id=eq.${itemId}`;
    
    // Verificar se existe
    const existing = supabaseGet(url);
    
    if (existing && existing.length > 0) {
      // Update se quantidade diferente
      if (existing[0].quantity !== quantity) {
        const updateUrl = `${SUPABASE_URL}/rest/v1/inventory?id=eq.${existing[0].id}`;
        supabasePatch(updateUrl, { quantity: quantity });
      }
    } else {
      // Insert
      const insertUrl = `${SUPABASE_URL}/rest/v1/inventory`;
      supabasePost(insertUrl, {
        username: username,
        item_id: itemId,
        quantity: quantity
      });
    }
    
    return true;
  } catch (e) {
    Logger.log(`❌ Erro sync ${username}/${itemName}: ${e.message}`);
    return false;
  }
}

// ==================== SINCRONIZAÇÃO INVERSA (Supabase -> Sheets) ====================

/**
 * Baixa dados do Supabase e atualiza a planilha
 */
function syncFromSupabase() {
  const ss = SpreadsheetApp.getActiveSpreadsheet();
  
  // Buscar todos os jogos
  const games = supabaseGet(`${SUPABASE_URL}/rest/v1/games?select=*`);
  if (!games) return;
  
  for (const game of games) {
    // Buscar itens do jogo
    const items = supabaseGet(`${SUPABASE_URL}/rest/v1/game_items?game_id=eq.${game.id}&select=*`);
    if (!items || items.length === 0) continue;
    
    // Buscar inventário de cada item
    const itemIds = items.map(i => i.id).join(',');
    const inventory = supabaseGet(`${SUPABASE_URL}/rest/v1/inventory?item_id=in.(${itemIds})&select=*`);
    
    // Encontrar ou criar aba
    let sheet = ss.getSheetByName(game.name);
    if (!sheet) {
      sheet = ss.insertSheet(game.name);
      // Criar headers
      sheet.getRange(1, 1, 1, 4).setValues([["Cookie", "Usuário", "Produto", "Quantidade"]]);
    }
    
    // Atualizar dados na aba
    updateSheetFromInventory(sheet, items, inventory);
  }
  
  Logger.log("✅ Sincronização do Supabase concluída");
}

/**
 * Atualiza uma aba com dados do inventário
 */
function updateSheetFromInventory(sheet, items, inventory) {
  const data = sheet.getDataRange().getValues();
  const headers = data[0];
  
  // Encontrar colunas
  const colUsuario = findColumn(headers.map(h => h.toString().toLowerCase()), ["usuário", "usuario", "user", "username"]);
  const colProduto = findColumn(headers.map(h => h.toString().toLowerCase()), ["produto", "product", "item"]);
  const colQuantidade = findColumn(headers.map(h => h.toString().toLowerCase()), ["quantidade", "qty", "quantity", "estoque"]);
  
  if (colUsuario === -1 || colProduto === -1 || colQuantidade === -1) return;
  
  // Criar mapa de itens
  const itemMap = {};
  items.forEach(i => itemMap[i.id] = i.name);
  
  // Criar mapa de inventário
  const invMap = {};
  inventory.forEach(inv => {
    const key = `${inv.username}|${itemMap[inv.item_id]}`;
    invMap[key] = inv.quantity;
  });
  
  // Atualizar linhas existentes
  for (let i = 1; i < data.length; i++) {
    const username = data[i][colUsuario]?.toString().trim();
    const itemName = data[i][colProduto]?.toString().trim();
    
    if (!username || !itemName) continue;
    
    const key = `${username}|${itemName}`;
    if (invMap[key] !== undefined && invMap[key] !== data[i][colQuantidade]) {
      sheet.getRange(i + 1, colQuantidade + 1).setValue(invMap[key]);
    }
  }
}

// ==================== TRIGGER AO EDITAR ====================

/**
 * Trigger que executa quando uma célula é editada
 * Sincroniza apenas o item modificado
 */
function onEdit(e) {
  const sheet = e.source.getActiveSheet();
  const range = e.range;
  const row = range.getRow();
  const col = range.getColumn();
  
  // Ignorar header
  if (row === 1) return;
  
  // Pegar headers
  const headers = sheet.getRange(1, 1, 1, sheet.getLastColumn()).getValues()[0];
  const headersLower = headers.map(h => h.toString().toLowerCase().trim());
  
  const colQuantidade = findColumn(headersLower, ["quantidade", "qty", "quantity", "estoque"]) + 1;
  
  // Só sincronizar se editou a coluna de quantidade
  if (col !== colQuantidade) return;
  
  // Pegar dados da linha
  const rowData = sheet.getRange(row, 1, 1, sheet.getLastColumn()).getValues()[0];
  
  const colUsuario = findColumn(headersLower, ["usuário", "usuario", "user", "username"]);
  const colProduto = findColumn(headersLower, ["produto", "product", "item"]);
  
  const username = rowData[colUsuario]?.toString().trim();
  const itemName = rowData[colProduto]?.toString().trim();
  const quantity = parseInt(e.value) || 0;
  
  if (!username || !itemName) return;
  
  // Buscar game ID
  const sheetName = sheet.getName();
  let gameId = GAME_NAME_TO_ID[sheetName];
  if (!gameId) {
    gameId = getGameIdByName(sheetName);
  }
  
  if (gameId) {
    syncInventoryItem(username, gameId, itemName, quantity);
    Logger.log(`🔄 Sync: ${username} - ${itemName} = ${quantity}`);
  }
}

// ==================== FUNÇÕES AUXILIARES ====================

function findColumn(headers, possibleNames) {
  for (let i = 0; i < headers.length; i++) {
    if (possibleNames.includes(headers[i])) {
      return i;
    }
  }
  return -1;
}

function getOrCreateGame(name) {
  // Buscar existente
  const existing = supabaseGet(`${SUPABASE_URL}/rest/v1/games?name=eq.${encodeURIComponent(name)}`);
  if (existing && existing.length > 0) {
    return existing[0].id;
  }
  
  // Criar novo
  const result = supabasePost(`${SUPABASE_URL}/rest/v1/games`, { name: name }, true);
  return result ? result[0]?.id : null;
}

function getGameIdByName(name) {
  const result = supabaseGet(`${SUPABASE_URL}/rest/v1/games?name=eq.${encodeURIComponent(name)}`);
  return result && result.length > 0 ? result[0].id : null;
}

function getOrCreateItem(gameId, name) {
  // Buscar existente
  const existing = supabaseGet(`${SUPABASE_URL}/rest/v1/game_items?game_id=eq.${gameId}&name=eq.${encodeURIComponent(name)}`);
  if (existing && existing.length > 0) {
    return existing[0].id;
  }
  
  // Criar novo
  const result = supabasePost(`${SUPABASE_URL}/rest/v1/game_items`, { 
    game_id: gameId, 
    name: name 
  }, true);
  return result ? result[0]?.id : null;
}

// ==================== FUNÇÕES HTTP ====================

function supabaseGet(url) {
  try {
    const options = {
      method: "get",
      headers: {
        "apikey": SUPABASE_KEY,
        "Authorization": `Bearer ${SUPABASE_KEY}`,
        "Content-Type": "application/json"
      },
      muteHttpExceptions: true
    };
    
    const response = UrlFetchApp.fetch(url, options);
    if (response.getResponseCode() === 200) {
      return JSON.parse(response.getContentText());
    }
    Logger.log(`GET Error ${response.getResponseCode()}: ${response.getContentText()}`);
    return null;
  } catch (e) {
    Logger.log(`GET Exception: ${e.message}`);
    return null;
  }
}

function supabasePost(url, data, returnData = false) {
  try {
    const options = {
      method: "post",
      headers: {
        "apikey": SUPABASE_KEY,
        "Authorization": `Bearer ${SUPABASE_KEY}`,
        "Content-Type": "application/json",
        "Prefer": returnData ? "return=representation" : "return=minimal"
      },
      payload: JSON.stringify(data),
      muteHttpExceptions: true
    };
    
    const response = UrlFetchApp.fetch(url, options);
    if (response.getResponseCode() >= 200 && response.getResponseCode() < 300) {
      return returnData ? JSON.parse(response.getContentText()) : true;
    }
    Logger.log(`POST Error ${response.getResponseCode()}: ${response.getContentText()}`);
    return null;
  } catch (e) {
    Logger.log(`POST Exception: ${e.message}`);
    return null;
  }
}

function supabasePatch(url, data) {
  try {
    const options = {
      method: "patch",
      headers: {
        "apikey": SUPABASE_KEY,
        "Authorization": `Bearer ${SUPABASE_KEY}`,
        "Content-Type": "application/json",
        "Prefer": "return=minimal"
      },
      payload: JSON.stringify(data),
      muteHttpExceptions: true
    };
    
    const response = UrlFetchApp.fetch(url, options);
    return response.getResponseCode() >= 200 && response.getResponseCode() < 300;
  } catch (e) {
    Logger.log(`PATCH Exception: ${e.message}`);
    return false;
  }
}

// ==================== SETUP ====================

/**
 * Configura triggers automáticos
 * Execute esta função uma vez após instalar o script
 */
function setupTriggers() {
  // Remover triggers existentes
  const triggers = ScriptApp.getProjectTriggers();
  triggers.forEach(t => ScriptApp.deleteTrigger(t));
  
  // Trigger ao editar (sincronização instantânea)
  ScriptApp.newTrigger('onEdit')
    .forSpreadsheet(SpreadsheetApp.getActive())
    .onEdit()
    .create();
  
  // Trigger a cada 5 minutos (sincronização completa do Supabase -> Sheets)
  ScriptApp.newTrigger('syncFromSupabase')
    .timeBased()
    .everyMinutes(5)
    .create();
  
  Logger.log("✅ Triggers configurados!");
}

/**
 * Remove todos os triggers
 */
function removeTriggers() {
  const triggers = ScriptApp.getProjectTriggers();
  triggers.forEach(t => ScriptApp.deleteTrigger(t));
  Logger.log("🗑️ Triggers removidos");
}

// ==================== MENU PERSONALIZADO ====================

function onOpen() {
  const ui = SpreadsheetApp.getUi();
  ui.createMenu('🔄 Supabase Sync')
    .addItem('Sincronizar Tudo → Supabase', 'syncAllToSupabase')
    .addItem('Baixar do Supabase', 'syncFromSupabase')
    .addSeparator()
    .addItem('Configurar Triggers', 'setupTriggers')
    .addItem('Remover Triggers', 'removeTriggers')
    .addToUi();
}

/**
 * =====================================================
 * Google Apps Script - Sincronização Sheets <-> Supabase
 * ESTOQUE BLOX BRASIL
 * =====================================================
 * 
 * INSTRUÇÕES DE INSTALAÇÃO:
 * 1. Abra sua planilha no Google Sheets
 * 2. Vá em Extensões > Apps Script
 * 3. Cole este código (substitua todo o conteúdo)
 * 4. Clique em Salvar
 * 5. Execute a função "setupTriggers" uma vez
 * 6. Autorize as permissões quando solicitado
 * 
 * ESTRUTURA DA PLANILHA:
 * - Cada aba = um jogo (identificado pelo GID)
 * - Coluna A: Cookie
 * - Coluna B: Usuário (username)  
 * - Coluna C: Produto (nome do item)
 * - Coluna D: Quantidade
 * - Coluna E: (vazia)
 * - Coluna F: Lista de todos os itens do jogo
 */

// ==================== CONFIGURAÇÃO SUPABASE ====================
const SUPABASE_URL = "https://exiwlqyojynbdcvozree.supabase.co";
const SUPABASE_KEY = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJpc3MiOiJzdXBhYmFzZSIsInJlZiI6ImV4aXdscXlvanluYmRjdm96cmVlIiwicm9sZSI6ImFub24iLCJpYXQiOjE3NzAyMTkzNzQsImV4cCI6MjA4NTc5NTM3NH0.unnN1IBwlEcD0lKamelDA3K0EuwcFmEUfs-Oh5KOUCk";

// ==================== MAPEAMENTO DE JOGOS (GID -> Nome) ====================
// GID é o identificador da aba no Google Sheets (aparece na URL como #gid=XXXXX)
const GAMES_CONFIG = {
  1637218332: { name: "Steal A Brainrot", placeId: "109983668079237" },
  1036319857: { name: "Escape Tsunami", placeId: "131623223084840" },
  329604601: { name: "Blox Fruits", placeId: "2753915549" },
  1699388936: { name: "Murder Mystery 2", placeId: "142823291" },
  47237033: { name: "Grow A Garden", placeId: "126884695634066" },
  1554191605: { name: "Plants vs Brainrots", placeId: "127742093697776" },
  750499672: { name: "Levantar Animais", placeId: "122826953758426" },
  1562392488: { name: "Toilet Tower Defense", placeId: "13775256536" },
  1895042406: { name: "Creatures of Sonaria", placeId: "5233782396" },
  697309269: { name: "Tap Simulator", placeId: "75992362647444" },
  532010938: { name: "Break Lucky Block", placeId: "124311897657957" },
  660585678: { name: "ROBUX", placeId: "" }
};

// Cache local para evitar múltiplas requisições
let _gamesCache = null;
let _itemsCache = {};

// ==================== FUNÇÕES PRINCIPAIS ====================

/**
 * Sincroniza APENAS as contas (Cookie + Username) para a tabela accounts
 * Use esta função primeiro para importar todas as contas!
 */
function syncAccountsOnly() {
  const ss = SpreadsheetApp.getActiveSpreadsheet();
  const sheets = ss.getSheets();
  
  const accountsToSync = {}; // username -> cookie
  
  Logger.log("🔄 Coletando contas de todas as abas...");
  
  for (const sheet of sheets) {
    const gid = sheet.getSheetId();
    const gameConfig = GAMES_CONFIG[gid];
    
    if (!gameConfig) continue;
    
    const data = sheet.getDataRange().getValues();
    if (data.length < 2) continue;
    
    const COL_COOKIE = 0;
    const COL_USUARIO = 1;
    
    for (let i = 1; i < data.length; i++) {
      const cookie = data[i][COL_COOKIE]?.toString().trim();
      const username = data[i][COL_USUARIO]?.toString().trim();
      
      if (username && cookie && cookie !== "" && !cookie.toLowerCase().startsWith("cookie")) {
        // Só adicionar se ainda não temos ou se o cookie atual é mais completo
        if (!accountsToSync[username] || accountsToSync[username].length < cookie.length) {
          accountsToSync[username] = cookie;
        }
      }
    }
  }
  
  const totalAccounts = Object.keys(accountsToSync).length;
  Logger.log(`📋 ${totalAccounts} contas únicas encontradas`);
  
  let synced = 0;
  let errors = 0;
  
  for (const username in accountsToSync) {
    if (syncAccount(username, accountsToSync[username])) {
      synced++;
      if (synced % 50 === 0) {
        Logger.log(`   ⏳ ${synced}/${totalAccounts} contas...`);
      }
    } else {
      errors++;
    }
  }
  
  Logger.log(`\n========================================`);
  Logger.log(`✅ Contas sincronizadas: ${synced}`);
  if (errors > 0) {
    Logger.log(`❌ Erros: ${errors}`);
  }
  
  SpreadsheetApp.getUi().alert(
    'Sincronização de Contas',
    `✅ ${synced} contas sincronizadas com o Supabase\n❌ ${errors} erros`,
    SpreadsheetApp.getUi().ButtonSet.OK
  );
  
  return { synced: synced, errors: errors };
}

/**
 * Sincroniza TODA a planilha com o Supabase
 */
function syncAllToSupabase() {
  const ss = SpreadsheetApp.getActiveSpreadsheet();
  const sheets = ss.getSheets();
  
  // Limpar cache
  _gamesCache = null;
  _itemsCache = {};
  
  let totalSynced = 0;
  let errors = [];
  
  for (const sheet of sheets) {
    const gid = sheet.getSheetId();
    const gameConfig = GAMES_CONFIG[gid];
    
    if (!gameConfig) {
      Logger.log(`⏭️ Aba ignorada (GID ${gid} não mapeado): ${sheet.getName()}`);
      continue;
    }
    
    try {
      const synced = syncSheetToSupabase(sheet, gameConfig.name);
      totalSynced += synced;
      Logger.log(`✅ ${gameConfig.name}: ${synced} registros`);
    } catch (e) {
      errors.push(`${gameConfig.name}: ${e.message}`);
      Logger.log(`❌ Erro em ${gameConfig.name}: ${e.message}`);
    }
  }
  
  const message = `Sincronização completa: ${totalSynced} registros`;
  Logger.log(`\n========================================`);
  Logger.log(`✅ ${message}`);
  if (errors.length > 0) {
    Logger.log(`⚠️ Erros: ${errors.length}`);
    errors.forEach(e => Logger.log(`   - ${e}`));
  }
  
  return { synced: totalSynced, errors: errors };
}

/**
 * Sincroniza uma aba específica com o Supabase
 */
function syncSheetToSupabase(sheet, gameName) {
  const data = sheet.getDataRange().getValues();
  
  if (data.length < 2) return 0;
  
  // Colunas fixas conforme estrutura
  const COL_COOKIE = 0;     // Coluna A (índice 0)
  const COL_USUARIO = 1;    // Coluna B (índice 1)
  const COL_PRODUTO = 2;    // Coluna C (índice 2)
  const COL_QUANTIDADE = 3; // Coluna D (índice 3)
  
  // Buscar ou criar jogo no Supabase
  const gameId = getOrCreateGame(gameName);
  if (!gameId) {
    throw new Error(`Não foi possível criar/encontrar jogo: ${gameName}`);
  }
  
  // Processar cada linha (pular header)
  let synced = 0;
  const batchUpdates = [];
  const accountsToSync = {}; // username -> cookie (para evitar duplicatas)
  
  for (let i = 1; i < data.length; i++) {
    const row = data[i];
    const cookie = row[COL_COOKIE]?.toString().trim();
    const username = row[COL_USUARIO]?.toString().trim();
    const itemName = row[COL_PRODUTO]?.toString().trim();
    const quantity = parseInt(row[COL_QUANTIDADE]) || 0;
    
    // Pular linhas vazias ou inválidas
    if (!username || !itemName || username === "" || itemName === "") continue;
    
    // Coletar cookie para sincronizar na tabela accounts
    if (cookie && cookie !== "" && !cookie.startsWith("Cookie")) {
      accountsToSync[username] = cookie;
    }
    
    batchUpdates.push({
      username: username,
      itemName: itemName,
      quantity: quantity,
      gameId: gameId
    });
  }
  
  // 1. Sincronizar accounts (cookie + username)
  let accountsSynced = 0;
  for (const username in accountsToSync) {
    if (syncAccount(username, accountsToSync[username])) {
      accountsSynced++;
    }
  }
  if (accountsSynced > 0) {
    Logger.log(`   👤 ${accountsSynced} contas sincronizadas`);
  }
  
  // 2. Processar inventory em lotes
  for (const update of batchUpdates) {
    if (syncInventoryItem(update.username, update.gameId, update.itemName, update.quantity)) {
      synced++;
    }
  }
  
  return synced;
}

/**
 * Sincroniza uma conta na tabela accounts (upsert por username)
 */
function syncAccount(username, cookie) {
  try {
    // Verificar se já existe
    const existingUrl = `${SUPABASE_URL}/rest/v1/accounts?username=eq.${encodeURIComponent(username)}`;
    const existing = supabaseGet(existingUrl);
    
    if (existing && existing.length > 0) {
      // Atualizar cookie se diferente
      if (existing[0].cookie !== cookie) {
        const updateUrl = `${SUPABASE_URL}/rest/v1/accounts?username=eq.${encodeURIComponent(username)}`;
        return supabasePatch(updateUrl, { cookie: cookie, updated_at: new Date().toISOString() });
      }
      return true; // Já está igual
    } else {
      // Inserir nova conta
      const insertUrl = `${SUPABASE_URL}/rest/v1/accounts`;
      return supabasePost(insertUrl, {
        username: username,
        cookie: cookie
      }) !== null;
    }
  } catch (e) {
    Logger.log(`❌ Erro sync account ${username}: ${e.message}`);
    return false;
  }
}

/**
 * Sincroniza um item específico do inventário
 */
function syncInventoryItem(username, gameId, itemName, quantity) {
  try {
    // 1. Buscar ou criar o item
    const itemId = getOrCreateItem(gameId, itemName);
    if (!itemId) {
      Logger.log(`⚠️ Não foi possível criar item: ${itemName}`);
      return false;
    }
    
    // 2. Verificar se já existe no inventário
    const existingUrl = `${SUPABASE_URL}/rest/v1/inventory?username=eq.${encodeURIComponent(username)}&item_id=eq.${itemId}`;
    const existing = supabaseGet(existingUrl);
    
    if (existing && existing.length > 0) {
      // Atualizar apenas se quantidade diferente
      if (existing[0].quantity !== quantity) {
        const updateUrl = `${SUPABASE_URL}/rest/v1/inventory?id=eq.${existing[0].id}`;
        return supabasePatch(updateUrl, { quantity: quantity });
      }
      return true; // Já está igual
    } else {
      // Inserir novo registro
      const insertUrl = `${SUPABASE_URL}/rest/v1/inventory`;
      return supabasePost(insertUrl, {
        username: username,
        item_id: itemId,
        quantity: quantity
      }) !== null;
    }
  } catch (e) {
    Logger.log(`❌ Erro sync ${username}/${itemName}: ${e.message}`);
    return false;
  }
}

// ==================== TRIGGER AO EDITAR (TEMPO REAL) ====================

/**
 * Executa automaticamente quando uma célula é editada
 */
function onEdit(e) {
  if (!e || !e.range) return;
  
  const sheet = e.source.getActiveSheet();
  const range = e.range;
  const row = range.getRow();
  const col = range.getColumn();
  
  // Ignorar header (linha 1)
  if (row === 1) return;
  
  // Só sincronizar se editou a coluna D (Quantidade)
  if (col !== 4) return;
  
  const gid = sheet.getSheetId();
  const gameConfig = GAMES_CONFIG[gid];
  
  // Ignorar abas não mapeadas
  if (!gameConfig) return;
  
  // Pegar dados da linha
  const rowData = sheet.getRange(row, 1, 1, 4).getValues()[0];
  const username = rowData[1]?.toString().trim();  // Coluna B
  const itemName = rowData[2]?.toString().trim();  // Coluna C
  const quantity = parseInt(e.value) || 0;
  
  if (!username || !itemName) return;
  
  // Sincronizar com Supabase
  const gameId = getOrCreateGame(gameConfig.name);
  if (gameId) {
    syncInventoryItem(username, gameId, itemName, quantity);
    Logger.log(`🔄 Sync: ${username} - ${itemName} = ${quantity} (${gameConfig.name})`);
  }
}

// ==================== FUNÇÕES AUXILIARES ====================

/**
 * Busca ou cria um jogo no Supabase
 */
function getOrCreateGame(gameName) {
  // Usar cache se disponível
  if (!_gamesCache) {
    _gamesCache = supabaseGet(`${SUPABASE_URL}/rest/v1/games?select=*`) || [];
  }
  
  // Buscar no cache
  const existing = _gamesCache.find(g => 
    g.name.toLowerCase() === gameName.toLowerCase()
  );
  
  if (existing) return existing.id;
  
  // Criar novo jogo
  const result = supabasePost(`${SUPABASE_URL}/rest/v1/games`, { name: gameName }, true);
  if (result && result.length > 0) {
    _gamesCache.push(result[0]);
    return result[0].id;
  }
  
  return null;
}

/**
 * Busca ou cria um item no Supabase
 */
function getOrCreateItem(gameId, itemName) {
  const cacheKey = `${gameId}`;
  
  // Carregar cache de itens do jogo se necessário
  if (!_itemsCache[cacheKey]) {
    _itemsCache[cacheKey] = supabaseGet(
      `${SUPABASE_URL}/rest/v1/game_items?game_id=eq.${gameId}&select=*`
    ) || [];
  }
  
  // Buscar no cache
  const existing = _itemsCache[cacheKey].find(i => 
    i.name.toLowerCase() === itemName.toLowerCase()
  );
  
  if (existing) return existing.id;
  
  // Criar novo item
  const result = supabasePost(`${SUPABASE_URL}/rest/v1/game_items`, { 
    game_id: gameId, 
    name: itemName 
  }, true);
  
  if (result && result.length > 0) {
    _itemsCache[cacheKey].push(result[0]);
    return result[0].id;
  }
  
  return null;
}

// ==================== FUNÇÕES HTTP SUPABASE ====================

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
    const code = response.getResponseCode();
    
    if (code === 200) {
      return JSON.parse(response.getContentText());
    }
    
    if (code !== 404) {
      Logger.log(`GET ${code}: ${response.getContentText().substring(0, 200)}`);
    }
    return null;
  } catch (e) {
    Logger.log(`GET Error: ${e.message}`);
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
    const code = response.getResponseCode();
    
    if (code >= 200 && code < 300) {
      return returnData ? JSON.parse(response.getContentText()) : true;
    }
    
    Logger.log(`POST ${code}: ${response.getContentText().substring(0, 200)}`);
    return null;
  } catch (e) {
    Logger.log(`POST Error: ${e.message}`);
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
    Logger.log(`PATCH Error: ${e.message}`);
    return false;
  }
}

// ==================== CONFIGURAÇÃO DE TRIGGERS ====================

/**
 * Configura triggers automáticos
 * EXECUTE ESTA FUNÇÃO UMA VEZ APÓS INSTALAR O SCRIPT
 */
function setupTriggers() {
  // Remover triggers existentes deste projeto
  const triggers = ScriptApp.getProjectTriggers();
  triggers.forEach(t => ScriptApp.deleteTrigger(t));
  
  // Trigger ao editar (sincronização em tempo real Sheets -> Supabase)
  ScriptApp.newTrigger('onEdit')
    .forSpreadsheet(SpreadsheetApp.getActive())
    .onEdit()
    .create();
  
  Logger.log("✅ Trigger configurado com sucesso!");
  Logger.log("   - onEdit: Sincroniza Sheets → Supabase em tempo real");
  
  SpreadsheetApp.getUi().alert(
    '✅ Trigger Configurado!',
    'Sincronização automática ativada:\n\n' +
    '• Edições na planilha → Supabase (tempo real)\n\n' +
    'O Supabase NÃO enviará dados para a planilha.',
    SpreadsheetApp.getUi().ButtonSet.OK
  );
}

/**
 * Remove todos os triggers
 */
function removeTriggers() {
  const triggers = ScriptApp.getProjectTriggers();
  const count = triggers.length;
  triggers.forEach(t => ScriptApp.deleteTrigger(t));
  
  Logger.log(`🗑️ ${count} triggers removidos`);
  
  SpreadsheetApp.getUi().alert(
    'Triggers Removidos',
    `${count} trigger(s) foram removidos.\nA sincronização automática foi desativada.`,
    SpreadsheetApp.getUi().ButtonSet.OK
  );
}

// ==================== MENU PERSONALIZADO ====================

/**
 * Cria menu personalizado quando a planilha é aberta
 */
function onOpen() {
  const ui = SpreadsheetApp.getUi();
  ui.createMenu('🔄 Supabase Sync')
    .addItem('👤 Sincronizar Contas (Cookie)', 'syncAccountsOnly')
    .addItem('📤 Sincronizar Tudo → Supabase', 'syncAllToSupabase')
    .addItem('📤 Sincronizar Aba Atual → Supabase', 'syncCurrentSheet')
    .addSeparator()
    .addItem('⚙️ Configurar Trigger Automático', 'setupTriggers')
    .addItem('🗑️ Remover Triggers', 'removeTriggers')
    .addSeparator()
    .addItem('🔍 Verificar Configuração', 'checkConfiguration')
    .addToUi();
}

/**
 * Verifica a configuração e mostra status
 */
function checkConfiguration() {
  const ss = SpreadsheetApp.getActiveSpreadsheet();
  const sheets = ss.getSheets();
  
  let report = "📋 RELATÓRIO DE CONFIGURAÇÃO\n\n";
  report += "ABAS MAPEADAS:\n";
  
  let mapped = 0;
  let unmapped = 0;
  
  for (const sheet of sheets) {
    const gid = sheet.getSheetId();
    const gameConfig = GAMES_CONFIG[gid];
    
    if (gameConfig) {
      report += `✅ ${sheet.getName()} → ${gameConfig.name}\n`;
      mapped++;
    } else {
      report += `⏭️ ${sheet.getName()} (GID: ${gid}) - NÃO MAPEADA\n`;
      unmapped++;
    }
  }
  
  report += `\n📊 RESUMO:\n`;
  report += `• Abas mapeadas: ${mapped}\n`;
  report += `• Abas ignoradas: ${unmapped}\n`;
  
  // Testar conexão com Supabase
  report += `\n🌐 CONEXÃO SUPABASE:\n`;
  try {
    const games = supabaseGet(`${SUPABASE_URL}/rest/v1/games?select=id&limit=1`);
    if (games !== null) {
      report += `✅ Conexão OK\n`;
    } else {
      report += `❌ Erro na conexão\n`;
    }
  } catch (e) {
    report += `❌ Erro: ${e.message}\n`;
  }
  
  // Verificar triggers
  const triggers = ScriptApp.getProjectTriggers();
  report += `\n⏰ TRIGGERS ATIVOS: ${triggers.length}\n`;
  triggers.forEach(t => {
    report += `• ${t.getHandlerFunction()}\n`;
  });
  
  Logger.log(report);
  SpreadsheetApp.getUi().alert('Configuração', report, SpreadsheetApp.getUi().ButtonSet.OK);
}

/**
 * Sincroniza apenas a aba atual
 */
function syncCurrentSheet() {
  const sheet = SpreadsheetApp.getActiveSheet();
  const gid = sheet.getSheetId();
  const gameConfig = GAMES_CONFIG[gid];
  
  if (!gameConfig) {
    SpreadsheetApp.getUi().alert(
      'Aba não mapeada',
      `Esta aba (GID: ${gid}) não está configurada para sincronização.`,
      SpreadsheetApp.getUi().ButtonSet.OK
    );
    return;
  }
  
  const synced = syncSheetToSupabase(sheet, gameConfig.name);
  
  SpreadsheetApp.getUi().alert(
    'Sincronização Concluída',
    `${gameConfig.name}: ${synced} registros sincronizados`,
    SpreadsheetApp.getUi().ButtonSet.OK
  );
}

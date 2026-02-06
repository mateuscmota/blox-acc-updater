-- =====================================================
-- SQL para criar tabela de usuários no Supabase
-- Execute este SQL no SQL Editor do Supabase
-- =====================================================

-- Criar tabela de usuários
CREATE TABLE IF NOT EXISTS users (
    id SERIAL PRIMARY KEY,
    username VARCHAR(100) UNIQUE NOT NULL,
    password_hash VARCHAR(255) NOT NULL,
    display_name VARCHAR(100),
    role VARCHAR(50) DEFAULT 'user',  -- 'admin', 'user', 'viewer'
    is_active BOOLEAN DEFAULT true,
    permissions TEXT[],  -- Array de permissões específicas
    last_login TIMESTAMP WITH TIME ZONE,
    created_at TIMESTAMP WITH TIME ZONE DEFAULT NOW(),
    updated_at TIMESTAMP WITH TIME ZONE DEFAULT NOW()
);

-- Criar índice para busca por username
CREATE INDEX IF NOT EXISTS idx_users_username ON users(username);

-- Criar índice para busca por role
CREATE INDEX IF NOT EXISTS idx_users_role ON users(role);

-- Habilitar RLS (Row Level Security)
ALTER TABLE users ENABLE ROW LEVEL SECURITY;

-- Política para permitir leitura (SELECT) de usuários
CREATE POLICY "Allow select for authenticated" ON users
    FOR SELECT USING (true);

-- Política para permitir atualização (UPDATE) de usuários
CREATE POLICY "Allow update for authenticated" ON users
    FOR UPDATE USING (true);

-- Política para permitir inserção (INSERT) de usuários
CREATE POLICY "Allow insert for authenticated" ON users
    FOR INSERT WITH CHECK (true);

-- =====================================================
-- INSERIR USUÁRIO ADMINISTRADOR PADRÃO
-- IMPORTANTE: Altere a senha após o primeiro login!
-- =====================================================

INSERT INTO users (username, password_hash, display_name, role, is_active)
VALUES ('admin', 'admin123', 'Administrador', 'admin', true)
ON CONFLICT (username) DO NOTHING;

-- =====================================================
-- COLUNAS ADICIONAIS PARA FUNCIONALIDADES
-- =====================================================

-- Coluna 'archived' na tabela 'accounts' (para arquivar contas)
ALTER TABLE accounts ADD COLUMN IF NOT EXISTS archived BOOLEAN DEFAULT false;

-- Coluna 'archived' na tabela 'games' (para arquivar jogos)
ALTER TABLE games ADD COLUMN IF NOT EXISTS archived BOOLEAN DEFAULT false;

-- Coluna 'archived' na tabela 'game_items' (para arquivar itens)
ALTER TABLE game_items ADD COLUMN IF NOT EXISTS archived BOOLEAN DEFAULT false;

-- Coluna 'updated_at' na tabela 'games' (para rastrear atualizações)
ALTER TABLE games ADD COLUMN IF NOT EXISTS updated_at TIMESTAMP WITH TIME ZONE DEFAULT NOW();

-- =====================================================
-- LOGIN VIA GOOGLE OAUTH
-- =====================================================

-- Coluna 'email' na tabela users (para login via Google)
ALTER TABLE users ADD COLUMN IF NOT EXISTS email VARCHAR(255);
CREATE INDEX IF NOT EXISTS idx_users_email ON users(email);

-- Tabela de emails permitidos para login via Google
CREATE TABLE IF NOT EXISTS allowed_emails (
    id SERIAL PRIMARY KEY,
    email VARCHAR(255) UNIQUE NOT NULL,
    added_by VARCHAR(100),
    created_at TIMESTAMP WITH TIME ZONE DEFAULT NOW()
);

ALTER TABLE allowed_emails ENABLE ROW LEVEL SECURITY;

CREATE POLICY "Allow select for all" ON allowed_emails
    FOR SELECT USING (true);

CREATE POLICY "Allow insert for all" ON allowed_emails
    FOR INSERT WITH CHECK (true);

-- Exemplo: adicionar email permitido
-- INSERT INTO allowed_emails (email, added_by) VALUES ('usuario@gmail.com', 'admin');

-- Listar emails permitidos:
-- SELECT * FROM allowed_emails;

-- Remover email permitido:
-- DELETE FROM allowed_emails WHERE email = 'usuario@gmail.com';

-- =====================================================
-- COMANDOS ÚTEIS
-- =====================================================

-- Criar novo usuário:
-- INSERT INTO users (username, password_hash, display_name, role)
-- VALUES ('novo_usuario', 'senha123', 'Nome Exibição', 'user');

-- Alterar senha de um usuário:
-- UPDATE users SET password_hash = 'nova_senha' WHERE username = 'usuario';

-- Desativar usuário:
-- UPDATE users SET is_active = false WHERE username = 'usuario';

-- Promover usuário a admin:
-- UPDATE users SET role = 'admin' WHERE username = 'usuario';

-- Listar todos os usuários:
-- SELECT id, username, display_name, role, is_active, last_login FROM users;

-- Restaurar jogo arquivado:
-- UPDATE games SET archived = false WHERE id = <game_id>;

-- Restaurar item arquivado:
-- UPDATE game_items SET archived = false WHERE id = <item_id>;

-- Restaurar conta arquivada:
-- UPDATE accounts SET archived = false WHERE username = 'nome_usuario';

-- =====================================================
-- HISTÓRICO DE ACESSO POR CONTA
-- =====================================================

CREATE TABLE IF NOT EXISTS account_history (
    id SERIAL PRIMARY KEY,
    account_username VARCHAR(100) NOT NULL,
    user_email VARCHAR(255) NOT NULL,
    user_display_name VARCHAR(100),
    action VARCHAR(50) DEFAULT 'launch',
    place_id BIGINT,
    created_at TIMESTAMP WITH TIME ZONE DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS idx_account_history_username ON account_history(account_username);
CREATE INDEX IF NOT EXISTS idx_account_history_created ON account_history(created_at DESC);

ALTER TABLE account_history ENABLE ROW LEVEL SECURITY;

CREATE POLICY "Allow select for all" ON account_history
    FOR SELECT USING (true);

CREATE POLICY "Allow insert for all" ON account_history
    FOR INSERT WITH CHECK (true);

-- Coluna 'game_name' para armazenar o nome do jogo (buscado via API Roblox)
ALTER TABLE account_history ADD COLUMN IF NOT EXISTS game_name VARCHAR(255);

-- =====================================================
-- CONFIGURAÇÕES COMPARTILHADAS (app_config)
-- =====================================================

CREATE TABLE IF NOT EXISTS app_config (
    key VARCHAR(100) PRIMARY KEY,
    value TEXT,
    updated_at TIMESTAMP WITH TIME ZONE DEFAULT NOW()
);

ALTER TABLE app_config ENABLE ROW LEVEL SECURITY;

CREATE POLICY "Allow select for all" ON app_config
    FOR SELECT USING (true);

CREATE POLICY "Allow insert for all" ON app_config
    FOR INSERT WITH CHECK (true);

CREATE POLICY "Allow update for all" ON app_config
    FOR UPDATE USING (true);

-- Inserir cookie inicial do TopUpG
INSERT INTO app_config (key, value) VALUES ('topupg_cookie', 'eyJpdiI6Im5ISGI0YXFYbUpKVkZxcEp1Y0FyaVE9PSIsInZhbHVlIjoiUHdJNDBsdy83VEt4amRSWFYzSUp1OGhiVUV2OURaNnhKWWE3NFcxdkNtY3oyb1NPbEtnc29ObkRMbWh2eEYrV3F1bk5ISi9JUW9vRXhWaFY0b2tubXdxTlJRazdnT2drZXJUK1VyYlZNV1V0QitCZXk4UlhHR3hseHplN2dFRHQiLCJtYWMiOiJlNzJmNDgwZGMyNWUzNzE2YWU4MjExY2I4NWY4NGFlNjRkNzUwNDE4MWI3NjUyZjNlOGFmMTliZDJhN2M0MDFkIiwidGFnIjoiIn0%3D')
ON CONFLICT (key) DO NOTHING;

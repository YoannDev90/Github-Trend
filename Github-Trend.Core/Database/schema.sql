-- GitHub-Trend SQLite Schema
-- Version: 2

CREATE TABLE IF NOT EXISTS trending_cache (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    cache_key TEXT NOT NULL UNIQUE,
    since TEXT NOT NULL,
    language TEXT,
    data_json TEXT NOT NULL,
    cached_at_utc TEXT NOT NULL DEFAULT (datetime('now')),
    expires_at_utc TEXT NOT NULL
);

CREATE INDEX IF NOT EXISTS idx_trending_cache_key ON trending_cache(cache_key);
CREATE INDEX IF NOT EXISTS idx_trending_cache_expires ON trending_cache(expires_at_utc);

CREATE TABLE IF NOT EXISTS repository_details_cache (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    owner TEXT NOT NULL,
    name TEXT NOT NULL,
    data_json TEXT NOT NULL,
    etag TEXT,
    cached_at_utc TEXT NOT NULL DEFAULT (datetime('now')),
    expires_at_utc TEXT NOT NULL,
    UNIQUE(owner, name)
);

CREATE INDEX IF NOT EXISTS idx_repo_details_owner ON repository_details_cache(owner, name);
CREATE INDEX IF NOT EXISTS idx_repo_details_expires ON repository_details_cache(expires_at_utc);

CREATE TABLE IF NOT EXISTS colors_cache (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    data_json TEXT NOT NULL,
    cached_at_utc TEXT NOT NULL DEFAULT (datetime('now')),
    expires_at_utc TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS auth_tokens (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    user_id TEXT NOT NULL,
    github_account_id INTEGER NOT NULL,
    access_token_encrypted TEXT NOT NULL,
    refresh_token_encrypted TEXT,
    expires_at TEXT NOT NULL,
    refresh_token_expires_at TEXT,
    scope_list_json TEXT NOT NULL DEFAULT '[]',
    created_at TEXT NOT NULL,
    updated_at TEXT NOT NULL,
    revoked_at TEXT,
    login TEXT,
    name TEXT,
    email TEXT,
    avatar_url TEXT,
    UNIQUE(user_id, github_account_id)
);

CREATE TABLE IF NOT EXISTS selected_languages (
    id INTEGER PRIMARY KEY CHECK (id = 1),
    languages_json TEXT NOT NULL DEFAULT '[]',
    updated_at TEXT NOT NULL DEFAULT (datetime('now'))
);

CREATE TABLE IF NOT EXISTS user_starred (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    slug TEXT NOT NULL UNIQUE,
    starred_at_utc TEXT NOT NULL DEFAULT (datetime('now'))
);

CREATE TABLE IF NOT EXISTS user_watched (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    slug TEXT NOT NULL UNIQUE,
    watched_at_utc TEXT NOT NULL DEFAULT (datetime('now'))
);

CREATE TABLE IF NOT EXISTS image_cache (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    url_hash TEXT NOT NULL UNIQUE,
    data_blob BLOB NOT NULL,
    cached_at_utc TEXT NOT NULL DEFAULT (datetime('now')),
    expires_at_utc TEXT NOT NULL
);

CREATE INDEX IF NOT EXISTS idx_image_cache_hash ON image_cache(url_hash);
CREATE INDEX IF NOT EXISTS idx_image_cache_expires ON image_cache(expires_at_utc);

CREATE TABLE IF NOT EXISTS dismissed_repos (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    slug TEXT NOT NULL UNIQUE,
    dismissed_at_utc TEXT NOT NULL DEFAULT (datetime('now'))
);

CREATE INDEX IF NOT EXISTS idx_dismissed_slug ON dismissed_repos(slug);

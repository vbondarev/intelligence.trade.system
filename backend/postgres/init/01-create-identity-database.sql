SELECT 'CREATE DATABASE tradesystem_identity'
WHERE NOT EXISTS (
    SELECT FROM pg_database WHERE datname = 'tradesystem_identity'
)\gexec

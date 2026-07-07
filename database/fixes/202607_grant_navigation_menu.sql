-- Replace <APP_DB_USER> with the DB user used by Dashboard connection string if not postgres.

GRANT USAGE ON SCHEMA system TO "<APP_DB_USER>";
GRANT SELECT, INSERT, UPDATE, DELETE ON system.navigation_menu_groups TO "<APP_DB_USER>";
GRANT SELECT, INSERT, UPDATE, DELETE ON system.navigation_menu_items TO "<APP_DB_USER>";

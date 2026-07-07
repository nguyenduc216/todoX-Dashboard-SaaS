SELECT table_schema, table_name
FROM information_schema.tables
WHERE table_schema = 'system'
  AND table_name IN ('navigation_menu_groups', 'navigation_menu_items')
ORDER BY table_name;

SELECT code, title, icon_key, sort_order, default_expanded, is_active
FROM system.navigation_menu_groups
ORDER BY sort_order;

SELECT g.code AS group_code,
       g.title AS group_title,
       i.code AS item_code,
       i.title AS item_title,
       i.href,
       i.icon_key,
       i.module_keys,
       i.visibility_policy,
       i.sort_order,
       i.is_active
FROM system.navigation_menu_groups g
JOIN system.navigation_menu_items i ON i.group_id = g.id
ORDER BY g.sort_order, i.sort_order;

SELECT g.code AS group_code,
       count(i.id) FILTER (WHERE i.is_active) AS active_items
FROM system.navigation_menu_groups g
LEFT JOIN system.navigation_menu_items i ON i.group_id = g.id
GROUP BY g.code, g.sort_order
ORDER BY g.sort_order;

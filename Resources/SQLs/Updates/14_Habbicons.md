# Habicons setup

Stop PlusEMU and back up the database, then apply `14_Habbicons.sql` with a MySQL/MariaDB client to the existing Plus database before starting the new server. SQL updates are manual in PlusEMU. Existing revision files must include the new Habicon and messenger headers; the shipped revisions already do.

The migration creates InnoDB collection, icon and ownership tables, adds `catalog_items.habbicon_id`, and seeds four collections and their catalog offers. Only `duck_duck` (28) is owned by default. Regular icons cost 5 credits, collections cost 40 credits, and collection rewards unlock for claiming after the other members are collected. Reruns preserve configured prices, holdings and existing catalog offers. There is no legacy grant-all backfill or revocation.

Collections and items support credits plus Plus's activity point types 0 (duckets) and 5 (diamonds). Unsupported point types are rejected. Catalog Habicon rows use `item_id = '0'` and a positive `habbicon_id`; they do not need a furniture definition and cannot be gifted or purchased in bulk. No badge is automatically awarded, matching the Polaris service.

Enable Habicons in the existing Octane client configuration and serve its Habicon assets. This port changes no client files. Shop requests load ownership, favorites, recents and category-8 unseen notifications; use the normal unseen reset packets to clear those notifications. Room use respects mute/flood controls and ownership.

Direct-friend Habicon messages use the modern type-4 protocol with acknowledgement and typed delivery for online recipients. Offline recipients use Plus's existing `:icon_name:` text fallback on login. This port does not add Polaris's separate messenger history/group system or its Habicon quest goal, which Plus does not implement.

## Tests

Run `dotnet test Plus.Tests/Plus.Tests.csproj`. MariaDB integration cases run when `PLUS_HABBICONS_TEST_CONNECTION_STRING` points to a disposable database named `task_habicons_tests_*`, with Plus-compatible `users`, `catalog_pages`, and `catalog_items` tables. The account needs DDL/DML, user variables and trigger privileges on that schema. The cases seed/delete test ownership and user 910001 and deliberately change icon prices, so never use a live database. Without the variable, those cases are explicitly skipped.

using System.Globalization;
using System.Text;
using Microsoft.Data.SqlClient;

namespace Goldmark.Api.Commands;

/// <summary>
/// The principal audit (DEC-031; issues #2, #3, #4): read-only checks of Goldmark's two logins
/// and of the SQL Server instance they live on, run whenever <c>bootstrap-db</c> adopts or
/// revalidates a principal, in every deployment. Two layers:
/// <list type="bullet">
/// <item><b>Administrator side</b> (<see cref="EnvironmentAsync"/>, <see cref="PrincipalAsync"/>):
/// catalog checks that only an administrator can see, such as linked-server mappings,
/// ownership, login state and grants to <c>public</c>/<c>guest</c> in the system databases.</item>
/// <item><b>As the login</b> (<see cref="SelfAsync"/>): effective rights read from the login's own
/// token, so rights that arrive through a Windows group (later deployments) are seen too.</item>
/// </list>
/// Every finding has a stable ID (<c>kind:part[:part...]</c>). Findings about the shared
/// instance (<see cref="IsAcceptableKind"/>) can be accepted by ID with
/// <c>Goldmark:Bootstrap:AcceptedFindings</c>; findings about Goldmark's own principals or
/// database cannot, and must be fixed.
/// </summary>
internal static class PrincipalAudit
{
    /// <summary>
    /// Findings about the instance, not about Goldmark's principals or database: an operator on a
    /// shared server may accept them. Server-level grants to public are not among them: every
    /// login inherits them, so they are rights of Goldmark's logins like any other (DEC-015).
    /// </summary>
    private static readonly HashSet<string> AcceptableKinds = new(StringComparer.Ordinal)
    {
        "guest-connect", "linked-login-all-logins", "remote-login-all-logins", "system-grant",
        "cross-db-chaining", "database-unreadable",
    };

    public static bool IsAcceptableKind(string kind) => AcceptableKinds.Contains(kind);

    /// <summary>Every finding kind the audit reports.</summary>
    public static readonly HashSet<string> KnownKinds = new(StringComparer.Ordinal)
    {
        "guest-connect", "database-unreadable", "linked-login-all-logins", "remote-login-all-logins", "public-server-grant",
        "system-grant", "cross-db-chaining", "goldmark-grant", "goldmark-database-option", "goldmark-database-owner",
        "goldmark-database-principal", "goldmark-public-owner", "runtime-role-grant", "login-disabled", "login-policy-off", "login-expiration-on",
        "login-expired", "login-locked", "login-must-change", "login-connect-denied", "login-credential", "server-role",
        "server-grant", "owns", "linked-login", "remote-login", "mapped-into", "database-access", "database-role",
        "database-grant", "database-connect-denied", "server-securable", "database-securable",
    };

    public static IReadOnlyCollection<string> AcceptableKindNames => AcceptableKinds;

    private const string BaselineResource = "Goldmark.Audit.sql-server-2025-public-grants.txt";

    /// <summary>System databases whose public/guest grants are compared with the measured baseline.</summary>
    public static readonly string[] SystemDatabases = ["master", "msdb", "tempdb", "model"];

    // Columns of every audit query: Kind and up to four ID parts (NULL = absent).
    private const string FindingsTable = "DECLARE @f TABLE (Kind nvarchar(64), A nvarchar(256), B nvarchar(256), C nvarchar(256), D nvarchar(512));";

    // Target of a database-scope permission row (database_permissions alias p), as class and
    // name. Names are QUOTENAME'd, so a target is unambiguous and can be pasted into a REVOKE.
    private const string DatabaseTargetClass = """
        CASE p.class WHEN 0 THEN N'DATABASE' WHEN 1 THEN N'OBJECT' WHEN 3 THEN N'SCHEMA'
            WHEN 4 THEN CASE (SELECT dp.type FROM sys.database_principals AS dp WHERE dp.principal_id = p.major_id)
                WHEN 'R' THEN N'ROLE' WHEN 'A' THEN N'APPLICATION ROLE' ELSE N'USER' END
            WHEN 5 THEN N'ASSEMBLY' WHEN 6 THEN N'TYPE' WHEN 10 THEN N'XML SCHEMA COLLECTION'
            WHEN 15 THEN N'MESSAGE TYPE' WHEN 16 THEN N'CONTRACT' WHEN 17 THEN N'SERVICE' WHEN 18 THEN N'REMOTE SERVICE BINDING'
            WHEN 19 THEN N'ROUTE' WHEN 23 THEN N'FULLTEXT CATALOG' WHEN 24 THEN N'SYMMETRIC KEY' WHEN 25 THEN N'CERTIFICATE'
            WHEN 26 THEN N'ASYMMETRIC KEY' WHEN 29 THEN N'FULLTEXT STOPLIST' WHEN 31 THEN N'SEARCH PROPERTY LIST'
            WHEN 32 THEN N'DATABASE SCOPED CREDENTIAL'
            ELSE p.class_desc COLLATE DATABASE_DEFAULT END
        """;

    private const string DatabaseTargetName = """
        CASE p.class
            WHEN 0 THEN NULL
            WHEN 1 THEN ISNULL((QUOTENAME(OBJECT_SCHEMA_NAME(p.major_id)) + N'.' + QUOTENAME(OBJECT_NAME(p.major_id))) COLLATE DATABASE_DEFAULT, N'#' + CAST(p.major_id AS nvarchar(12)))
                + CASE WHEN p.minor_id > 0 THEN N' (' + ISNULL(QUOTENAME(COL_NAME(p.major_id, p.minor_id)) COLLATE DATABASE_DEFAULT, N'#' + CAST(p.minor_id AS nvarchar(12))) + N')' ELSE N'' END
            WHEN 3 THEN ISNULL(QUOTENAME(SCHEMA_NAME(p.major_id)) COLLATE DATABASE_DEFAULT, N'#' + CAST(p.major_id AS nvarchar(12)))
            WHEN 4 THEN ISNULL(QUOTENAME(USER_NAME(p.major_id)) COLLATE DATABASE_DEFAULT, N'#' + CAST(p.major_id AS nvarchar(12)))
            WHEN 6 THEN ISNULL((SELECT QUOTENAME(SCHEMA_NAME(t.schema_id)) + N'.' + QUOTENAME(t.name) FROM sys.types AS t WHERE t.user_type_id = p.major_id),
                N'#' + CAST(p.major_id AS nvarchar(12)))
            ELSE ISNULL(CASE p.class
                WHEN 5 THEN (SELECT QUOTENAME(x.name) COLLATE DATABASE_DEFAULT FROM sys.assemblies AS x WHERE x.assembly_id = p.major_id)
                WHEN 10 THEN (SELECT QUOTENAME(SCHEMA_NAME(x.schema_id)) + N'.' + QUOTENAME(x.name) FROM sys.xml_schema_collections AS x WHERE x.xml_collection_id = p.major_id)
                WHEN 15 THEN (SELECT QUOTENAME(x.name) COLLATE DATABASE_DEFAULT FROM sys.service_message_types AS x WHERE x.message_type_id = p.major_id)
                WHEN 16 THEN (SELECT QUOTENAME(x.name) COLLATE DATABASE_DEFAULT FROM sys.service_contracts AS x WHERE x.service_contract_id = p.major_id)
                WHEN 17 THEN (SELECT QUOTENAME(x.name) COLLATE DATABASE_DEFAULT FROM sys.services AS x WHERE x.service_id = p.major_id)
                WHEN 18 THEN (SELECT QUOTENAME(x.name) COLLATE DATABASE_DEFAULT FROM sys.remote_service_bindings AS x WHERE x.remote_service_binding_id = p.major_id)
                WHEN 19 THEN (SELECT QUOTENAME(x.name) COLLATE DATABASE_DEFAULT FROM sys.routes AS x WHERE x.route_id = p.major_id)
                WHEN 23 THEN (SELECT QUOTENAME(x.name) COLLATE DATABASE_DEFAULT FROM sys.fulltext_catalogs AS x WHERE x.fulltext_catalog_id = p.major_id)
                WHEN 24 THEN (SELECT QUOTENAME(x.name) COLLATE DATABASE_DEFAULT FROM sys.symmetric_keys AS x WHERE x.symmetric_key_id = p.major_id)
                WHEN 25 THEN (SELECT QUOTENAME(x.name) COLLATE DATABASE_DEFAULT FROM sys.certificates AS x WHERE x.certificate_id = p.major_id)
                WHEN 26 THEN (SELECT QUOTENAME(x.name) COLLATE DATABASE_DEFAULT FROM sys.asymmetric_keys AS x WHERE x.asymmetric_key_id = p.major_id)
                WHEN 29 THEN (SELECT QUOTENAME(x.name) COLLATE DATABASE_DEFAULT FROM sys.fulltext_stoplists AS x WHERE x.stoplist_id = p.major_id)
                WHEN 31 THEN (SELECT QUOTENAME(x.name) COLLATE DATABASE_DEFAULT FROM sys.registered_search_property_lists AS x WHERE x.property_list_id = p.major_id)
                WHEN 32 THEN (SELECT QUOTENAME(x.name) COLLATE DATABASE_DEFAULT FROM sys.database_scoped_credentials AS x WHERE x.credential_id = p.major_id)
                END, N'#' + CAST(p.major_id AS nvarchar(12))) END
        """;

    // Every database securable that records an owner, as (kind, name, owner principal_id).
    private const string OwnedSecurables = """
        SELECT N'object' AS kind, SCHEMA_NAME(schema_id) COLLATE DATABASE_DEFAULT + N'.' + name COLLATE DATABASE_DEFAULT AS name, principal_id AS owner FROM sys.objects
        UNION ALL SELECT N'schema', name COLLATE DATABASE_DEFAULT, principal_id FROM sys.schemas
        UNION ALL SELECT N'database principal', name COLLATE DATABASE_DEFAULT, owning_principal_id FROM sys.database_principals
        UNION ALL SELECT N'type', name COLLATE DATABASE_DEFAULT, principal_id FROM sys.types
        UNION ALL SELECT N'xml schema collection', name COLLATE DATABASE_DEFAULT, principal_id FROM sys.xml_schema_collections
        UNION ALL SELECT N'assembly', name COLLATE DATABASE_DEFAULT, principal_id FROM sys.assemblies
        UNION ALL SELECT N'certificate', name COLLATE DATABASE_DEFAULT, principal_id FROM sys.certificates
        UNION ALL SELECT N'asymmetric key', name COLLATE DATABASE_DEFAULT, principal_id FROM sys.asymmetric_keys
        UNION ALL SELECT N'symmetric key', name COLLATE DATABASE_DEFAULT, principal_id FROM sys.symmetric_keys
        UNION ALL SELECT N'database scoped credential', name COLLATE DATABASE_DEFAULT, principal_id FROM sys.database_scoped_credentials
        UNION ALL SELECT N'full-text catalog', name COLLATE DATABASE_DEFAULT, principal_id FROM sys.fulltext_catalogs
        UNION ALL SELECT N'full-text stoplist', name COLLATE DATABASE_DEFAULT, principal_id FROM sys.fulltext_stoplists
        UNION ALL SELECT N'search property list', name COLLATE DATABASE_DEFAULT, principal_id FROM sys.registered_search_property_lists
        UNION ALL SELECT N'message type', name COLLATE DATABASE_DEFAULT, principal_id FROM sys.service_message_types
        UNION ALL SELECT N'contract', name COLLATE DATABASE_DEFAULT, principal_id FROM sys.service_contracts
        UNION ALL SELECT N'service', name COLLATE DATABASE_DEFAULT, principal_id FROM sys.services
        UNION ALL SELECT N'route', name COLLATE DATABASE_DEFAULT, principal_id FROM sys.routes
        UNION ALL SELECT N'remote service binding', name COLLATE DATABASE_DEFAULT, principal_id FROM sys.remote_service_bindings
        UNION ALL SELECT N'event notification', name COLLATE DATABASE_DEFAULT, principal_id FROM sys.event_notifications
        UNION ALL SELECT N'external language', language COLLATE DATABASE_DEFAULT, principal_id FROM sys.external_languages
        UNION ALL SELECT N'external library', name COLLATE DATABASE_DEFAULT, principal_id FROM sys.external_libraries
        """;

    // Class and name of a server-scope permission row (server_permissions alias p).
    private const string ServerTargetClass = """
        CASE p.class WHEN 100 THEN N'SERVER'
            WHEN 101 THEN CASE (SELECT sp.type FROM sys.server_principals AS sp WHERE sp.principal_id = p.major_id) WHEN 'R' THEN N'SERVER ROLE' ELSE N'LOGIN' END
            WHEN 105 THEN N'ENDPOINT' ELSE p.class_desc COLLATE DATABASE_DEFAULT END
        """;

    private const string ServerTargetName = """
        CASE p.class WHEN 100 THEN NULL
            WHEN 101 THEN ISNULL(QUOTENAME(SUSER_NAME(p.major_id)), N'#' + CAST(p.major_id AS nvarchar(12)))
            WHEN 105 THEN ISNULL((SELECT QUOTENAME(e.name) FROM sys.endpoints AS e WHERE e.endpoint_id = p.major_id), N'#' + CAST(p.major_id AS nvarchar(12)))
            ELSE N'#' + CAST(p.major_id AS nvarchar(12)) END
        """;

    private const string PermissionName = "p.permission_name COLLATE DATABASE_DEFAULT + CASE WHEN p.state = 'W' THEN N' WITH GRANT OPTION' ELSE N'' END";

    /// <summary>
    /// Every grant to <c>public</c> and <c>guest</c> in the current database, as
    /// (grantee, permission, class, target). The class keeps, for example, a schema named
    /// <c>sys.all_objects</c> apart from the view (Codex on #17). Used for the system databases
    /// and the Goldmark database, and by the test that keeps the measured baseline current.
    /// </summary>
    internal const string PublicGrantsSql = $"""
        SET NOCOUNT ON;
        SELECT USER_NAME(p.grantee_principal_id), {PermissionName}, {DatabaseTargetClass},
            ISNULL({DatabaseTargetName}, N'')
        FROM sys.database_permissions AS p
        WHERE p.grantee_principal_id IN (0, 2) AND p.state IN ('G', 'W');
        """;

    // Run in master. Instance-wide conditions that give every login, Goldmark's included,
    // rights it was never granted (#2, #3), plus the Goldmark database's own options.
    private const string EnvironmentSql = $"""
        SET NOCOUNT ON;
        {FindingsTable}
        -- Mappings for all logins (local principal 0). Only those that submit a fixed remote
        -- credential add rights; the self-credential mapping sp_addlinkedserver creates does not.
        INSERT @f SELECT N'linked-login-all-logins', s.name, NULL, NULL, NULL
        FROM sys.linked_logins AS l JOIN sys.servers AS s ON s.server_id = l.server_id
        WHERE l.local_principal_id = 0 AND l.uses_self_credential = 0;
        INSERT @f SELECT N'remote-login-all-logins', s.name, NULL, NULL, NULL
        FROM sys.remote_logins AS l JOIN sys.servers AS s ON s.server_id = l.server_id
        WHERE l.local_principal_id = 0;

        -- Server-level grants to public, which every login inherits, beyond SQL Server's own
        -- (VIEW ANY DATABASE, CONNECT on the system endpoints; measured on SQL Server 2025).
        INSERT @f SELECT N'public-server-grant', {PermissionName}, {ServerTargetClass}, {ServerTargetName}, NULL
        FROM sys.server_permissions AS p
        WHERE p.grantee_principal_id = 2 AND p.state IN ('G', 'W')
            AND NOT (p.class = 100 AND p.permission_name = N'VIEW ANY DATABASE' AND p.state = 'G')
            AND NOT (p.class = 105 AND p.permission_name = N'CONNECT' AND p.major_id < 65536 AND p.state = 'G');

        INSERT @f SELECT N'cross-db-chaining', NULL, NULL, NULL, NULL FROM sys.configurations
        WHERE name = N'cross db ownership chaining' AND CAST(value_in_use AS int) = 1;

        -- TRUSTWORTHY lets db_owner (the migration login) act with the database owner's server
        -- rights; database chaining extends ownership chains into other databases.
        INSERT @f SELECT N'goldmark-database-option', name, N'TRUSTWORTHY', NULL, NULL FROM sys.databases WHERE name = @db AND is_trustworthy_on = 1;
        INSERT @f SELECT N'goldmark-database-option', name, N'DB_CHAINING', NULL, NULL FROM sys.databases WHERE name = @db AND is_db_chaining_on = 1;

        SELECT Kind, A, B, C, D FROM @f;
        """;

    // Run in master. One pass over every other database (opening a database can be slow, for
    // example on Express, where new databases close automatically): guest access, which lets
    // any login in without a user (#2; SQL Server enables it only in master, tempdb and msdb),
    // and users mapped to either Goldmark login, which give it that user's rights there.
    private const string DatabasesSql = $"""
        SET NOCOUNT ON;
        {FindingsTable}
        DECLARE @sid1 varbinary(85) = SUSER_SID(@login1), @sid2 varbinary(85) = SUSER_SID(@login2);
        DECLARE @name sysname, @id int, @sql nvarchar(max), @guest int, @mapped1 int, @mapped2 int;
        DECLARE dbs CURSOR LOCAL FAST_FORWARD FOR SELECT name, database_id FROM sys.databases WHERE state = 0 AND name <> @db;
        OPEN dbs;
        FETCH NEXT FROM dbs INTO @name, @id;
        WHILE @@FETCH_STATUS = 0
        BEGIN
            BEGIN TRY
                SET @sql = N'SELECT @guest = (SELECT COUNT(*) FROM ' + QUOTENAME(@name) + N'.sys.database_permissions'
                    + N' WHERE grantee_principal_id = 2 AND class = 0 AND permission_name = N''CONNECT'' AND state IN (''G'', ''W'')),'
                    + N' @mapped1 = (SELECT COUNT(*) FROM ' + QUOTENAME(@name) + N'.sys.database_principals WHERE sid = @sid1),'
                    + N' @mapped2 = (SELECT COUNT(*) FROM ' + QUOTENAME(@name) + N'.sys.database_principals WHERE sid = @sid2);';
                EXEC sys.sp_executesql @sql, N'@sid1 varbinary(85), @sid2 varbinary(85), @guest int OUTPUT, @mapped1 int OUTPUT, @mapped2 int OUTPUT',
                    @sid1 = @sid1, @sid2 = @sid2, @guest = @guest OUTPUT, @mapped1 = @mapped1 OUTPUT, @mapped2 = @mapped2 OUTPUT;
                IF @guest > 0 AND @id NOT IN (1, 2, 4) INSERT @f VALUES (N'guest-connect', @name, NULL, NULL, NULL);
                IF @mapped1 > 0 INSERT @f VALUES (N'mapped-into', @login1, @name, NULL, NULL);
                IF @mapped2 > 0 INSERT @f VALUES (N'mapped-into', @login2, @name, NULL, NULL);
            END TRY
            BEGIN CATCH
                -- For example a secondary replica that is not readable: it cannot be checked.
                INSERT @f VALUES (N'database-unreadable', @name, CAST(ERROR_NUMBER() AS nvarchar(12)), NULL, NULL);
            END CATCH;
            FETCH NEXT FROM dbs INTO @name, @id;
        END;
        CLOSE dbs;
        DEALLOCATE dbs;

        -- Offline, restoring, suspect...: they cannot be checked now, and may come online later.
        INSERT @f SELECT N'database-unreadable', name, state_desc, NULL, NULL FROM sys.databases WHERE state <> 0 AND name <> @db;
        SELECT Kind, A, B, C, D FROM @f;
        """;

    // Run in the Goldmark database through sp_executesql (see PrincipalSql). The user is the
    // one mapped to the login; when adopting, a same-name user that bootstrap-db would remap to
    // the login is audited too, before anything is changed (Codex on #17).
    private const string PrincipalInDatabaseSql = $"""
        SET NOCOUNT ON;
        {FindingsTable}
        DECLARE @uid int = (SELECT principal_id FROM sys.database_principals WHERE sid = @sid);
        IF @uid IS NULL AND @adopt_user = 1
            SET @uid = (SELECT principal_id FROM sys.database_principals
                WHERE name = @login AND type NOT IN ('R', 'A') AND principal_id > 4);
        IF @uid IS NOT NULL
        BEGIN
            INSERT @f SELECT N'database-role', @login, r.name, NULL, NULL
            FROM sys.database_role_members AS m JOIN sys.database_principals AS r ON r.principal_id = m.role_principal_id
            WHERE m.member_principal_id = @uid AND r.name <> @expected_role;

            -- Rights come only through the expected role: any direct grant besides CONNECT is a finding,
            -- even one an existing DENY currently overrides.
            INSERT @f SELECT N'database-grant', @login, {PermissionName}, {DatabaseTargetClass}, {DatabaseTargetName}
            FROM sys.database_permissions AS p
            WHERE p.grantee_principal_id = @uid AND p.state IN ('G', 'W') AND NOT (p.class = 0 AND p.permission_name = N'CONNECT' AND p.state = 'G');

            INSERT @f SELECT N'database-connect-denied', @login, USER_NAME(p.grantee_principal_id), NULL, NULL
            FROM sys.database_permissions AS p
            WHERE p.grantee_principal_id IN (@uid, 0) AND p.state = 'D' AND p.class = 0 AND p.permission_name = N'CONNECT';

            -- Ownership of any database securable: an owner holds every right on it, and objects not
            -- owned by dbo break ownership chains (DEC-029: everything is owned by dbo).
            INSERT @f SELECT N'owns', @login, o.kind, o.name, NULL FROM (
                {OwnedSecurables}
            ) AS o WHERE o.owner = @uid;
        END;
        SELECT Kind, A, B, C, D FROM @f;
        """;

    // Run in the Goldmark database as the administrator. Who else controls it: an owner (dbo)
    // without CONTROL SERVER, for example a login that created the database before Goldmark did
    // (Codex security review on #17), and any other user or application role that holds a role,
    // a permission beyond CONNECT, or owns something. Goldmark's two users and the built-in
    // principals are expected.
    // Run in master as the administrator. The Goldmark database's owner (its dbo) must hold
    // CONTROL SERVER effectively (#19): sa, or a login whose own token has it, through any role
    // or group and honouring a DENY, checked by impersonating it. An owner that is not a login, or
    // that cannot be impersonated, is a finding too. The impersonation runs in master, which every
    // login can enter, and ends with its sp_executesql batch.
    private const string DatabaseOwnerSql = $"""
        SET NOCOUNT ON;
        {FindingsTable}
        DECLARE @owner varbinary(85) = (SELECT owner_sid FROM sys.databases WHERE name = @db);
        DECLARE @name sysname = SUSER_SNAME(@owner), @controls int = 0;
        IF @owner = 0x01
            SET @controls = 1;
        ELSE IF @name IS NOT NULL
        BEGIN
            BEGIN TRY
                EXEC sys.sp_executesql
                    N'EXECUTE AS LOGIN = @name; SET @controls = ISNULL(HAS_PERMS_BY_NAME(NULL, NULL, N''CONTROL SERVER''), 0); REVERT;',
                    N'@name sysname, @controls int OUTPUT', @name = @name, @controls = @controls OUTPUT;
            END TRY
            BEGIN CATCH
                SET @controls = 0;
            END CATCH;
        END;
        IF @controls = 0
            INSERT @f VALUES (N'goldmark-database-owner', @db, ISNULL(@name, N'#unknown'), NULL, NULL);
        SELECT Kind, A, B, C, D FROM @f;
        """;

    private const string GoldmarkDatabaseSql = $"""
        SET NOCOUNT ON;
        {FindingsTable}
        -- Securables owned by public (#18): every user in the database would hold every right on them.
        INSERT @f SELECT N'goldmark-public-owner', DB_NAME(), o.kind, o.name, NULL
        FROM ({OwnedSecurables}) AS o WHERE o.owner = 0;

        -- Any other user or application role that holds a role, a permission beyond a plain
        -- CONNECT, or owns anything (a role it owns can be altered to give it Goldmark's rights).
        INSERT @f SELECT DISTINCT N'goldmark-database-principal', DB_NAME(), u.name, NULL, NULL
        FROM sys.database_principals AS u
        WHERE u.type NOT IN ('R') AND u.principal_id > 4
            AND u.sid NOT IN (ISNULL(SUSER_SID(@login1), 0x00), ISNULL(SUSER_SID(@login2), 0x00))
            AND (EXISTS (SELECT 1 FROM sys.database_role_members AS m WHERE m.member_principal_id = u.principal_id)
                OR EXISTS (SELECT 1 FROM sys.database_permissions AS p WHERE p.grantee_principal_id = u.principal_id
                    AND p.state IN ('G', 'W') AND NOT (p.class = 0 AND p.permission_name = N'CONNECT' AND p.state = 'G'))
                OR EXISTS (SELECT 1 FROM ({OwnedSecurables}) AS o WHERE o.owner = u.principal_id));

        -- goldmark_runtime holds exactly what migration 0002 grants it (keep in step with new
        -- migrations, like VerifyRuntimeSql). Its effective rights are checked as the login, but
        -- not on system objects, so explicit grants are compared here (Codex on #17).
        DECLARE @role int = DATABASE_PRINCIPAL_ID(N'goldmark_runtime');
        INSERT @f SELECT N'runtime-role-grant', DB_NAME(), {PermissionName}, {DatabaseTargetClass}, {DatabaseTargetName}
        FROM sys.database_permissions AS p
        WHERE @role IS NOT NULL AND p.grantee_principal_id = @role AND p.state IN ('G', 'W')
            AND NOT (p.state = 'G' AND p.class = 3 AND p.major_id = SCHEMA_ID(N'cmdb') AND p.permission_name IN (N'SELECT', N'INSERT', N'UPDATE', N'DELETE'))
            AND NOT (p.state = 'G' AND p.class = 1 AND p.major_id = OBJECT_ID(N'dbo.SchemaVersions') AND p.minor_id = 0 AND p.permission_name = N'SELECT');
        SELECT Kind, A, B, C, D FROM @f;
        """;

    // Run in master as the administrator, for one login (#4). Nothing is returned for a login
    // that does not exist yet (bootstrap-db is about to create it).
    private const string PrincipalSql = $"""
        SET NOCOUNT ON;
        {FindingsTable}
        DECLARE @sid varbinary(85) = SUSER_SID(@login);
        DECLARE @pid int = (SELECT principal_id FROM sys.server_principals WHERE sid = @sid);
        IF @pid IS NOT NULL
        BEGIN
            -- State that makes the login stop working on its own, or weakens its password.
            INSERT @f SELECT N'login-disabled', @login, NULL, NULL, NULL FROM sys.server_principals WHERE principal_id = @pid AND is_disabled = 1;
            INSERT @f SELECT N'login-policy-off', @login, NULL, NULL, NULL FROM sys.sql_logins WHERE principal_id = @pid AND is_policy_checked = 0;
            INSERT @f SELECT N'login-expiration-on', @login, NULL, NULL, NULL FROM sys.sql_logins WHERE principal_id = @pid AND is_expiration_checked = 1;
            INSERT @f SELECT v.kind, @login, NULL, NULL, NULL
            FROM (VALUES (N'login-expired', N'IsExpired'), (N'login-locked', N'IsLocked'), (N'login-must-change', N'IsMustChange')) AS v(kind, property)
            WHERE EXISTS (SELECT 1 FROM sys.sql_logins WHERE principal_id = @pid)
                AND ISNULL(CAST(LOGINPROPERTY(@login, v.property) AS int), 0) = 1;
            INSERT @f SELECT N'login-connect-denied', @login, SUSER_NAME(p.grantee_principal_id), NULL, NULL
            FROM sys.server_permissions AS p
            WHERE p.grantee_principal_id IN (@pid, 2) AND p.state = 'D' AND p.class = 100 AND p.permission_name = N'CONNECT SQL';
            INSERT @f SELECT N'login-credential', @login, c.name, NULL, NULL
            FROM sys.sql_logins AS l JOIN sys.credentials AS c ON c.credential_id = l.credential_id WHERE l.principal_id = @pid;
            INSERT @f SELECT N'login-credential', @login, c.name, NULL, NULL
            FROM sys.server_principal_credentials AS pc JOIN sys.credentials AS c ON c.credential_id = pc.credential_id WHERE pc.principal_id = @pid;

            -- Server-level rights held directly (effective and nested ones are checked as the login).
            INSERT @f SELECT N'server-role', @login, r.name, NULL, NULL
            FROM sys.server_role_members AS m JOIN sys.server_principals AS r ON r.principal_id = m.role_principal_id
            WHERE m.member_principal_id = @pid;
            INSERT @f SELECT N'server-grant', @login, {PermissionName}, {ServerTargetClass}, {ServerTargetName}
            FROM sys.server_permissions AS p
            WHERE p.grantee_principal_id = @pid AND p.state IN ('G', 'W') AND NOT (p.class = 100 AND p.permission_name = N'CONNECT SQL' AND p.state = 'G');

            -- Ownership of server-level securables.
            INSERT @f SELECT N'owns', @login, o.kind, o.name, NULL FROM (
                SELECT N'login or server role' AS kind, name COLLATE DATABASE_DEFAULT AS name, owning_principal_id AS owner FROM sys.server_principals
                UNION ALL SELECT N'endpoint', name COLLATE DATABASE_DEFAULT, principal_id FROM sys.endpoints
                UNION ALL SELECT N'server event notification', name COLLATE DATABASE_DEFAULT, principal_id FROM sys.server_event_notifications
                UNION ALL SELECT N'server audit', name COLLATE DATABASE_DEFAULT, principal_id FROM sys.server_audits
            ) AS o WHERE o.owner = @pid;
            INSERT @f SELECT N'owns', @login, N'database', name, NULL FROM sys.databases WHERE owner_sid = @sid;
            INSERT @f SELECT N'owns', @login, N'availability replica', replica_server_name, NULL FROM sys.availability_replicas WHERE owner_sid = @sid;

            -- Linked-server and remote-login mappings for this login (#3).
            INSERT @f SELECT N'linked-login', @login, s.name, NULL, NULL
            FROM sys.linked_logins AS l JOIN sys.servers AS s ON s.server_id = l.server_id WHERE l.local_principal_id = @pid;
            INSERT @f SELECT N'remote-login', @login, s.name, NULL, NULL
            FROM sys.remote_logins AS l JOIN sys.servers AS s ON s.server_id = l.server_id WHERE l.local_principal_id = @pid;

        END;

        -- Also when the login does not exist yet: adopting a same-name user remaps it to the login.
        IF DB_ID(@db) IS NOT NULL AND (@pid IS NOT NULL OR @adopt_user = 1)
        BEGIN
            DECLARE @exec nvarchar(300) = QUOTENAME(@db) + N'.sys.sp_executesql';
            INSERT @f EXEC @exec @in_database, N'@sid varbinary(85), @login sysname, @expected_role sysname, @adopt_user bit',
                @sid = @sid, @login = @login, @expected_role = @expected_role, @adopt_user = @adopt_user;
        END;
        SELECT Kind, A, B, C, D FROM @f;
        """;

    // Run as the login itself, in the Goldmark database: effective rights from its own token, so
    // rights that arrive through a group or a nested role are seen too.
    private const string SelfSql = $"""
        SET NOCOUNT ON;
        {FindingsTable}
        DECLARE @me sysname = SUSER_SNAME();
        DECLARE @name sysname, @user sysname, @exec nvarchar(300);

        -- Databases this login can enter besides its own (#2). As guest, master, tempdb and msdb
        -- are SQL Server's default; anywhere else guest access is a finding, and a real user in
        -- another database (mapped directly or through a group) is one too. HAS_DBACCESS opens
        -- every database, so callers that already ran this pass skip it (@databases = 0).
        IF @databases = 1
        BEGIN
            DECLARE dbs CURSOR LOCAL FAST_FORWARD FOR
                SELECT name FROM sys.databases WHERE state = 0 AND name <> @db AND HAS_DBACCESS(name) = 1;
            OPEN dbs;
            FETCH NEXT FROM dbs INTO @name;
            WHILE @@FETCH_STATUS = 0
            BEGIN
                BEGIN TRY
                    SET @exec = QUOTENAME(@name) + N'.sys.sp_executesql';
                    EXEC @exec N'SELECT @user = USER_NAME();', N'@user sysname OUTPUT', @user = @user OUTPUT;
                    IF @user <> N'guest' INSERT @f VALUES (N'database-access', @me, @name, @user, NULL);
                    ELSE IF @name NOT IN (N'master', N'tempdb', N'msdb') INSERT @f VALUES (N'guest-connect', @name, NULL, NULL, NULL);
                END TRY
                BEGIN CATCH
                    -- HAS_DBACCESS reports access, but the database cannot be entered (e.g. a non-readable secondary).
                END CATCH;
                FETCH NEXT FROM dbs INTO @name;
            END;
            CLOSE dbs;
            DEALLOCATE dbs;
        END;

        -- Rights on other logins and server roles (e.g. IMPERSONATE), and on endpoints beyond CONNECT
        -- on the system endpoints. A login's implicit rights on itself are expected.
        INSERT @f SELECT N'server-securable', @me, c.class, p.name, f.permission_name
        FROM sys.server_principals AS p
        CROSS APPLY (SELECT CASE p.type WHEN 'R' THEN N'SERVER ROLE' ELSE N'LOGIN' END AS class) AS c
        CROSS APPLY sys.fn_my_permissions(QUOTENAME(p.name), c.class) AS f
        WHERE p.sid <> SUSER_SID() AND f.subentity_name = N'';
        INSERT @f SELECT N'server-securable', @me, N'ENDPOINT', e.name, f.permission_name
        FROM sys.endpoints AS e CROSS APPLY sys.fn_my_permissions(QUOTENAME(e.name), N'ENDPOINT') AS f
        WHERE NOT (e.endpoint_id < 65536 AND f.permission_name = N'CONNECT');

        -- Below database scope, for the runtime login only (the migration login is db_owner here).
        -- Allowed: DML on schema cmdb and its objects, SELECT on the journal. Schemas cmdb and dbo
        -- and the audit log are also checked by migrate (VerifyRuntimeSql).
        IF @runtime = 1
        BEGIN
            INSERT @f SELECT N'database-securable', @me, c.class, p.name, f.permission_name
            FROM sys.database_principals AS p
            CROSS APPLY (SELECT CASE p.type WHEN 'R' THEN N'ROLE' WHEN 'A' THEN N'APPLICATION ROLE' ELSE N'USER' END AS class) AS c
            CROSS APPLY sys.fn_my_permissions(QUOTENAME(p.name), c.class) AS f
            WHERE p.principal_id <> DATABASE_PRINCIPAL_ID() AND f.subentity_name = N'';

            INSERT @f SELECT N'database-securable', @me, N'SCHEMA', s.name, f.permission_name
            FROM sys.schemas AS s CROSS APPLY sys.fn_my_permissions(QUOTENAME(s.name), N'SCHEMA') AS f
            WHERE s.name NOT IN (N'cmdb', N'dbo');

            INSERT @f SELECT N'database-securable', @me, N'OBJECT', o.full_name,
                f.permission_name + CASE WHEN f.subentity_name <> N'' THEN N' (' + f.subentity_name + N')' ELSE N'' END
            FROM (SELECT SCHEMA_NAME(schema_id) AS schema_name, name,
                    SCHEMA_NAME(schema_id) + N'.' + name AS full_name,
                    QUOTENAME(SCHEMA_NAME(schema_id)) + N'.' + QUOTENAME(name) AS securable
                FROM sys.objects WHERE parent_object_id = 0 AND is_ms_shipped = 0) AS o
            CROSS APPLY sys.fn_my_permissions(o.securable, N'OBJECT') AS f
            WHERE NOT (o.schema_name = N'cmdb' AND f.permission_name IN (N'SELECT', N'INSERT', N'UPDATE', N'DELETE'))
                AND NOT (o.schema_name = N'dbo' AND o.name = N'SchemaVersions' AND f.permission_name = N'SELECT');

            -- Other securables are visible only to a principal with a permission on them.
            DECLARE @other TABLE (class nvarchar(64), name nvarchar(256), securable nvarchar(600));
            INSERT @other
                SELECT N'TYPE', SCHEMA_NAME(schema_id) + N'.' + name, QUOTENAME(SCHEMA_NAME(schema_id)) + N'.' + QUOTENAME(name) FROM sys.types WHERE is_user_defined = 1
                UNION ALL SELECT N'XML SCHEMA COLLECTION', SCHEMA_NAME(schema_id) + N'.' + name, QUOTENAME(SCHEMA_NAME(schema_id)) + N'.' + QUOTENAME(name) FROM sys.xml_schema_collections WHERE xml_collection_id > 1
                UNION ALL SELECT N'ASSEMBLY', name, QUOTENAME(name) FROM sys.assemblies WHERE is_user_defined = 1
                UNION ALL SELECT N'CERTIFICATE', name, QUOTENAME(name) FROM sys.certificates
                UNION ALL SELECT N'ASYMMETRIC KEY', name, QUOTENAME(name) FROM sys.asymmetric_keys
                UNION ALL SELECT N'SYMMETRIC KEY', name, QUOTENAME(name) FROM sys.symmetric_keys
                UNION ALL SELECT N'FULLTEXT CATALOG', name, QUOTENAME(name) FROM sys.fulltext_catalogs
                UNION ALL SELECT N'FULLTEXT STOPLIST', name, QUOTENAME(name) FROM sys.fulltext_stoplists
                UNION ALL SELECT N'SEARCH PROPERTY LIST', name, QUOTENAME(name) FROM sys.registered_search_property_lists
                UNION ALL SELECT N'DATABASE SCOPED CREDENTIAL', name, QUOTENAME(name) FROM sys.database_scoped_credentials
                UNION ALL SELECT N'MESSAGE TYPE', name, QUOTENAME(name) FROM sys.service_message_types
                UNION ALL SELECT N'CONTRACT', name, QUOTENAME(name) FROM sys.service_contracts
                UNION ALL SELECT N'SERVICE', name, QUOTENAME(name) FROM sys.services
                UNION ALL SELECT N'ROUTE', name, QUOTENAME(name) FROM sys.routes
                UNION ALL SELECT N'REMOTE SERVICE BINDING', name, QUOTENAME(name) FROM sys.remote_service_bindings;
            INSERT @f SELECT N'database-securable', @me, o.class, o.name, f.permission_name
            FROM @other AS o CROSS APPLY sys.fn_my_permissions(o.securable, o.class) AS f
            WHERE o.securable IS NOT NULL AND f.subentity_name = N'';
            -- QUOTENAME returns NULL for names over 128 characters: report those as visible.
            INSERT @f SELECT N'database-securable', @me, o.class, o.name, N'VISIBLE' FROM @other AS o WHERE o.securable IS NULL;
        END;

        SELECT Kind, A, B, C, D FROM @f;
        """;

    /// <summary>Instance-wide findings (administrator side). The connection is left in master.</summary>
    public static async Task<List<AuditFinding>> EnvironmentAsync(SqlConnection admin, string database)
    {
        var findings = await QueryAsync(admin, EnvironmentSql, ("@db", database));
        var baseline = Baseline.Value;
        foreach (var system in SystemDatabases)
        {
            await admin.ChangeDatabaseAsync(system);
            foreach (var (grantee, permission, securable, target) in await PublicGrantsAsync(admin))
            {
                var key = BaselineKey(grantee, permission, securable, target);
                if (key == GuestConnect)
                {
                    continue; // reported as guest-connect by DatabasesAsync (model; the others are SQL Server's default)
                }

                if (!baseline["*"].Contains(key) && !(baseline.TryGetValue(system, out var own) && own.Contains(key)))
                {
                    findings.Add(new AuditFinding("system-grant", [system, grantee, permission, securable, target]));
                }
            }
        }

        await admin.ChangeDatabaseAsync("master");
        if (await DatabaseExistsAsync(admin, database))
        {
            await admin.ChangeDatabaseAsync(database);
            foreach (var (grantee, permission, securable, target) in await PublicGrantsAsync(admin))
            {
                if (!baseline["*"].Contains(BaselineKey(grantee, permission, securable, target)))
                {
                    findings.Add(new AuditFinding("goldmark-grant", [database, grantee, permission, securable, target]));
                }
            }

            await admin.ChangeDatabaseAsync("master");
        }

        return findings;
    }

    /// <summary>
    /// One pass over every other database (administrator side): guest access and users mapped to
    /// either login. Run before any change; logins created afterwards cannot be mapped anywhere.
    /// </summary>
    public static Task<List<AuditFinding>> DatabasesAsync(SqlConnection admin, string database, string login1, string login2) =>
        QueryAsync(admin, DatabasesSql, ("@db", database), ("@login1", login1), ("@login2", login2));

    /// <summary>
    /// Findings about one of Goldmark's logins (administrator side), run in master. With
    /// <paramref name="adoptUser"/>, a same-name database user that adoption would remap is audited too.
    /// </summary>
    public static Task<List<AuditFinding>> PrincipalAsync(SqlConnection admin, string database, string login, string expectedRole, bool adoptUser) =>
        QueryAsync(admin, PrincipalSql, ("@db", database), ("@login", login), ("@expected_role", expectedRole),
            ("@adopt_user", adoptUser), ("@in_database", PrincipalInDatabaseSql));

    /// <summary>
    /// The Goldmark database's owner and any other principal that holds rights in it
    /// (administrator side). Nothing while the database does not exist; the connection is left in master.
    /// </summary>
    public static async Task<List<AuditFinding>> GoldmarkDatabaseAsync(SqlConnection admin, string database, string login1, string login2)
    {
        if (!await DatabaseExistsAsync(admin, database))
        {
            return [];
        }

        var findings = await QueryAsync(admin, DatabaseOwnerSql, ("@db", database));
        await admin.ChangeDatabaseAsync(database);
        try
        {
            findings.AddRange(await QueryAsync(admin, GoldmarkDatabaseSql, ("@login1", login1), ("@login2", login2)));
            return findings;
        }
        finally
        {
            await admin.ChangeDatabaseAsync("master");
        }
    }

    /// <summary>
    /// Findings from the login's own effective rights; the connection must be in the Goldmark
    /// database. <paramref name="databases"/> includes the pass over every other database.
    /// </summary>
    public static Task<List<AuditFinding>> SelfAsync(SqlConnection asLogin, string database, bool runtime, bool databases = true) =>
        QueryAsync(asLogin, SelfSql, ("@db", database), ("@runtime", runtime), ("@databases", databases));

    internal static async Task<List<(string Grantee, string Permission, string Class, string Target)>> PublicGrantsAsync(SqlConnection connection)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = PublicGrantsSql;
        command.CommandTimeout = 60;
        await using var reader = await command.ExecuteReaderAsync();
        var rows = new List<(string, string, string, string)>();
        while (await reader.ReadAsync())
        {
            rows.Add((reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3)));
        }

        return rows;
    }

    internal static string BaselineKey(string grantee, string permission, string securable, string target) =>
        $"{grantee}|{permission}|{securable}|{target}";

    private static readonly string GuestConnect = BaselineKey("guest", "CONNECT", "DATABASE", "");

    /// <summary>Measured baseline: scope ("*" or a database) → grantee|permission|target.</summary>
    internal static readonly Lazy<Dictionary<string, HashSet<string>>> Baseline = new(LoadBaseline);

    private static Dictionary<string, HashSet<string>> LoadBaseline()
    {
        using var stream = typeof(PrincipalAudit).Assembly.GetManifestResourceStream(BaselineResource)
            ?? throw new InvalidOperationException($"Embedded resource {BaselineResource} is missing.");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        var baseline = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase) { ["*"] = new(StringComparer.Ordinal) };
        while (reader.ReadLine() is { } line)
        {
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            var separator = line.IndexOf('|', StringComparison.Ordinal);
            var scope = line[..separator];
            if (!baseline.TryGetValue(scope, out var set))
            {
                baseline[scope] = set = new HashSet<string>(StringComparer.Ordinal);
            }

            set.Add(line[(separator + 1)..]);
        }

        return baseline;
    }

    private static async Task<bool> DatabaseExistsAsync(SqlConnection connection, string database)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT CASE WHEN DB_ID(@db) IS NULL THEN 0 ELSE 1 END;";
        command.Parameters.Add(new SqlParameter("@db", System.Data.SqlDbType.NVarChar, 128) { Value = database });
        return Convert.ToInt32(await command.ExecuteScalarAsync(), CultureInfo.InvariantCulture) == 1;
    }

    private static async Task<List<AuditFinding>> QueryAsync(SqlConnection connection, string sql, params (string Name, object Value)[] parameters)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.CommandTimeout = 120;
        foreach (var (name, value) in parameters)
        {
            command.Parameters.Add(value is bool flag
                ? new SqlParameter(name, System.Data.SqlDbType.Bit) { Value = flag }
                : new SqlParameter(name, System.Data.SqlDbType.NVarChar, -1) { Value = value });
        }

        await using var reader = await command.ExecuteReaderAsync();
        var findings = new List<AuditFinding>();
        while (await reader.ReadAsync())
        {
            var parts = new List<string>();
            for (var i = 1; i <= 4; i++)
            {
                if (!reader.IsDBNull(i))
                {
                    parts.Add(reader.GetString(i));
                }
            }

            findings.Add(new AuditFinding(reader.GetString(0), parts));
        }

        return findings;
    }
}

/// <summary>One audit finding: a kind and the names that identify it.</summary>
internal sealed record AuditFinding(string Kind, IReadOnlyList<string> Parts)
{
    /// <summary>Stable ID, for example <c>guest-connect:Sales</c>. Parts never contain separators (see <see cref="Encode"/>).</summary>
    public string Id => string.Join(':', Parts.Where(p => p.Length > 0).Select(Encode).Prepend(Kind));

    public bool Acceptable => PrincipalAudit.IsAcceptableKind(Kind);

    private string P(int index) => index < Parts.Count ? Parts[index] : "";

    // Names in the suggested fixes come from catalogs and are not limited to simple identifiers,
    // so they are quoted like QUOTENAME (identifiers) or as N'' literals (Codex on #17): a pasted
    // fix can never run anything else. Securable targets arrive from SQL already QUOTENAME'd.
    public string Description => Kind switch
    {
        "guest-connect" => $"guest can connect to database '{P(0)}', so every login, Goldmark's included, can enter it. Fix: USE {Q(P(0))}; REVOKE CONNECT FROM guest;",
        "database-unreadable" => $"database '{P(0)}' could not be checked ({P(1)}) for guest access or users of Goldmark's logins.",
        "linked-login-all-logins" => $"linked server '{P(0)}' maps every login to a fixed remote login. Fix: EXEC sp_droplinkedsrvlogin @rmtsrvname = {L(P(0))}, @locallogin = NULL;",
        "remote-login-all-logins" => $"remote server '{P(0)}' accepts every login through a remote-login mapping. Fix: EXEC sp_dropremotelogin @remoteserver = {L(P(0))};",
        "public-server-grant" => $"public holds {P(0)} on {Target(P(1), P(2))}, which every login inherits. Fix: REVOKE {Revocable(P(0))} {On(P(1), P(2))}FROM public{Cascade(P(0))};",
        "system-grant" => $"{P(1)} holds {P(2)} on {Target(P(3), P(4))} in {P(0)}, beyond SQL Server 2025's default; every login inherits it. " +
            $"Fix: USE {Q(P(0))}; REVOKE {Revocable(P(2))} {On(P(3), P(4))}FROM {Q(P(1))}{Cascade(P(2))};",
        "cross-db-chaining" => "the server option 'cross db ownership chaining' is on. Fix: EXEC sp_configure 'cross db ownership chaining', 0; RECONFIGURE;",
        "goldmark-grant" => $"{P(1)} holds {P(2)} on {Target(P(3), P(4))} in the Goldmark database '{P(0)}'. Fix: USE {Q(P(0))}; REVOKE {Revocable(P(2))} {On(P(3), P(4))}FROM {Q(P(1))}{Cascade(P(2))};",
        "goldmark-database-option" => $"the Goldmark database '{P(0)}' has {P(1)} on. Fix: ALTER DATABASE {Q(P(0))} SET {P(1)} OFF;",
        "goldmark-database-owner" => $"the Goldmark database '{P(0)}' is owned by '{P(1)}', which does not hold CONTROL SERVER (effectively, honouring DENY), so that login controls everything in it. " +
            $"Fix: ALTER AUTHORIZATION ON DATABASE::{Q(P(0))} TO <a login with CONTROL SERVER>; (or drop the database if Goldmark did not create it)",
        "goldmark-public-owner" => $"public owns {P(1)} '{P(2)}' in the Goldmark database '{P(0)}', so every user there holds every right on it. " +
            $"Fix: USE {Q(P(0))}; {PublicOwnerFix(P(1), P(2))}",
        "runtime-role-grant" => $"the role goldmark_runtime holds {P(1)} on {Target(P(2), P(3))} in '{P(0)}', which Goldmark's migrations do not grant. " +
            $"Fix: USE {Q(P(0))}; REVOKE {Revocable(P(1))} {On(P(2), P(3))}FROM [goldmark_runtime]{Cascade(P(1))};",
        "goldmark-database-principal" => $"'{P(1)}' in the Goldmark database '{P(0)}' holds a role or a permission, or owns something; only Goldmark's two users may. " +
            $"Fix: USE {Q(P(0))}; remove its roles, grants and ownership, or DROP USER {Q(P(1))};",
        "login-disabled" => $"login '{P(0)}' is disabled. Goldmark does not enable logins. Fix: ALTER LOGIN {Q(P(0))} ENABLE; or configure another login.",
        "login-policy-off" => $"login '{P(0)}' does not enforce SQL Server's password policy. Fix: ALTER LOGIN {Q(P(0))} WITH CHECK_POLICY = ON;",
        "login-expiration-on" => $"login '{P(0)}' has password expiration on, so Goldmark would stop working when it expires. Fix: ALTER LOGIN {Q(P(0))} WITH CHECK_EXPIRATION = OFF;",
        "login-expired" => $"the password of login '{P(0)}' has expired.",
        "login-locked" => $"login '{P(0)}' is locked out. Fix: ALTER LOGIN {Q(P(0))} WITH PASSWORD = '...' UNLOCK; (then set the same password in Goldmark's settings)",
        "login-must-change" => $"login '{P(0)}' must change its password at the next sign-in. Fix: ALTER LOGIN {Q(P(0))} WITH PASSWORD = '...'; (without MUST_CHANGE)",
        "login-connect-denied" => $"CONNECT SQL is denied to {P(1)}, so login '{P(0)}' cannot connect. Fix: REVOKE CONNECT SQL FROM {Q(P(1))};",
        "login-credential" => $"login '{P(0)}' is mapped to credential '{P(1)}'. Fix: ALTER LOGIN {Q(P(0))} DROP CREDENTIAL {Q(P(1))}; (or WITH NO CREDENTIAL)",
        "server-role" => $"login '{P(0)}' is a member of server role {P(1)}. Fix: ALTER SERVER ROLE {Q(P(1))} DROP MEMBER {Q(P(0))};",
        "server-grant" => $"login '{P(0)}' holds {P(1)} on {Target(P(2), P(3))}. Fix: REVOKE {Revocable(P(1))} {On(P(2), P(3))}FROM {Q(P(0))}{Cascade(P(1))};",
        "owns" => $"login '{P(0)}' owns {P(1)} '{P(2)}'. Fix: transfer it (ALTER AUTHORIZATION ... TO dbo, or to another owner).",
        "linked-login" => $"login '{P(0)}' has a login mapping on linked server '{P(1)}'. Fix: EXEC sp_droplinkedsrvlogin @rmtsrvname = {L(P(1))}, @locallogin = {L(P(0))};",
        "remote-login" => $"login '{P(0)}' has a remote-login mapping on server '{P(1)}'. Fix: EXEC sp_dropremotelogin @remoteserver = {L(P(1))}, @loginame = {L(P(0))};",
        "mapped-into" => $"login '{P(0)}' has a user in database '{P(1)}' and holds that user's rights there. Fix: drop that user, or configure another login.",
        "database-access" => $"login '{P(0)}' can enter database '{P(1)}' as user '{P(2)}'. Fix: drop that user (or the group's), or configure another login.",
        "database-role" => $"the database user of login '{P(0)}' is a member of database role {P(1)}. Fix: ALTER ROLE {Q(P(1))} DROP MEMBER {Q(P(0))};",
        "database-grant" => $"the database user of login '{P(0)}' holds {P(1)} on {Target(P(2), P(3))} directly; Goldmark's rights come only through its role. " +
            $"Fix: REVOKE {Revocable(P(1))} {On(P(2), P(3))}FROM {Q(P(0))}{Cascade(P(1))};",
        "database-connect-denied" => $"CONNECT on the Goldmark database is denied to {P(1)}, so login '{P(0)}' cannot use it. Fix: REVOKE CONNECT FROM {Q(P(1))};",
        "server-securable" => $"login '{P(0)}' holds {P(3)} on {P(1)} '{P(2)}'. Fix: REVOKE {P(3)} ON {P(1)}::{Q(P(2))} FROM {Q(P(0))}; (or from the role or group that grants it)",
        "database-securable" => $"login '{P(0)}' holds {P(3)} on {P(1)} '{P(2)}' in the Goldmark database. Fix: revoke it from the user, role or group that grants it.",
        _ => $"{Kind}: {string.Join(", ", Parts)}",
    };

    // Schemas and roles have simple names to quote; for anything else the class and name are named.
    private static string PublicOwnerFix(string kind, string name) => kind switch
    {
        "schema" => $"ALTER AUTHORIZATION ON SCHEMA::{Q(name)} TO dbo;",
        "database principal" => $"ALTER AUTHORIZATION ON ROLE::{Q(name)} TO dbo; (APPLICATION ROLE:: for an application role)",
        _ => $"transfer the {kind} to dbo (ALTER AUTHORIZATION ON <class>::<name> TO dbo).",
    };

    private static string Target(string @class, string name) =>
        @class is "SERVER" or "DATABASE" ? $"the {@class.ToLowerInvariant()}" : $"{@class} {name}";

    // REVOKE ... ON <class>::<name>; nothing for the server or database itself.
    private static string On(string @class, string name) => @class is "SERVER" or "DATABASE" ? "" : $"ON {@class}::{name} ";

    private const string GrantOption = " WITH GRANT OPTION";

    // REVOKE names the permission without WITH GRANT OPTION, and then needs CASCADE.
    private static string Revocable(string permission) =>
        permission.EndsWith(GrantOption, StringComparison.Ordinal) ? permission[..^GrantOption.Length] : permission;

    private static string Cascade(string permission) => permission.EndsWith(GrantOption, StringComparison.Ordinal) ? " CASCADE" : "";

    /// <summary>An identifier as QUOTENAME writes it: in brackets, with <c>]</c> doubled.</summary>
    internal static string Q(string name) => $"[{name.Replace("]", "]]", StringComparison.Ordinal)}]";

    /// <summary>A Unicode string literal, with <c>'</c> doubled.</summary>
    internal static string L(string value) => $"N'{value.Replace("'", "''", StringComparison.Ordinal)}'";

    /// <summary>
    /// Keeps letters, digits and <c>_ . - $ # @ \ / [ ]</c>; a space becomes <c>+</c> and anything
    /// else (including <c>: , %</c> and <c>+</c>) is percent-encoded, so an ID never contains a
    /// separator or white space.
    /// </summary>
    internal static string Encode(string part)
    {
        var builder = new StringBuilder(part.Length);
        foreach (var b in Encoding.UTF8.GetBytes(part))
        {
            var c = (char)b;
            if (b < 0x80 && (char.IsAsciiLetterOrDigit(c) || "_.-$#@\\/[]".Contains(c, StringComparison.Ordinal)))
            {
                builder.Append(c);
            }
            else if (c == ' ')
            {
                builder.Append('+');
            }
            else
            {
                builder.Append('%').Append(b.ToString("X2", CultureInfo.InvariantCulture));
            }
        }

        return builder.ToString();
    }
}

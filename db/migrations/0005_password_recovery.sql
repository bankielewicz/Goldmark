-- 0005 password recovery (slice 4a; issue #25; DEC-042): an administrator's password reset, the
-- password-and-two-factor reset, dual control for the links that need none of the person's own
-- factors, and the operator command recover-admin.
--
-- Forward-only and immutable once merged (db/migrations.sha256, DEC-028). Every table is in schema
-- cmdb, so goldmark_runtime's schema-level DML grant (0002) covers it; nothing here grants more. No
-- secret is stored in a usable form (see 0003).

-- Set when an administrator (or recover-admin) resets the person's password: from then on the old
-- password is refused at sign-in, though its hash stays until the person chooses a new one through
-- the link, which clears this.
ALTER TABLE cmdb.UserAccount ADD PasswordResetAt datetime2(7) NULL;
GO

-- Two new purposes: a password reset (the person keeps their authenticator and proves it at the end)
-- and a password-and-two-factor reset (the invitation's steps).
ALTER TABLE cmdb.UserInvitation DROP CONSTRAINT CK_UserInvitation_Purpose;
ALTER TABLE cmdb.UserInvitation ADD CONSTRAINT CK_UserInvitation_Purpose
    CHECK (Purpose IN ('Invitation', 'TwoFactorReset', 'PasswordReset', 'CombinedReset'));

-- Null for a link issued by recover-admin, run by an operator on the server, who has no account.
ALTER TABLE cmdb.UserInvitation DROP CONSTRAINT FK_UserInvitation_CreatedBy;
ALTER TABLE cmdb.UserInvitation ALTER COLUMN CreatedBy uniqueidentifier NULL;
ALTER TABLE cmdb.UserInvitation ADD CONSTRAINT FK_UserInvitation_CreatedBy
    FOREIGN KEY (CreatedBy) REFERENCES cmdb.UserAccount (Id);

-- Dual control (DEC-042): an invitation, or a password-and-two-factor reset, asked for while another
-- administrator can approve it, has a token but no code until that administrator approves; the
-- approver gets the code, the one who asked has the link. Until then ExpiresAt is when the request
-- ends; approval sets it to the link's own lifetime from then.
--
-- The code's hash: scheme 1 is SHA-256 of the token and the code (0003), used whenever the token is
-- known when the code is made. Scheme 2 is for codes made at approval, when the token is no longer
-- known: HMAC-SHA-256 of the code under a random key made for that link and stored in CodeKey,
-- encrypted with Data Protection. Either way a copy of the database alone gives no way to test codes.
ALTER TABLE cmdb.UserInvitation ALTER COLUMN CodeHash binary(32) NULL;
ALTER TABLE cmdb.UserInvitation ADD
    CodeHashScheme   tinyint          NOT NULL CONSTRAINT DF_UserInvitation_CodeHashScheme DEFAULT (1)
        CONSTRAINT CK_UserInvitation_CodeHashScheme CHECK (CodeHashScheme IN (1, 2)),
    CodeKey          varbinary(512)   NULL,
    ApprovalRequired bit              NOT NULL CONSTRAINT DF_UserInvitation_ApprovalRequired DEFAULT (0),
    ApprovedAt       datetime2(7)     NULL,
    ApprovedBy       uniqueidentifier NULL CONSTRAINT FK_UserInvitation_ApprovedBy REFERENCES cmdb.UserAccount (Id),
    RevokedBy        uniqueidentifier NULL CONSTRAINT FK_UserInvitation_RevokedBy REFERENCES cmdb.UserAccount (Id),
    -- Why the link closed early: a newer link replaced it, an administrator revoked it, too many wrong
    -- codes, another administrator declined the request, or the one who asked cancelled it. Null for
    -- links closed before this migration.
    RevokeReason     varchar(20)      NULL
        CONSTRAINT CK_UserInvitation_RevokeReason CHECK (RevokeReason IN ('Replaced', 'Revoked', 'CodeAttempts', 'Declined', 'Cancelled'));
GO

ALTER TABLE cmdb.UserInvitation ADD
    CONSTRAINT CK_UserInvitation_Code CHECK (CodeHash IS NOT NULL OR (ApprovalRequired = 1 AND ApprovedAt IS NULL)),
    CONSTRAINT CK_UserInvitation_CodeKey CHECK ((CodeHashScheme = 1 AND CodeKey IS NULL) OR (CodeHashScheme = 2 AND CodeKey IS NOT NULL)),
    CONSTRAINT CK_UserInvitation_Approval CHECK (ApprovedAt IS NULL OR (ApprovalRequired = 1 AND ApprovedBy IS NOT NULL));
GO

/*
Read-only preflight for the authentication migration.
Resolve any reported rows deliberately before adding normalized unique indexes.
This script does not modify or delete user data.
*/
WITH NormalizedEmails AS
(
    SELECT
        Id,
        NULLIF(UPPER(LTRIM(RTRIM(Email))), N'') AS NormalizedEmail,
        LEN(LTRIM(RTRIM(Email))) AS EmailLength
    FROM dbo.Users
)
SELECT
    N'Email exceeds 320 characters' AS Issue,
    Id AS UserId,
    EmailLength AS ValueLength
FROM NormalizedEmails
WHERE EmailLength > 320
UNION ALL
SELECT
    N'Email is empty after trimming' AS Issue,
    Id AS UserId,
    EmailLength AS ValueLength
FROM NormalizedEmails
WHERE NormalizedEmail IS NULL
UNION ALL
SELECT
    N'Duplicate normalized email' AS Issue,
    Id AS UserId,
    EmailLength AS ValueLength
FROM NormalizedEmails
WHERE NormalizedEmail IN
(
    SELECT NormalizedEmail
    FROM NormalizedEmails
    WHERE NormalizedEmail IS NOT NULL
    GROUP BY NormalizedEmail
    HAVING COUNT(*) > 1
)
ORDER BY Issue, UserId;

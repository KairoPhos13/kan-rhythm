-- Real accounts start at id 2.
--
-- osu!lazer treats a local user whose id is 1 or less as "no real account"
-- (LocalUserStatisticsProvider.initialiseStatistics returns early for
-- Id <= 1; the guest user is id 0), so the account with id 1 signs in but its
-- statistics never load and the login panel waits forever. Found 2026-11-10
-- with the server's first real account.
--
-- Every foreign key to users(id) gets ON UPDATE CASCADE, an account with an
-- id below 2 moves to the next free id (its tokens, scores and beatmap sets
-- follow), the identity continues after the highest id, and a check keeps
-- ids from ever going below 2 again.

DO $$
DECLARE
  fk record;
BEGIN
  FOR fk IN
    SELECT conrelid::regclass AS tbl, conname, pg_get_constraintdef(oid) AS def
    FROM pg_constraint
    WHERE contype = 'f' AND confrelid = 'users'::regclass AND confupdtype <> 'c'
  LOOP
    EXECUTE format('ALTER TABLE %s DROP CONSTRAINT %I', fk.tbl, fk.conname);
    EXECUTE format('ALTER TABLE %s ADD CONSTRAINT %I %s ON UPDATE CASCADE', fk.tbl, fk.conname, fk.def);
  END LOOP;

  UPDATE users
  SET id = (SELECT GREATEST(COALESCE(max(id), 1), 1) + 1 FROM users)
  WHERE id < 2;

  EXECUTE format(
    'ALTER TABLE users ALTER COLUMN id RESTART WITH %s',
    (SELECT GREATEST(COALESCE(max(id), 1), 1) + 1 FROM users)
  );
END $$;

ALTER TABLE users ADD CONSTRAINT users_id_from_2 CHECK (id >= 2);

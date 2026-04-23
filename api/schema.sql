CREATE EXTENSION IF NOT EXISTS "pgcrypto";

-- Tabelas base

CREATE TABLE IF NOT EXISTS teams (
    id SERIAL PRIMARY KEY,
    emblem VARCHAR(255),
    uniformhome VARCHAR(255),
    uniformaway VARCHAR(255),
    deleted BOOLEAN NOT NULL DEFAULT FALSE,
    sportsid INT,
    -- Compatibilidade com consultas legadas que usam "sportid"
    sportid INT GENERATED ALWAYS AS (sportsid) STORED,
    name VARCHAR(255)
);

CREATE TABLE IF NOT EXISTS championships (
    id SERIAL PRIMARY KEY,
    name VARCHAR(255) NOT NULL,
    sportsid INT,
    initialdate TIMESTAMP,
    finaldate TIMESTAMP,
    rules TEXT,
    logo VARCHAR(255),
    description TEXT,
    format INT,
    organizerid UUID,
    teamquantity INT,
    numberofplayers INT,
    status INT,
    doublematchgroupstage BOOLEAN NOT NULL DEFAULT FALSE,
    doublematcheliminations BOOLEAN NOT NULL DEFAULT FALSE,
    doublestartleaguesystem BOOLEAN NOT NULL DEFAULT FALSE,
    finaldoublematch BOOLEAN NOT NULL DEFAULT FALSE,
    deleted BOOLEAN NOT NULL DEFAULT FALSE
);

CREATE TABLE IF NOT EXISTS users (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    name VARCHAR(255) NOT NULL,
    username VARCHAR(255) NOT NULL UNIQUE,
    passwordhash VARCHAR(255),
    email VARCHAR(255) NOT NULL UNIQUE,
    deleted BOOLEAN NOT NULL DEFAULT FALSE,
    birthday TIMESTAMP,
    cpf VARCHAR(20) UNIQUE,
    cnpj VARCHAR(20) UNIQUE,
    teammanagementid INT,
    playerteamid INT,
    artisticname VARCHAR(255),
    number INT,
    iscaptain BOOLEAN NOT NULL DEFAULT FALSE,
    bio TEXT,
    picture TEXT,
    confirmemail BOOLEAN NOT NULL DEFAULT FALSE,
    role VARCHAR(50) NOT NULL DEFAULT 'user',
    accepted BOOLEAN NOT NULL DEFAULT FALSE,
    playerposition INT,
    championshipid INT,
    CONSTRAINT fk_users_teammanagement FOREIGN KEY (teammanagementid) REFERENCES teams(id),
    CONSTRAINT fk_users_playerteam FOREIGN KEY (playerteamid) REFERENCES teams(id),
    CONSTRAINT fk_users_championship FOREIGN KEY (championshipid) REFERENCES championships(id)
);

CREATE TABLE IF NOT EXISTS playertempprofiles (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    name VARCHAR(255),
    artisticname VARCHAR(255),
    number INT,
    email VARCHAR(255),
    teamsid INT,
    playerposition INT,
    iscaptain BOOLEAN NOT NULL DEFAULT FALSE,
    accepted BOOLEAN NOT NULL DEFAULT FALSE,
    picture TEXT,
    CONSTRAINT fk_playertemp_team FOREIGN KEY (teamsid) REFERENCES teams(id)
);

CREATE TABLE IF NOT EXISTS errorlog (
    id SERIAL PRIMARY KEY,
    message TEXT,
    stacktrace TEXT,
    timeoferror TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP
);

-- Relacionamentos e apoio

CREATE TABLE IF NOT EXISTS championships_teams (
    teamid INT NOT NULL,
    championshipid INT NOT NULL,
    accepted BOOLEAN NOT NULL DEFAULT FALSE,
    PRIMARY KEY (teamid, championshipid),
    CONSTRAINT fk_ct_team FOREIGN KEY (teamid) REFERENCES teams(id),
    CONSTRAINT fk_ct_championship FOREIGN KEY (championshipid) REFERENCES championships(id)
);

CREATE TABLE IF NOT EXISTS organizers (
    organizerid UUID NOT NULL,
    championshipid INT NOT NULL,
    mainorganizer BOOLEAN NOT NULL DEFAULT FALSE,
    PRIMARY KEY (organizerid, championshipid),
    CONSTRAINT fk_organizers_user FOREIGN KEY (organizerid) REFERENCES users(id),
    CONSTRAINT fk_organizers_championship FOREIGN KEY (championshipid) REFERENCES championships(id)
);

CREATE TABLE IF NOT EXISTS championshipactivitylog (
    id SERIAL PRIMARY KEY,
    dateofactivity TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
    typeofactivity INT,
    championshipid INT,
    organizerid UUID,
    CONSTRAINT fk_activity_championship FOREIGN KEY (championshipid) REFERENCES championships(id),
    CONSTRAINT fk_activity_organizer FOREIGN KEY (organizerid) REFERENCES users(id)
);

CREATE TABLE IF NOT EXISTS classifications (
    id SERIAL PRIMARY KEY,
    points INT NOT NULL DEFAULT 0,
    teamid INT NOT NULL,
    championshipid INT NOT NULL,
    position INT,
    CONSTRAINT fk_classifications_team FOREIGN KEY (teamid) REFERENCES teams(id),
    CONSTRAINT fk_classifications_championship FOREIGN KEY (championshipid) REFERENCES championships(id)
);

-- Partidas e eventos

CREATE TABLE IF NOT EXISTS matches (
    id SERIAL PRIMARY KEY,
    winner INT,
    home INT,
    visitor INT,
    arbitrator VARCHAR(255),
    championshipid INT,
    date TIMESTAMP,
    round INT,
    phase INT,
    tied BOOLEAN NOT NULL DEFAULT FALSE,
    previousmatch INT,
    homeuniform VARCHAR(255),
    visitoruniform VARCHAR(255),
    prorrogation BOOLEAN NOT NULL DEFAULT FALSE,
    cep INT,
    city VARCHAR(255),
    road VARCHAR(255),
    number INT,
    matchreport TEXT,
    penalties BOOLEAN NOT NULL DEFAULT FALSE,
    CONSTRAINT fk_matches_home FOREIGN KEY (home) REFERENCES teams(id),
    CONSTRAINT fk_matches_visitor FOREIGN KEY (visitor) REFERENCES teams(id),
    CONSTRAINT fk_matches_championship FOREIGN KEY (championshipid) REFERENCES championships(id),
    CONSTRAINT fk_matches_previousmatch FOREIGN KEY (previousmatch) REFERENCES matches(id)
);

CREATE TABLE IF NOT EXISTS goals (
    id SERIAL PRIMARY KEY,
    matchid INT NOT NULL,
    teamid INT NOT NULL,
    playerid UUID,
    playertempid UUID,
    set INT,
    owngoal BOOLEAN NOT NULL DEFAULT FALSE,
    assisterplayerid UUID,
    assisterplayertempid UUID,
    minutes INT,
    date TIMESTAMP,
    CONSTRAINT fk_goals_match FOREIGN KEY (matchid) REFERENCES matches(id),
    CONSTRAINT fk_goals_team FOREIGN KEY (teamid) REFERENCES teams(id),
    CONSTRAINT fk_goals_player FOREIGN KEY (playerid) REFERENCES users(id),
    CONSTRAINT fk_goals_playertemp FOREIGN KEY (playertempid) REFERENCES playertempprofiles(id),
    CONSTRAINT fk_goals_assister_player FOREIGN KEY (assisterplayerid) REFERENCES users(id),
    CONSTRAINT fk_goals_assister_playertemp FOREIGN KEY (assisterplayertempid) REFERENCES playertempprofiles(id)
);

CREATE TABLE IF NOT EXISTS fouls (
    id SERIAL PRIMARY KEY,
    yellowcard BOOLEAN NOT NULL DEFAULT FALSE,
    considered BOOLEAN NOT NULL DEFAULT FALSE,
    matchid INT NOT NULL,
    playerid UUID,
    playertempid UUID,
    minutes INT,
    valid BOOLEAN NOT NULL DEFAULT TRUE,
    CONSTRAINT fk_fouls_match FOREIGN KEY (matchid) REFERENCES matches(id),
    CONSTRAINT fk_fouls_player FOREIGN KEY (playerid) REFERENCES users(id),
    CONSTRAINT fk_fouls_playertemp FOREIGN KEY (playertempid) REFERENCES playertempprofiles(id)
);

CREATE TABLE IF NOT EXISTS penalties (
    id SERIAL PRIMARY KEY,
    matchid INT NOT NULL,
    teamid INT NOT NULL,
    playerid UUID,
    playertempid UUID,
    converted BOOLEAN NOT NULL DEFAULT FALSE,
    CONSTRAINT fk_penalties_match FOREIGN KEY (matchid) REFERENCES matches(id),
    CONSTRAINT fk_penalties_team FOREIGN KEY (teamid) REFERENCES teams(id),
    CONSTRAINT fk_penalties_player FOREIGN KEY (playerid) REFERENCES users(id),
    CONSTRAINT fk_penalties_playertemp FOREIGN KEY (playertempid) REFERENCES playertempprofiles(id)
);

CREATE TABLE IF NOT EXISTS firststringplayers (
    playerid UUID,
    playertempid UUID,
    matchid INT NOT NULL,
    teamid INT NOT NULL,
    position INT,
    line INT,
    PRIMARY KEY (matchid, teamid, position, line),
    CONSTRAINT fk_firststring_match FOREIGN KEY (matchid) REFERENCES matches(id),
    CONSTRAINT fk_firststring_team FOREIGN KEY (teamid) REFERENCES teams(id),
    CONSTRAINT fk_firststring_player FOREIGN KEY (playerid) REFERENCES users(id),
    CONSTRAINT fk_firststring_playertemp FOREIGN KEY (playertempid) REFERENCES playertempprofiles(id)
);

CREATE TABLE IF NOT EXISTS replacements (
    id SERIAL PRIMARY KEY,
    replacedid UUID,
    replacedtempid UUID,
    replacerid UUID,
    replacertempid UUID,
    matchid INT NOT NULL,
    teamid INT NOT NULL,
    CONSTRAINT fk_replacements_match FOREIGN KEY (matchid) REFERENCES matches(id),
    CONSTRAINT fk_replacements_team FOREIGN KEY (teamid) REFERENCES teams(id),
    CONSTRAINT fk_replacements_replaced_user FOREIGN KEY (replacedid) REFERENCES users(id),
    CONSTRAINT fk_replacements_replaced_temp FOREIGN KEY (replacedtempid) REFERENCES playertempprofiles(id),
    CONSTRAINT fk_replacements_replacer_user FOREIGN KEY (replacerid) REFERENCES users(id),
    CONSTRAINT fk_replacements_replacer_temp FOREIGN KEY (replacertempid) REFERENCES playertempprofiles(id)
);

CREATE TABLE IF NOT EXISTS reports (
    id SERIAL PRIMARY KEY,
    authorid UUID NOT NULL,
    completed BOOLEAN NOT NULL DEFAULT FALSE,
    description TEXT,
    reporttype INT,
    reporteduserid UUID,
    reportedteamid INT,
    reportedchampionshipid INT,
    reportedplayertempid UUID,
    violation INT,
    CONSTRAINT fk_reports_author FOREIGN KEY (authorid) REFERENCES users(id),
    CONSTRAINT fk_reports_user FOREIGN KEY (reporteduserid) REFERENCES users(id),
    CONSTRAINT fk_reports_team FOREIGN KEY (reportedteamid) REFERENCES teams(id),
    CONSTRAINT fk_reports_championship FOREIGN KEY (reportedchampionshipid) REFERENCES championships(id),
    CONSTRAINT fk_reports_playertemp FOREIGN KEY (reportedplayertempid) REFERENCES playertempprofiles(id)
);
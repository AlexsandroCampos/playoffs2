using Dapper;
using Npgsql;
using System.Text.Json;
using Microsoft.Extensions.Options;
using PlayOffs.Worker.Domain; // Confirme se é este o namespace dos seus DTOs

namespace PlayOffs.Worker.Processing;

public class StandingsBuilderService
{
    private readonly string _connectionString;
    private readonly StackExchange.Redis.ConnectionMultiplexer _redis;
    private FakeDbService _dbService; // O nosso truque para não precisar refatorar seu código

    public StandingsBuilderService(IOptions<WorkerOptions> options)
    {
        _connectionString = options.Value.Postgres.ConnectionString;
        _redis = StackExchange.Redis.ConnectionMultiplexer.Connect(options.Value.Redis.ConnectionString);
    }

    public async Task<string> BuildStandingsJsonAsync(int championshipId)
    {
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();
        _dbService = new FakeDbService(conn); // Inicia a conexão para os seus métodos usarem

        var classifications = await GetClassificationsValidationAsync(championshipId);

        // Gera o JSON final perfeito e em camelCase para a API consumir
        return JsonSerializer.Serialize(classifications, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
    }

    // ====================================================================
    // O TRUQUE MÁGICO: Imita o DbService da sua API usando o Dapper
    // ====================================================================
    private class FakeDbService
    {
        private readonly NpgsqlConnection _conn;
        public FakeDbService(NpgsqlConnection conn) => _conn = conn;
        public async Task<T> GetAsync<T>(string sql, object param = null) => await _conn.QueryFirstOrDefaultAsync<T>(sql, param);
        public async Task<List<T>> GetAll<T>(string sql, object param = null) => (await _conn.QueryAsync<T>(sql, param)).ToList();
    }

    // ====================================================================
    // MÉTODO PRINCIPAL ADAPTADO
    // ====================================================================
    private async Task<List<ClassificationDTO>> GetClassificationsValidationAsync(int championshipId)
    {
        var championship = await GetChampionshipByIdSend(championshipId);
        if (championship is null)
        {
            // Isso é um erro de verdade — não devia acontecer nunca. Melhor gritar do que sumir.
            throw new ApplicationException($"Campeonato {championshipId} não encontrado ao calcular classificação.");
        }

        if (championship.Format == Enum.Format.Knockout)
        {
            // Isso é esperado (mata-mata não tem classificação) — mas sinalizamos com null,
            // para o handler saber que não deve sobrescrever nada no Redis.
            return null;
        }

        // Lê os cartões do Redis direto no background!
        var dbRedis = _redis.GetDatabase();
        var cardsJson = await dbRedis.StringGetAsync($"championship:{championshipId}:cards");
        var cachedCards = string.IsNullOrEmpty(cardsJson) 
            ? new List<CardDTO>() 
            : JsonSerializer.Deserialize<List<CardDTO>>(cardsJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        if(championship.SportsId == Enum.Sports.Football)
        {
            var classifications = await GetAllClassificationsByChampionshipId(championshipId);
            var classificationsDTO = new List<ClassificationDTO>();

            if(championship.Format == Enum.Format.LeagueSystem)
            {
                classifications = classifications.OrderBy(c => c.Position).ToList();
                foreach (var classification in classifications)
                {
                    var classificationDTO = new ClassificationDTO();
                    var team = await GetByTeamIdSendAsync(classification.TeamId);
                    if (team is null) continue;
                    
                    classificationDTO.Position = classification.Position;
                    classificationDTO.Points = classification.Points;
                    classificationDTO.Emblem = team.Emblem;
                    classificationDTO.Name = team.Name;
                    classificationDTO.TeamId = team.Id;
                    classificationDTO.Wins = await AmountOfWins(team.Id, championshipId);
                    classificationDTO.GoalBalance = await GoalDifference(team.Id, championshipId);
                    classificationDTO.ProGoals = await ProGoals(team.Id, championshipId);
                    classificationDTO.AmountOfMatches = await AmountOfMatches(team.Id, championshipId);
                    classificationDTO.LastMatches = await GetLast3Matches(team.Id, championshipId);
                    classificationDTO.LastResults = GetResults(classificationDTO);
                    
                    if (cachedCards != null && cachedCards.Any())
                    {
                        classificationDTO.RedCard = cachedCards.Where(c => c.TeamId == team.Id).Sum(c => c.RedCards);
                        classificationDTO.YellowCard = cachedCards.Where(c => c.TeamId == team.Id).Sum(c => c.YellowCards);
                    }
                    else
                    {
                        classificationDTO.RedCard = await AmountOfRedCards(team.Id, championshipId); 
                        classificationDTO.YellowCard = await AmountOfYellowCards(team.Id, championshipId); 
                    }
                    classificationsDTO.Add(classificationDTO);
                }
                return classificationsDTO;
            }
            else if(championship.Format == Enum.Format.GroupStage)
            {
                var classificationsDTOOrdered = new List<Classification>();
                for (int i = 0; i < classifications.Count(); i += 4)
                {
                    List<Classification> group = classifications.Skip(i).Take(4).ToList();
                    group = group.OrderBy(c => c.Position).ToList();
                    classificationsDTOOrdered.AddRange(group);   
                }
                foreach (var classification in classificationsDTOOrdered)
                {
                    var classificationDTO = new ClassificationDTO();
                    var team = await GetByTeamIdSendAsync(classification.TeamId);
                    classificationDTO.Position = classification.Position;
                    classificationDTO.Points = classification.Points;
                    classificationDTO.TeamId = team.Id;
                    classificationDTO.Emblem = team.Emblem;
                    classificationDTO.Name = team.Name;
                    classificationDTO.Wins = await AmountOfWins(team.Id, championshipId);
                    classificationDTO.GoalBalance = await GoalDifference(team.Id, championshipId);
                    classificationDTO.ProGoals = await ProGoals(team.Id, championshipId);
                    classificationDTO.AmountOfMatches = await AmountOfMatches(team.Id, championshipId);
                    classificationDTO.LastMatches = await GetLast3Matches(team.Id, championshipId);
                    classificationDTO.LastResults = GetResults(classificationDTO);

                    if (cachedCards != null && cachedCards.Any())
                    {
                        classificationDTO.RedCard = cachedCards.Where(c => c.TeamId == team.Id).Sum(c => c.RedCards);
                        classificationDTO.YellowCard = cachedCards.Where(c => c.TeamId == team.Id).Sum(c => c.YellowCards);
                    }
                    else
                    {
                        classificationDTO.RedCard = await AmountOfRedCards(team.Id, championshipId); 
                        classificationDTO.YellowCard = await AmountOfYellowCards(team.Id, championshipId); 
                    }
                    classificationsDTO.Add(classificationDTO);
                }
                return classificationsDTO;
            }
        }
        else
        {
            var classifications = await GetAllClassificationsByChampionshipId(championshipId);
            var classificationsDTO = new List<ClassificationDTO>();

            if(championship.Format == Enum.Format.LeagueSystem)
            {
                classifications = classifications.OrderBy(c => c.Position).ToList();
                foreach (var classification in classifications)
                {
                    var classificationDTO = new ClassificationDTO();
                    var team = await GetByTeamIdSendAsync(classification.TeamId);
                    classificationDTO.Position = classification.Position;
                    classificationDTO.Points = classification.Points;
                    classificationDTO.Emblem = team.Emblem;
                    classificationDTO.Name = team.Name;
                    classificationDTO.TeamId = team.Id;
                    classificationDTO.Wins = await AmountOfWins(team.Id, championshipId);
                    classificationDTO.WinningSets = await WinningSets(team.Id, championshipId);
                    classificationDTO.LosingSets = await LosingSets(team.Id, championshipId);
                    classificationDTO.ProPoints = await ProGoals(team.Id, championshipId);
                    classificationDTO.PointsAgainst = await PointsAgainst(team.Id, championshipId);
                    classificationDTO.AmountOfMatches = await AmountOfMatches(team.Id, championshipId);
                    classificationDTO.LastMatches = await GetLast3MatchesVolley(team.Id, championshipId);
                    classificationDTO.LastResults = await GetResultsToVolley(classificationDTO, team.Id);
                    classificationsDTO.Add(classificationDTO);
                }
                return classificationsDTO;
            }
            else if(championship.Format == Enum.Format.GroupStage)
            {
                var classificationsDTOOrdered = new List<Classification>();
                for (int i = 0; i < classifications.Count(); i += 4)
                {
                    List<Classification> group = classifications.Skip(i).Take(4).ToList();
                    group = group.OrderBy(c => c.Position).ToList();
                    classificationsDTOOrdered.AddRange(group);   
                }
                foreach (var classification in classifications)
                {
                    var classificationDTO = new ClassificationDTO();
                    var team = await GetByTeamIdSendAsync(classification.TeamId);
                    classificationDTO.Position = classification.Position;
                    classificationDTO.Points = classification.Points;
                    classificationDTO.Emblem = team.Emblem;
                    classificationDTO.Name = team.Name;
                    classificationDTO.TeamId = team.Id;
                    classificationDTO.Wins = await AmountOfWins(team.Id, championshipId);
                    classificationDTO.WinningSets = await WinningSets(team.Id, championshipId);
                    classificationDTO.LosingSets = await LosingSets(team.Id, championshipId);
                    classificationDTO.ProPoints = await ProGoals(team.Id, championshipId);
                    classificationDTO.PointsAgainst = await PointsAgainst(team.Id, championshipId);
                    classificationDTO.AmountOfMatches = await AmountOfMatches(team.Id, championshipId);
                    classificationDTO.LastMatches = await GetLast3Matches(team.Id, championshipId);
                    classificationDTO.LastResults = await GetResultsToVolley(classificationDTO, team.Id);
                    classificationsDTO.Add(classificationDTO);
                }
                return classificationsDTO;
            }
        }
        return new();
    }

    // ====================================================================
    // VÁ NO SEU StatisticsService DA API, COPIE TODOS OS MÉTODOS PRIVADOS 
    // E COLE-OS AQUI ABAIXO! (AmountOfWins, GoalDifference, etc...)
    // ====================================================================

    private async Task<int> AmountOfRedCards(int teamId, int championshipId)
    {
        var tempCards = await _dbService.GetAsync<int>(
            @"SELECT COUNT(*)
            FROM Fouls f
            JOIN PlayerTempProfiles p ON f.PlayerTempId = p.Id
            JOIN Matches m ON m.Id = f.MatchId
            WHERE p.TeamsId = @teamId AND m.ChampionshipId = @championshipId AND f.YellowCard = false;", 
            new {teamId, championshipId});
        var userCards = await _dbService.GetAsync<int>(
            @"SELECT COUNT(*)
            FROM Fouls f
            JOIN Users u ON f.PlayerId = u.Id
            JOIN Matches m ON m.Id = f.MatchId
            WHERE u.PlayerTeamId = @teamId AND m.ChampionshipId = @championshipId AND f.YellowCard = false;", 
            new {teamId, championshipId});
        return tempCards + userCards;
    }
    private async Task<int> AmountOfYellowCards(int teamId, int championshipId)
    {
        var tempCards = await _dbService.GetAsync<int>(
            @"SELECT COUNT(*)
            FROM Fouls f
            JOIN PlayerTempProfiles p ON f.PlayerTempId = p.Id
            JOIN Matches m ON m.Id = f.MatchId
            WHERE p.TeamsId = @teamId AND m.ChampionshipId = @championshipId AND f.YellowCard = true AND f.Considered = true;", 
            new {teamId, championshipId});
        var userCards = await _dbService.GetAsync<int>(
            @"SELECT COUNT(*)
            FROM Fouls f
            JOIN Users u ON f.PlayerId = u.Id
            JOIN Matches m ON m.Id = f.MatchId
            WHERE u.PlayerTeamId = @teamId AND m.ChampionshipId = @championshipId AND f.YellowCard = true AND f.Considered = true;", 
            new {teamId, championshipId});
        return tempCards + userCards;
    }
    private async Task<Championship> GetChampionshipByIdSend(int id) 
	    => await _dbService.GetAsync<Championship>("SELECT * FROM championships WHERE id = @id", new { id });
	private async Task<List<Classification>> GetAllClassificationsByChampionshipId(int championshipId)
        => await _dbService.GetAll<Classification>("SELECT * FROM classifications WHERE ChampionshipId = @ChampionshipId ORDER BY Id", new {championshipId});
	private async Task<Team> GetByTeamIdSendAsync(int id, bool returnDeletedTeams = false) => await _dbService.GetAsync<Team>("SELECT * FROM teams where id=@id AND deleted = @deleted", new { id, deleted = returnDeletedTeams});
    private async Task<int> AmountOfWins(int teamId, int championshipId)
        => await _dbService.GetAsync<int>(
            "SELECT COUNT(*) FROM matches WHERE ChampionshipId = @championshipId AND Winner = @teamId", 
            new {teamId, championshipId});
    private async Task<int> GoalDifference(int teamId, int championshipId)
    {
        var goalsScored = await ProGoals(teamId, championshipId);
        var goalsConceded = await _dbService.GetAsync<int>(
            @"SELECT  COALESCE(SUM(TotalGoals), 0) AS GrandTotalGoals
            FROM (
                SELECT g.TeamId, COUNT(g.Id) AS TotalGoals
                FROM Goals g
                JOIN Matches m ON g.MatchId = m.Id
                WHERE m.ChampionshipId = @championshipId AND
                    (m.Visitor = @teamId OR m.Home = @teamId) AND 
                    (g.TeamId <> @teamId AND g.OwnGoal = false OR g.TeamId = @teamId AND g.OwnGoal = true)
                GROUP BY g.TeamId
            ) AS SubqueryAlias;",
            new { championshipId, teamId });
        var result = goalsScored - goalsConceded;
        return result;
    }
    private async Task<int> ProGoals(int teamId, int championshipId)
        => await _dbService.GetAsync<int>(
            @"SELECT COUNT(g.Id)
            FROM Goals g
            JOIN Matches m ON g.MatchId = m.Id
            WHERE m.ChampionshipId = @championshipId AND (m.Visitor = @teamId OR m.Home = @teamId) AND
            ((g.TeamId = @teamId AND g.OwnGoal = false) OR (g.TeamId <> @teamId AND g.OwnGoal = true))",
            new { championshipId, teamId });
    private async Task<int> AmountOfMatches(int teamId, int championshipId)
        => await _dbService.GetAsync<int>(
            @"SELECT COUNT(Id) FROM matches 
            WHERE (Visitor = @teamId OR Home = @teamId) AND 
            ChampionshipId = @championshipId AND
            (Winner IS NOT NULL OR Tied = TRUE)", 
            new {teamId, championshipId});
    private async Task<List<MatchDTO>> GetLast3Matches(int teamId, int championshipId)
    {
        var matches = await _dbService.GetAll<Match>(
            @"SELECT * FROM matches 
            WHERE (Visitor = @teamId OR Home = @teamId) AND 
            ChampionshipId = @championshipId AND
            (Winner IS NOT NULL OR Tied = TRUE)
            ORDER BY Id DESC
            LIMIT 3", 
            new {teamId, championshipId});
        var matchesDTO = new List<MatchDTO>();
        foreach (var match in matches)
        {
            var matchDTO = new MatchDTO();
            var homeTeam = await GetByTeamIdSendAsync(match.Home);
            var visitorTeam = await GetByTeamIdSendAsync(match.Visitor);

            if (homeTeam is null || visitorTeam is null)
                continue;
            
            matchDTO.Id = match.Id;
            matchDTO.HomeEmblem = homeTeam.Emblem;
            matchDTO.HomeName = homeTeam.Name;
            matchDTO.HomeId = match.Home;
            matchDTO.IsSoccer = true;
            matchDTO.HomeGoals = await GetPointsFromTeamById(match.Id, match.Home);
            matchDTO.VisitorGoals = await GetPointsFromTeamById(match.Id, match.Visitor);
            matchDTO.VisitorEmblem = visitorTeam.Emblem;
            matchDTO.VisitorName = visitorTeam.Name;
            matchDTO.VisitorId = match.Visitor;
            matchDTO.Finished = true;
            matchesDTO.Add(matchDTO);
        }
        return matchesDTO;
    }
    private async Task<List<MatchDTO>> GetLast3MatchesVolley(int teamId, int championshipId)
    {
        var matches = await _dbService.GetAll<Match>(
            @"SELECT * FROM matches 
            WHERE (Visitor = @teamId OR Home = @teamId) AND 
            ChampionshipId = @championshipId AND
            (Winner IS NOT NULL OR Tied = TRUE)
            ORDER BY Date DESC
            LIMIT 3", 
            new {teamId, championshipId});
        var matchesDTO = new List<MatchDTO>();
        foreach (var match in matches)
        {
            var matchDTO = new MatchDTO();
            var homeTeam = await GetByTeamIdSendAsync(match.Home);
            var visitorTeam = await GetByTeamIdSendAsync(match.Visitor);
            matchDTO.Id = match.Id;
            matchDTO.HomeId = match.Home;
            matchDTO.HomeEmblem = homeTeam.Emblem;
            matchDTO.HomeName = homeTeam.Name;
            matchDTO.VisitorEmblem = visitorTeam.Emblem;
            matchDTO.VisitorName = visitorTeam.Name;
            matchDTO.VisitorId = match.Visitor;
            matchDTO.Finished = true;
            var pointsForSet = new List<int>();
            var pointsForSet2 = new List<int>();
            var WonSets = 0;
            var WonSets2 = 0;
            var lastSet = 0;
            lastSet = !await IsItFirstSet(match.Id) ? 1 : await GetLastSet(match.Id);
            var team2Id = await _dbService.GetAsync<int>("SELECT CASE WHEN home <> @teamId THEN home ELSE visitor END AS selected_team FROM matches WHERE id = @matchId;", new {teamId, matchId = match.Id});

            for (int i = 0;  i < lastSet; i++)
            {
                pointsForSet.Add(await _dbService.GetAsync<int>("select count(*) from goals where MatchId = @matchId AND (TeamId = @teamId And OwnGoal = false OR TeamId <> @teamId And OwnGoal = true) AND Set = @j", new {matchId = match.Id, teamId, j = i+1}));
                pointsForSet2.Add(await _dbService.GetAsync<int>("select count(*) from goals where MatchId = @matchId AND (TeamId <> @teamId And OwnGoal = false OR TeamId = @teamId And OwnGoal = true) AND Set = @j", new {matchId = match.Id, teamId, j = i+1}));
            }

            for (int i = 0;  i < lastSet; i++)
            {
                if(i != 4)
                {
                    if(pointsForSet[i] == 25 && pointsForSet2[i] < 24)
                    {
                        WonSets++;
                    }
                    else if(pointsForSet[i] < 24 && pointsForSet2[i] == 25)
                    {
                        WonSets2++;
                    }
                    else if(pointsForSet[i] >= 24 && pointsForSet2[i] >= 24)
                    {
                        if(pointsForSet[i] - pointsForSet2[i] == 2)
                        {
                            WonSets++;
                        }
                        else if(pointsForSet[i] - pointsForSet2[i] == -2)
                        {
                            WonSets2++;

                        }
                    }
                }

                else
                {
                    if(pointsForSet[i] == 15 && pointsForSet2[i] < 14)
                    {
                        WonSets++;
                    }
                    else if(pointsForSet[i] < 14 && pointsForSet2[i] == 15)
                    {
                        WonSets2++;
                    }
                    else if(pointsForSet[i] >= 14 && pointsForSet2[i] >= 14)
                    {
                        if(pointsForSet[i] - pointsForSet2[i] == 2)
                        {
                            WonSets++;
                        }
                        else if(pointsForSet[i] - pointsForSet2[i] == -2)
                        {
                            WonSets2++;

                        }
                    }
                }
            }
            if(match.Home == teamId)
            {
                matchDTO.HomeWinnigSets = WonSets;
                matchDTO.VisitorWinnigSets = WonSets2;
            }
            else
            {
                matchDTO.HomeWinnigSets = WonSets2;
                matchDTO.VisitorWinnigSets = WonSets;
            }
            matchesDTO.Add(matchDTO);
        }
        return matchesDTO;
    }
    private async Task<int> GetPointsFromTeamById(int matchId, int teamId)
        => await _dbService.GetAsync<int>("SELECT COUNT(*) FROM goals WHERE MatchId = @matchId AND (TeamId = @teamId AND OwnGoal = false OR TeamId <> @teamId AND OwnGoal = true)", new {matchId, teamId});
   private List<LastResultsDTO> GetResults(ClassificationDTO classification)
   {
    var results = new List<LastResultsDTO>();
    foreach (var match in classification.LastMatches)
    {
        var result = new LastResultsDTO();
        if(match.HomeGoals == match.VisitorGoals)
        {
            result.Tied = true;
        }
        else if(match.HomeGoals > match.VisitorGoals)
        {
            if(match.HomeId == classification.TeamId)
            {
                result.Won = true;
            }
            else
            {
                result.Lose = true;
            }
        }
        else
        {
            if(match.HomeId == classification.TeamId)
            {
                result.Lose = true;
            }
            else
            {
                result.Won = true;
            }
        }

        results.Add(result);
    }
    return results;
   }
   private async Task<List<LastResultsDTO>> GetResultsToVolley(ClassificationDTO classificationDTO, int teamId)
   {
    var results = new List<LastResultsDTO>();
    foreach (var match in classificationDTO.LastMatches)
    {
        var result = new LastResultsDTO();

        if(match.HomeWinnigSets == 3)
        {
            if(match.HomeId == classificationDTO.TeamId)
            {
                result.Won = true;
            }
            else
            {
                result.Lose = true;
            } 
        }
        else if(match.VisitorWinnigSets == 3)
        {
            if(match.VisitorId == classificationDTO.TeamId)
            {
                result.Won = true;
            }
            else
            {
                result.Lose = true;
            } 
        }
        results.Add(result);
    }
    return results;
   }
   private async Task<int> WinningSets(int teamId, int championshipId)
    {
        var matches = await _dbService.GetAll<Match>(
            "SELECT * FROM Matches WHERE (Visitor = @teamId OR Home = @teamId) AND ChampionshipId = @championshipId",
            new {teamId, championshipId});
        var allSetsWon = 0;
         
        foreach (var match in matches)
        {
            var pointsForSet = new List<int>();
            var pointsForSet2 = new List<int>();
            var WonSets = 0;
            var WonSets2 = 0;
            var lastSet = 0;
            lastSet = !await IsItFirstSet(match.Id) ? 1 : await GetLastSet(match.Id);
            var team2Id = await _dbService.GetAsync<int>("SELECT CASE WHEN home <> @teamId THEN home ELSE visitor END AS selected_team FROM matches WHERE id = @matchId;", new {teamId, matchId = match.Id});

            for (int i = 0;  i < lastSet; i++)
            {
                pointsForSet.Add(await _dbService.GetAsync<int>("select count(*) from goals where MatchId = @matchId AND (TeamId = @teamId And OwnGoal = false OR TeamId <> @teamId And OwnGoal = true) AND Set = @j", new {matchId = match.Id, teamId, j = i+1}));
                pointsForSet2.Add(await _dbService.GetAsync<int>("select count(*) from goals where MatchId = @matchId AND (TeamId <> @teamId And OwnGoal = false OR TeamId = @teamId And OwnGoal = true) AND Set = @j", new {matchId = match.Id, teamId, j = i+1}));
            }

            for (int i = 0;  i < lastSet; i++)
            {
                if(i != 4)
                {
                    if(pointsForSet[i] == 25 && pointsForSet2[i] < 24)
                    {
                        WonSets++;
                    }
                    else if(pointsForSet[i] < 24 && pointsForSet2[i] == 25)
                    {
                        WonSets2++;
                    }
                    else if(pointsForSet[i] >= 24 && pointsForSet2[i] >= 24)
                    {
                        if(pointsForSet[i] - pointsForSet2[i] == 2)
                        {
                            WonSets++;
                        }
                        else if(pointsForSet[i] - pointsForSet2[i] == -2)
                        {
                            WonSets2++;

                        }
                    }
                }

                else
                {
                    if(pointsForSet[i] == 15 && pointsForSet2[i] < 14)
                    {
                        WonSets++;
                    }
                    else if(pointsForSet[i] < 14 && pointsForSet2[i] == 15)
                    {
                        WonSets2++;
                    }
                    else if(pointsForSet[i] >= 14 && pointsForSet2[i] >= 14)
                    {
                        if(pointsForSet[i] - pointsForSet2[i] == 2)
                        {
                            WonSets++;
                        }
                        else if(pointsForSet[i] - pointsForSet2[i] == -2)
                        {
                            WonSets2++;

                        }
                    }

                }
            }

            allSetsWon = allSetsWon + WonSets;  
        }
        return allSetsWon;
    }
    private async Task<int> GetLastSet(int matchId)
        => await _dbService.GetAsync<int>("SELECT MAX(Set) from goals where MatchId = @matchId", new {matchId});
    private async Task<bool> IsItFirstSet(int matchId)
        => await _dbService.GetAsync<bool>("SELECT EXISTS(SELECT * FROM goals WHERE MatchId = @matchId);", new {matchId});
   private async Task<int> LosingSets(int teamId, int championshipId)
    {
        var matches = await _dbService.GetAll<Match>(
            "SELECT * FROM Matches WHERE (Visitor = @teamId OR Home = @teamId) AND ChampionshipId = @championshipId",
            new {teamId, championshipId});
        var allLosingSets = 0;
         
        foreach (var match in matches)
        {
            var pointsForSet = new List<int>();
            var pointsForSet2 = new List<int>();
            var WonSets = 0;
            var WonSets2 = 0;
            var lastSet = 0;
            lastSet = !await IsItFirstSet(match.Id) ? 1 : await GetLastSet(match.Id);
            var team2Id = await _dbService.GetAsync<int>("SELECT CASE WHEN home <> @teamId THEN home ELSE visitor END AS selected_team FROM matches WHERE id = @matchId;", new {teamId, matchId = match.Id});

            for (int i = 0;  i < lastSet; i++)
            {
                pointsForSet.Add(await _dbService.GetAsync<int>("select count(*) from goals where MatchId = @matchId AND (TeamId = @teamId And OwnGoal = false OR TeamId <> @teamId And OwnGoal = true) AND Set = @j", new {matchId = match.Id, teamId, j = i+1}));
                pointsForSet2.Add(await _dbService.GetAsync<int>("select count(*) from goals where MatchId = @matchId AND (TeamId <> @teamId And OwnGoal = false OR TeamId = @teamId And OwnGoal = true) AND Set = @j", new {matchId = match.Id, teamId, j = i+1}));
            }

            for (int i = 0;  i < lastSet; i++)
            {
                if(i != 4)
                {
                    if(pointsForSet[i] == 25 && pointsForSet2[i] < 24)
                    {
                        WonSets++;
                    }
                    else if(pointsForSet[i] < 24 && pointsForSet2[i] == 25)
                    {
                        WonSets2++;
                    }
                    else if(pointsForSet[i] >= 24 && pointsForSet2[i] >= 24)
                    {
                        if(pointsForSet[i] - pointsForSet2[i] == 2)
                        {
                            WonSets++;
                        }
                        else if(pointsForSet[i] - pointsForSet2[i] == -2)
                        {
                            WonSets2++;

                        }
                    }
                }

                else
                {
                    if(pointsForSet[i] == 15 && pointsForSet2[i] < 14)
                    {
                        WonSets++;
                    }
                    else if(pointsForSet[i] < 14 && pointsForSet2[i] == 15)
                    {
                        WonSets2++;
                    }
                    else if(pointsForSet[i] >= 14 && pointsForSet2[i] >= 14)
                    {
                        if(pointsForSet[i] - pointsForSet2[i] == 2)
                        {
                            WonSets++;
                        }
                        else if(pointsForSet[i] - pointsForSet2[i] == -2)
                        {
                            WonSets2++;

                        }
                    }
                }
            }
            allLosingSets = allLosingSets + WonSets2;  
        }
        return allLosingSets;
    }
    private async Task<int> PointsAgainst(int teamId, int championshipId)
        => await _dbService.GetAsync<int>(
            @"SELECT COALESCE(SUM(TotalGoals), 0) AS GrandTotalGoals
            FROM (
                SELECT g.TeamId, COUNT(g.Id) AS TotalGoals
                FROM Goals g
                JOIN Matches m ON g.MatchId = m.Id
                WHERE m.ChampionshipId = @championshipId AND
                    (m.Visitor = @teamId OR m.Home = @teamId) AND 
                    (g.TeamId <> @teamId AND g.OwnGoal = false OR g.TeamId = @teamId AND g.OwnGoal = true)
                GROUP BY g.TeamId
            ) AS SubqueryAlias;",
            new { championshipId, teamId });


}

// Modelos simplificados embutidos para que o arquivo não dê erro de compilação
public class Championship { public int Id { get; set; } public Enum.Format Format { get; set; } public Enum.Sports SportsId { get; set; } }
public class Classification { public int Id { get; set; } public int TeamId { get; set; } public int Position { get; set; } public int Points { get; set; } public int ChampionshipId { get; set; } }
public class Team { public int Id { get; set; } public string Emblem { get; set; } public string Name { get; set; } }
public class Match { public int Id { get; set; } public int Home { get; set; } public int Visitor { get; set; } public DateTime Date { get; set; } public int HomeGoals {get;set;} public int VisitorGoals{get;set;} public int HomeWinnigSets{get;set;} public int VisitorWinnigSets{get;set;} }

public static class Enum
{
    public enum Format { Knockout = 1, LeagueSystem = 2, GroupStage = 3 }
    public enum Sports { Football = 1, Volleyball = 2 }
}
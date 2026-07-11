public class ClassificationDTO
{
    public int Position { get; set; }
    public int Points { get; set; }
    public string Emblem { get; set; }
    public string Name { get; set; }
    public int TeamId { get; set; }
    public int Wins { get; set; }
    public int GoalBalance { get; set; }
    public int ProGoals { get; set; }
    public int AmountOfMatches { get; set; }
    public int RedCard { get; set; }
    public int YellowCard { get; set; }
    // Vôlei
    public int WinningSets { get; set; }
    public int LosingSets { get; set; }
    public int ProPoints { get; set; }
    public int PointsAgainst { get; set; }
    
    public List<MatchDTO> LastMatches { get; set; } = new();
    public List<LastResultsDTO> LastResults { get; set; } = new();
}
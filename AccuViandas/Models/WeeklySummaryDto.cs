namespace AccuViandas.Models
{
    public class WeeklySummaryDto
    {
        public string UserName { get; set; }
        public Dictionary<DayOfWeek, List<string>> DailySelections { get; set; }
        public string AllObservations { get; set; }
    }
}

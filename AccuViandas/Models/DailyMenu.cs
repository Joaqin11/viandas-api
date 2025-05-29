namespace AccuViandas.Models
{
    public class DailyMenu
    {
        public int Id { get; set; }
        public DateTime Date { get; set; }
        public List<DailyMenuItem> Items { get; set; } = new List<DailyMenuItem>();
    }
}

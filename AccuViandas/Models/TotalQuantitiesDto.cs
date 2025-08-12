namespace AccuViandas.Models
{
    public class TotalQuantitiesDto
    {
        public string Day { get; set; }
        public Dictionary<string, int> Categories { get; set; }
    }
}

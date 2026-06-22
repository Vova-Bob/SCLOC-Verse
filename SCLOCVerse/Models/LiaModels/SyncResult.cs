namespace SCLOCVerse.Models.LiaModels
{
    public class SyncResult
    {
        public List<string> Downloaded { get; set; } = new();
        public int ModifiedCount { get; set; } = 0;
        public int DeletedCount { get; set; } = 0;
    }
}

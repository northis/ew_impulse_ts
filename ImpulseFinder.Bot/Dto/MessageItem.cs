namespace ImpulseFinder.Bot.Dto
{
    public class MessageItem
    {
        public long ChatId { get; set; }

        public long UserId { get; set; }

        public string Command { get; set; }

        public MemoryStream Stream { get; set; }
        public long FileSize { get; set; }

        public string Text { get; set; }

        public string TextOnly { get; set; }
        public byte[] Picture { get; set; }
    }
}
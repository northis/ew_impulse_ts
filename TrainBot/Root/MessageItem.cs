namespace TrainBot.Root
{
    public class MessageItem
    {
        public long ChatId { get; set; }

        public long UserId { get; set; }

        public string Command { get; set; } = null!;

        public MemoryStream Stream { get; set; } = null!;
        public long FileSize { get; set; }

        public string Text { get; set; } = null!;

        public string TextOnly { get; set; } = null!;
        public byte[] Picture { get; set; } = null!;
    }
}
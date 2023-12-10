using Telegram.Bot.Types.ReplyMarkups;

namespace TrainBot.Commands.Common
{
    public class AnswerItem
    {
        public string Message { get; set; } = null!;
        public string PathMainImage { get; set; } = null!;
        public string[] PathImages { get; set; } = null!;

        public IReplyMarkup? Markup { get; set; } = null;
    }
}
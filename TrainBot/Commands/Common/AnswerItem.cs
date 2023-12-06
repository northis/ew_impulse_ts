using Telegram.Bot.Types.ReplyMarkups;

namespace TrainBot.Commands.Common
{
    public class AnswerItem
    {
        public string Message { get; set; } = null!;

        public IReplyMarkup Markup { get; set; } = null!;
    }
}
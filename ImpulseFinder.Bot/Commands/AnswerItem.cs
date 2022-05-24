using ImpulseFinder.Bot.Dto;
using Telegram.Bot.Types.ReplyMarkups;

namespace ImpulseFinder.Bot.Commands
{
    public class AnswerItem
    {
        public string Message { get; set; }

        public IReplyMarkup Markup { get; set; }

        public ImageResult Picture { get; set; }
    }
}
using Telegram.Bot.Types.ReplyMarkups;
using TrainBot.Commands.Common;
using TrainBot.Commands.Enums;
using TrainBot.Root;

namespace TrainBot.Commands
{
    public class AboutCommand : CommandBase
    {
        private readonly string m_MainAboutString;

        public AboutCommand(string mainAboutString)
        {
            m_MainAboutString = mainAboutString;
        }

        public override string GetCommandIconUnicode()
        {
            return "🈴";
        }

        public override string GetCommandTextDescription()
        {
            return "About this bot";
        }

        public override ECommands GetCommandType()
        {
            return ECommands.ABOUT;
        }

        public override AnswerItem Reply(MessageItem mItem)
        {
            return new AnswerItem
            {
                Message = m_MainAboutString,
                Markup = new ReplyKeyboardRemove()
            };
        }
    }
}
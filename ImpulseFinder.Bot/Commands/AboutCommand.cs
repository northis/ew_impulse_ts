using ImpulseFinder.Bot.Dto;
using Telegram.Bot.Types.ReplyMarkups;

namespace ImpulseFinder.Bot.Commands
{
    public class AboutCommand : CommandBase
    {
        private readonly string m_ReleaseNotes;
        private readonly string m_MainAboutString;

        public AboutCommand(string releaseNotes, string mainAboutString)
        {
            m_ReleaseNotes = releaseNotes;
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
            return ECommands.About;
        }

        public override AnswerItem Reply(MessageItem mItem)
        {
            return new AnswerItem
            {
                Message = m_MainAboutString + Environment.NewLine + m_ReleaseNotes,
                Markup = new ReplyKeyboardRemove()
            };
        }
    }
}
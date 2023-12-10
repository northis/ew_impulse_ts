using TrainBot.Commands.Common;
using TrainBot.Commands.Enums;
using TrainBot.Root;

namespace TrainBot.Commands
{
    public class HelpCommand : CommandBase
    {
        public HelpCommand(Func<CommandBase[]> getAllCommands)
        {
            GetAllCommands = getAllCommands;
        }

        protected Func<CommandBase[]> GetAllCommands { get; }

        public override string GetCommandIconUnicode()
        {
            return "❓";
        }

        public override string GetCommandTextDescription()
        {
            return "List of available commands";
        }

        public override ECommands GetCommandType()
        {
            return ECommands.HELP;
        }

        public virtual string GetHelpMessage()
        {
            return string.Join(Environment.NewLine, GetAllCommands().Select(a => a.GetFormattedDescription().ToLowerInvariant()));
        }

        public override AnswerItem Reply(MessageItem mItem)
        {
            return new AnswerItem
            {
                Message = GetHelpMessage()
            };
        }
    }
}